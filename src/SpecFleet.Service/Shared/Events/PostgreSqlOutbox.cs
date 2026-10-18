using System.Text.Json;
using Npgsql;
using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Service.Shared.Events;

public sealed class PostgreSqlOutbox(string connectionString)
{
    private const int MaximumAttempts = 3;

    public async Task<ServiceResult<bool>> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS specfleet_outbox (
                event_id UUID PRIMARY KEY,
                event_type TEXT NOT NULL,
                payload JSONB NOT NULL,
                occurred_at TIMESTAMPTZ NOT NULL,
                status TEXT NOT NULL CHECK (status IN ('Pending', 'Delivered', 'Failed')),
                attempts INTEGER NOT NULL DEFAULT 0,
                available_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                lease_owner TEXT NULL,
                lease_until TIMESTAMPTZ NULL,
                delivered_at TIMESTAMPTZ NULL
            );
            CREATE INDEX IF NOT EXISTS ix_specfleet_outbox_pending
                ON specfleet_outbox (available_at, occurred_at)
                WHERE status = 'Pending';
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

    public async Task<ServiceResult<Guid>> AppendAsync(
        IDomainEvent domainEvent,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        if (domainEvent is null ||
            connection is null ||
            transaction is null ||
            connection.State != System.Data.ConnectionState.Open ||
            transaction.Connection != connection ||
            string.IsNullOrWhiteSpace(domainEvent.GetType().FullName))
        {
            return ServiceResult<Guid>.FromError(
                new ServiceError(ServiceErrorCode.Validation, "A valid event and active transaction are required."));
        }

        string payload;
        try
        {
            payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType());
        }
        catch (NotSupportedException)
        {
            return ServiceResult<Guid>.FromError(
                new ServiceError(ServiceErrorCode.Validation, "The domain event cannot be serialized."));
        }
        catch (JsonException)
        {
            return ServiceResult<Guid>.FromError(
                new ServiceError(ServiceErrorCode.Validation, "The domain event cannot be serialized."));
        }

        const string sql = """
            INSERT INTO specfleet_outbox (
                event_id, event_type, payload, occurred_at, status)
            VALUES (
                @event_id, @event_type, @payload::jsonb, @occurred_at, 'Pending')
            ON CONFLICT (event_id) DO UPDATE
                SET event_id = EXCLUDED.event_id
                WHERE specfleet_outbox.event_type = EXCLUDED.event_type
                  AND specfleet_outbox.payload = EXCLUDED.payload
                  AND specfleet_outbox.occurred_at = EXCLUDED.occurred_at
            RETURNING event_id;
            """;

