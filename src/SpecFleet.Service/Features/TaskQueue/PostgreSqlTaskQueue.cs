using System.Text.Json;
using Npgsql;
using SpecFleet.Service.Shared.Events;
using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Service.Features.TaskQueue;

public sealed class PostgreSqlTaskQueue(string connectionString)
{
    private readonly PostgreSqlOutbox _outbox = new(connectionString);

    public async Task<ServiceResult<bool>> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        var outboxResult = await _outbox.InitializeAsync(cancellationToken);
        if (outboxResult is ServiceResult<bool>.Failure outboxFailure)
        {
            return ServiceResult<bool>.FromError(outboxFailure.Error);
        }

        const string sql = """
            CREATE TABLE IF NOT EXISTS specfleet_tasks (
                task_id UUID PRIMARY KEY,
                task_type TEXT NOT NULL,
                payload JSONB NOT NULL,
                idempotency_key TEXT NOT NULL UNIQUE,
                status TEXT NOT NULL CHECK (status IN ('Queued', 'Leased', 'Succeeded', 'Failed')),
                attempts INTEGER NOT NULL DEFAULT 0 CHECK (attempts >= 0),
                maximum_attempts INTEGER NOT NULL CHECK (maximum_attempts > 0),
                available_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                lease_owner TEXT NULL,
                lease_until TIMESTAMPTZ NULL,
                succeeded_at TIMESTAMPTZ NULL,
                failed_at TIMESTAMPTZ NULL,
                last_error TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_specfleet_tasks_available
                ON specfleet_tasks (available_at, created_at, task_id)
                WHERE status IN ('Queued', 'Leased');
            """;

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return ServiceResult<bool>.FromValue(true);
        }
        catch (NpgsqlException)
        {
            return QueueUnavailable<bool>();
        }
    }

    public async Task<ServiceResult<EnqueuedTask>> EnqueueAsync(
        string taskType,
        string payload,
        string idempotencyKey,
        int maximumAttempts = 3,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskType) ||
            string.IsNullOrWhiteSpace(idempotencyKey) ||
            maximumAttempts < 1 ||
            !IsJson(payload))
        {
            return ServiceResult<EnqueuedTask>.FromError(
                new ServiceError(ServiceErrorCode.Validation, "Task type, JSON payload, idempotency key, and positive retry limit are required."));
        }

        const string insertSql = """
            INSERT INTO specfleet_tasks (
                task_id, task_type, payload, idempotency_key, status, maximum_attempts)
            VALUES (
                @task_id, @task_type, @payload::jsonb, @idempotency_key, 'Queued', @maximum_attempts)
            ON CONFLICT (idempotency_key) DO NOTHING
            RETURNING task_id, task_type, payload::text, idempotency_key, status, attempts, maximum_attempts;
            """;

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var taskId = Guid.NewGuid();
            EnqueuedTask? enqueuedTask;

            await using (var command = new NpgsqlCommand(insertSql, connection, transaction))
            {
                command.Parameters.AddWithValue("task_id", taskId);
                command.Parameters.AddWithValue("task_type", taskType);
                command.Parameters.AddWithValue("payload", payload);
                command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
                command.Parameters.AddWithValue("maximum_attempts", maximumAttempts);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                enqueuedTask = await reader.ReadAsync(cancellationToken)
                    ? ReadEnqueuedTask(reader)
                    : null;
            }

            if (enqueuedTask is not null)
            {
                var eventResult = await _outbox.AppendAsync(
                    new TaskQueued(taskId, DateTimeOffset.UtcNow, taskId, taskType, idempotencyKey),
                    connection,
                    transaction,
                    cancellationToken);
                if (eventResult is ServiceResult<Guid>.Failure eventFailure)
                {
                    return ServiceResult<EnqueuedTask>.FromError(eventFailure.Error);
                }

                await transaction.CommitAsync(cancellationToken);
                return ServiceResult<EnqueuedTask>.FromValue(enqueuedTask);
            }

            const string findSql = """
                SELECT task_id, task_type, payload::text, idempotency_key, status, attempts,
                       maximum_attempts, task_type = @task_type AND payload = @payload::jsonb
                FROM specfleet_tasks
                WHERE idempotency_key = @idempotency_key
                FOR UPDATE;
                """;

            await using var findCommand = new NpgsqlCommand(findSql, connection, transaction);
            findCommand.Parameters.AddWithValue("task_type", taskType);
            findCommand.Parameters.AddWithValue("payload", payload);
            findCommand.Parameters.AddWithValue("idempotency_key", idempotencyKey);
            await using var findReader = await findCommand.ExecuteReaderAsync(cancellationToken);
            if (!await findReader.ReadAsync(cancellationToken))
            {
                return ServiceResult<EnqueuedTask>.FromError(
                    new ServiceError(ServiceErrorCode.Conflict, "The idempotency key could not be resolved."));
            }

            var existingTask = ReadEnqueuedTask(findReader);
            var contentMatches = findReader.GetBoolean(7);
            await findReader.CloseAsync();
            await transaction.CommitAsync(cancellationToken);
            return contentMatches
                ? ServiceResult<EnqueuedTask>.FromValue(existingTask)
                : ServiceResult<EnqueuedTask>.FromError(
                    new ServiceError(ServiceErrorCode.Conflict, "The idempotency key already identifies different task content."));
        }
        catch (NpgsqlException)
        {
            return QueueUnavailable<EnqueuedTask>();
        }
    }

    public async Task<ServiceResult<TaskQueueLeaseAttempt>> LeaseNextAsync(
        string workerId,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workerId) || visibilityTimeout <= TimeSpan.Zero)
        {
            return ServiceResult<TaskQueueLeaseAttempt>.FromError(
                new ServiceError(ServiceErrorCode.Validation, "A worker id and positive visibility timeout are required."));
        }

        const string leaseSql = """
            WITH next_task AS (
                SELECT task_id
                FROM specfleet_tasks
                WHERE (status = 'Queued' AND available_at <= NOW())
                   OR (status = 'Leased' AND lease_until <= NOW() AND attempts < maximum_attempts)
                ORDER BY available_at, created_at, task_id
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            UPDATE specfleet_tasks AS task
            SET status = 'Leased',
                lease_owner = @worker_id,
                lease_until = NOW() + @visibility_timeout,
                attempts = attempts + 1
            FROM next_task
            WHERE task.task_id = next_task.task_id
            RETURNING task.task_id, task.task_type, task.payload::text, task.attempts,
                      task.maximum_attempts, task.lease_owner, task.lease_until;
            """;

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var expiredFailureResult = await FailExhaustedLeasesAsync(connection, transaction, cancellationToken);
            if (expiredFailureResult is ServiceResult<bool>.Failure expiredFailure)
            {
                return ServiceResult<TaskQueueLeaseAttempt>.FromError(expiredFailure.Error);
            }

            await using var command = new NpgsqlCommand(leaseSql, connection, transaction);
            command.Parameters.AddWithValue("worker_id", workerId);
            command.Parameters.AddWithValue("visibility_timeout", visibilityTimeout);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await reader.CloseAsync();
                await transaction.CommitAsync(cancellationToken);
                return ServiceResult<TaskQueueLeaseAttempt>.FromValue(new TaskQueueLeaseAttempt.Empty());
            }

            var lease = new TaskQueueLease(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.GetFieldValue<DateTimeOffset>(6));
            await reader.CloseAsync();
            await transaction.CommitAsync(cancellationToken);
            return ServiceResult<TaskQueueLeaseAttempt>.FromValue(new TaskQueueLeaseAttempt.Leased(lease));
        }
        catch (NpgsqlException)
        {
            return QueueUnavailable<TaskQueueLeaseAttempt>();
        }
    }

    public async Task<ServiceResult<bool>> CompleteAsync(
        TaskQueueLease lease,
        CancellationToken cancellationToken = default)
    {
        if (lease is null)
        {
            return ServiceResult<bool>.FromError(
                new ServiceError(ServiceErrorCode.Validation, "A task lease is required."));
        }

        const string sql = """
            UPDATE specfleet_tasks
            SET status = 'Succeeded',
                succeeded_at = NOW(),
                lease_owner = NULL,
                lease_until = NULL
            WHERE task_id = @task_id
              AND status = 'Leased'
              AND lease_owner = @worker_id
              AND attempts = @attempts
            RETURNING task_type;
            """;

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            AddLeaseParameters(command, lease);
            var taskType = await command.ExecuteScalarAsync(cancellationToken) as string;
            if (taskType is null)
            {
                return LeaseConflict<bool>();
            }

            var eventResult = await _outbox.AppendAsync(
                new TaskCompleted(Guid.NewGuid(), DateTimeOffset.UtcNow, lease.Id, taskType, lease.Attempts),
                connection,
                transaction,
                cancellationToken);
            if (eventResult is ServiceResult<Guid>.Failure eventFailure)
            {
                return ServiceResult<bool>.FromError(eventFailure.Error);
            }

            await transaction.CommitAsync(cancellationToken);
            return ServiceResult<bool>.FromValue(true);
        }
        catch (NpgsqlException)
        {
            return QueueUnavailable<bool>();
        }
    }

    public async Task<ServiceResult<TaskQueueRetryResult>> RetryAsync(
        TaskQueueLease lease,
        TimeSpan retryDelay,
        string failureReason = "Task processing failed.",
        CancellationToken cancellationToken = default)
    {
        if (lease is null || retryDelay < TimeSpan.Zero || string.IsNullOrWhiteSpace(failureReason))
        {
            return ServiceResult<TaskQueueRetryResult>.FromError(
                new ServiceError(ServiceErrorCode.Validation, "A task lease, non-negative retry delay, and failure reason are required."));
        }

        const string sql = """
            UPDATE specfleet_tasks
            SET status = CASE WHEN attempts >= maximum_attempts THEN 'Failed' ELSE 'Queued' END,
                available_at = CASE
                    WHEN attempts >= maximum_attempts THEN available_at
                    ELSE NOW() + @retry_delay
                END,
                failed_at = CASE WHEN attempts >= maximum_attempts THEN NOW() ELSE NULL END,
                last_error = @failure_reason,
                lease_owner = NULL,
                lease_until = NULL
            WHERE task_id = @task_id
              AND status = 'Leased'
              AND lease_owner = @worker_id
              AND attempts = @attempts
            RETURNING task_type, status, attempts;
            """;

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            AddLeaseParameters(command, lease);
            command.Parameters.AddWithValue("retry_delay", retryDelay);
            command.Parameters.AddWithValue("failure_reason", failureReason);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return LeaseConflict<TaskQueueRetryResult>();
            }

            var taskType = reader.GetString(0);
            var status = Enum.Parse<TaskQueueStatus>(reader.GetString(1));
            var attempts = reader.GetInt32(2);
            await reader.CloseAsync();

            if (status is TaskQueueStatus.Failed)
            {
                var eventResult = await _outbox.AppendAsync(
                    new TaskFailed(
                        Guid.NewGuid(),
                        DateTimeOffset.UtcNow,
                        lease.Id,
                        taskType,
                        attempts,
                        failureReason),
                    connection,
                    transaction,
                    cancellationToken);
                if (eventResult is ServiceResult<Guid>.Failure eventFailure)
                {
                    return ServiceResult<TaskQueueRetryResult>.FromError(eventFailure.Error);
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return ServiceResult<TaskQueueRetryResult>.FromValue(new TaskQueueRetryResult(status, attempts));
        }
        catch (NpgsqlException)
        {
            return QueueUnavailable<TaskQueueRetryResult>();
        }
    }

    public async Task<ServiceResult<TaskQueueStatus>> GetStatusAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT status FROM specfleet_tasks WHERE task_id = @task_id;";

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("task_id", taskId);
            var status = await command.ExecuteScalarAsync(cancellationToken) as string;
            return status is null
                ? ServiceResult<TaskQueueStatus>.FromError(
                    new ServiceError(ServiceErrorCode.NotFound, "The task does not exist."))
                : ServiceResult<TaskQueueStatus>.FromValue(Enum.Parse<TaskQueueStatus>(status));
        }
        catch (NpgsqlException)
        {
            return QueueUnavailable<TaskQueueStatus>();
        }
    }

    private async Task<ServiceResult<bool>> FailExhaustedLeasesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE specfleet_tasks
            SET status = 'Failed',
                failed_at = NOW(),
                last_error = 'Visibility lease expired after the maximum number of attempts.',
                lease_owner = NULL,
                lease_until = NULL
            WHERE status = 'Leased'
              AND lease_until <= NOW()
              AND attempts >= maximum_attempts
            RETURNING task_id, task_type, attempts, last_error;
            """;

        var failedTasks = new List<TaskFailed>();
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                failedTasks.Add(new TaskFailed(
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3)));
            }
        }

        foreach (var failedTask in failedTasks)
        {
            var eventResult = await _outbox.AppendAsync(
                failedTask,
                connection,
                transaction,
                cancellationToken);
            if (eventResult is ServiceResult<Guid>.Failure eventFailure)
            {
                return ServiceResult<bool>.FromError(eventFailure.Error);
            }
        }

        return ServiceResult<bool>.FromValue(true);
    }

    private static EnqueuedTask ReadEnqueuedTask(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            Enum.Parse<TaskQueueStatus>(reader.GetString(4)),
            reader.GetInt32(5),
            reader.GetInt32(6));

    private static void AddLeaseParameters(NpgsqlCommand command, TaskQueueLease lease)
    {
        command.Parameters.AddWithValue("task_id", lease.Id);
        command.Parameters.AddWithValue("worker_id", lease.WorkerId);
        command.Parameters.AddWithValue("attempts", lease.Attempts);
    }

    private static bool IsJson(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ServiceResult<TValue> LeaseConflict<TValue>()
        where TValue : notnull =>
        ServiceResult<TValue>.FromError(
            new ServiceError(ServiceErrorCode.Conflict, "The task lease is no longer current."));

    private static ServiceResult<TValue> QueueUnavailable<TValue>()
        where TValue : notnull =>
        ServiceResult<TValue>.FromError(
            new ServiceError(
                ServiceErrorCode.QueueUnavailable,
                "The PostgreSQL task queue is unavailable.",
                IsRetryable: true));
}