        try
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("event_id", domainEvent.Id);
            command.Parameters.AddWithValue("event_type", domainEvent.GetType().FullName!);
            command.Parameters.AddWithValue("payload", payload);
            command.Parameters.AddWithValue("occurred_at", domainEvent.OccurredAt);
            var savedId = await command.ExecuteScalarAsync(cancellationToken);
            return savedId is Guid id
                ? ServiceResult<Guid>.FromValue(id)
                : ServiceResult<Guid>.FromError(
                    new ServiceError(ServiceErrorCode.Conflict, "The event id already identifies different content."));
        }
        catch (NpgsqlException)
        {
            return QueueUnavailable<Guid>();
        }
    }

    public async Task<ServiceResult<OutboxLeaseAttempt>> LeaseNextAsync(
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workerId) || leaseDuration <= TimeSpan.Zero)
        {
            return ServiceResult<OutboxLeaseAttempt>.FromError(
                new ServiceError(ServiceErrorCode.Validation, "A worker id and positive lease duration are required."));
        }

        const string sql = """
            WITH next_event AS (
                SELECT event_id
                FROM specfleet_outbox
                WHERE status = 'Pending'
                  AND available_at <= NOW()
                  AND (lease_until IS NULL OR lease_until <= NOW())
                ORDER BY occurred_at, event_id
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            UPDATE specfleet_outbox AS outbox
            SET lease_owner = @worker_id,
                lease_until = NOW() + @lease_duration,
                attempts = attempts + 1
            FROM next_event
            WHERE outbox.event_id = next_event.event_id
            RETURNING outbox.event_id, outbox.event_type, outbox.payload::text,
                      outbox.attempts, outbox.lease_owner, outbox.lease_until;
            """;

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("worker_id", workerId);
            command.Parameters.AddWithValue("lease_duration", leaseDuration);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await reader.CloseAsync();
                await transaction.CommitAsync(cancellationToken);
                return ServiceResult<OutboxLeaseAttempt>.FromValue(new OutboxLeaseAttempt.Empty());
            }

            var lease = new OutboxLease(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5));
            await reader.CloseAsync();
            await transaction.CommitAsync(cancellationToken);
            return ServiceResult<OutboxLeaseAttempt>.FromValue(new OutboxLeaseAttempt.Leased(lease));
        }
        catch (NpgsqlException)
        {
            return QueueUnavailable<OutboxLeaseAttempt>();
        }
    }

    public async Task<ServiceResult<bool>> CompleteAsync(
        OutboxLease lease,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE specfleet_outbox
            SET status = 'Delivered',
                delivered_at = NOW(),
                lease_owner = NULL,
                lease_until = NULL
            WHERE event_id = @event_id
              AND status = 'Pending'
              AND lease_owner = @worker_id
              AND attempts = @attempts;
            """;

        var result = await UpdateLeaseAsync(lease, sql, cancellationToken);
        return result.Match(
            updated => updated
                ? ServiceResult<bool>.FromValue(true)
                : ServiceResult<bool>.FromError(new ServiceError(ServiceErrorCode.Conflict, "The outbox lease is no longer current.")),
            ServiceResult<bool>.FromError);
    }

    public async Task<ServiceResult<OutboxRetryResult>> RetryAsync(
        OutboxLease lease,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default)
    {
        if (retryDelay < TimeSpan.Zero)
        {
            return ServiceResult<OutboxRetryResult>.FromError(
                new ServiceError(ServiceErrorCode.Validation, "Retry delay cannot be negative."));
        }

        const string sql = """
            UPDATE specfleet_outbox
            SET status = CASE WHEN attempts >= @maximum_attempts THEN 'Failed' ELSE 'Pending' END,
                available_at = NOW() + @retry_delay,
                lease_owner = NULL,
                lease_until = NULL
            WHERE event_id = @event_id
              AND status = 'Pending'
              AND lease_owner = @worker_id
              AND attempts = @attempts
            RETURNING status, attempts;
            """;

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            AddLeaseParameters(command, lease);
            command.Parameters.AddWithValue("maximum_attempts", MaximumAttempts);
            command.Parameters.AddWithValue("retry_delay", retryDelay);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var status = Enum.Parse<OutboxDeliveryStatus>(reader.GetString(0));
                var attempts = reader.GetInt32(1);
                return ServiceResult<OutboxRetryResult>.FromValue(new OutboxRetryResult(status, attempts));
            }

            return ServiceResult<OutboxRetryResult>.FromError(
                new ServiceError(ServiceErrorCode.Conflict, "The outbox lease is no longer current."));
        }
        catch (NpgsqlException)
        {
            return QueueUnavailable<OutboxRetryResult>();
        }
    }

    private async Task<ServiceResult<bool>> UpdateLeaseAsync(
        OutboxLease lease,
        string sql,
        CancellationToken cancellationToken)
    {
        if (lease is null)
        {
            return ServiceResult<bool>.FromError(
                new ServiceError(ServiceErrorCode.Validation, "An outbox lease is required."));
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            AddLeaseParameters(command, lease);
            var updatedCount = await command.ExecuteNonQueryAsync(cancellationToken);
            return ServiceResult<bool>.FromValue(updatedCount == 1);
        }
        catch (NpgsqlException)
        {
            return QueueUnavailable<bool>();
        }
    }

    private static void AddLeaseParameters(NpgsqlCommand command, OutboxLease lease)
    {
        command.Parameters.AddWithValue("event_id", lease.EventId);
        command.Parameters.AddWithValue("worker_id", lease.WorkerId);
        command.Parameters.AddWithValue("attempts", lease.Attempts);
    }

    private static ServiceResult<TValue> QueueUnavailable<TValue>()
        where TValue : notnull =>
        ServiceResult<TValue>.FromError(
            new ServiceError(
                ServiceErrorCode.QueueUnavailable,
                "The PostgreSQL outbox is unavailable.",
                IsRetryable: true));
}
