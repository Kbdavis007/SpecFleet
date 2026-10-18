using Npgsql;
using SpecFleet.Service.Features.TaskQueue;
using SpecFleet.Service.Shared.Events;
using SpecFleet.Service.Shared.Results;
using Testcontainers.PostgreSql;

namespace SpecFleet.Test.Service.Features.TaskQueue;

public sealed class PostgreSqlTaskQueueTests
{
    [Fact]
    public async Task Enqueue_is_idempotent_for_matching_content_and_rejects_key_reuse()
    {
        await using var database = await CreateDatabaseAsync();
        var queue = await CreateQueueAsync(database);

        var first = SuccessValue(await queue.EnqueueAsync(
            "generate-worker",
            """{"name":"terraform"}""",
            "request-1"));
        var repeated = SuccessValue(await queue.EnqueueAsync(
            "generate-worker",
            """{"name":"terraform"}""",
            "request-1"));
        var conflicting = await queue.EnqueueAsync(
            "generate-worker",
            """{"name":"rabbitmq"}""",
            "request-1");

        Assert.Equal(first.Id, repeated.Id);
        Assert.IsType<ServiceResult<EnqueuedTask>.Failure>(conflicting);
        var lease = Assert.IsType<TaskQueueLeaseAttempt.Leased>(
            SuccessValue(await queue.LeaseNextAsync("worker-1", TimeSpan.FromMinutes(1))));
        Assert.IsType<ServiceResult<bool>.Success>(
            await queue.CompleteAsync(lease.Lease));
    }

    [Fact]
    public async Task Concurrent_consumers_lease_distinct_tasks()
    {
        await using var database = await CreateDatabaseAsync();
        var queue = await CreateQueueAsync(database);
        await queue.EnqueueAsync("generate-worker", """{"name":"first"}""", "request-1");
        await queue.EnqueueAsync("generate-worker", """{"name":"second"}""", "request-2");

        var leases = await Task.WhenAll(
            queue.LeaseNextAsync("worker-1", TimeSpan.FromMinutes(1)),
            queue.LeaseNextAsync("worker-2", TimeSpan.FromMinutes(1)));
        var firstLease = Assert.IsType<TaskQueueLeaseAttempt.Leased>(SuccessValue(leases[0]));
        var secondLease = Assert.IsType<TaskQueueLeaseAttempt.Leased>(SuccessValue(leases[1]));

        Assert.NotEqual(firstLease.Lease.Id, secondLease.Lease.Id);
    }

    [Fact]
    public async Task Concurrent_idempotent_enqueue_returns_one_durable_task()
    {
        await using var database = await CreateDatabaseAsync();
        var queue = await CreateQueueAsync(database);

        var results = await Task.WhenAll(
            queue.EnqueueAsync("generate-worker", """{"name":"terraform"}""", "request-1"),
            queue.EnqueueAsync("generate-worker", """{"name":"terraform"}""", "request-1"));

        Assert.All(results, result => Assert.IsType<ServiceResult<EnqueuedTask>.Success>(result));
        var tasks = results
            .Select(result => SuccessValue(result))
            .ToArray();
        Assert.Equal(tasks[0].Id, tasks[1].Id);
    }

    [Fact]
    public async Task Expired_visibility_lease_can_be_recovered_by_another_consumer()
    {
        await using var database = await CreateDatabaseAsync();
        var queue = await CreateQueueAsync(database);
        await queue.EnqueueAsync("generate-worker", """{"name":"terraform"}""", "request-1");
        var firstLease = Assert.IsType<TaskQueueLeaseAttempt.Leased>(
            SuccessValue(await queue.LeaseNextAsync("worker-1", TimeSpan.FromMilliseconds(100))));

        await Task.Delay(TimeSpan.FromMilliseconds(150));
        var recoveredLease = Assert.IsType<TaskQueueLeaseAttempt.Leased>(
            SuccessValue(await queue.LeaseNextAsync("worker-2", TimeSpan.FromMinutes(1))));

        Assert.Equal(firstLease.Lease.Id, recoveredLease.Lease.Id);
        Assert.Equal(2, recoveredLease.Lease.Attempts);
        Assert.Equal("worker-2", recoveredLease.Lease.WorkerId);
    }

    [Fact]
    public async Task Retry_policy_ends_in_a_terminal_failure_after_maximum_attempts()
    {
        await using var database = await CreateDatabaseAsync();
        var queue = await CreateQueueAsync(database);
        var task = SuccessValue(await queue.EnqueueAsync(
            "generate-worker",
            """{"name":"terraform"}""",
            "request-1",
            maximumAttempts: 2));

        var firstLease = Assert.IsType<TaskQueueLeaseAttempt.Leased>(
            SuccessValue(await queue.LeaseNextAsync("worker-1", TimeSpan.FromMinutes(1))));
        var firstRetry = SuccessValue(await queue.RetryAsync(firstLease.Lease, TimeSpan.Zero));
        Assert.Equal(TaskQueueStatus.Queued, firstRetry.Status);

        var secondLease = Assert.IsType<TaskQueueLeaseAttempt.Leased>(
            SuccessValue(await queue.LeaseNextAsync("worker-1", TimeSpan.FromMinutes(1))));
        var terminalRetry = SuccessValue(await queue.RetryAsync(secondLease.Lease, TimeSpan.Zero));

        Assert.Equal(TaskQueueStatus.Failed, terminalRetry.Status);
        Assert.Equal(2, terminalRetry.Attempts);
        Assert.Equal(TaskQueueStatus.Failed, SuccessValue(await queue.GetStatusAsync(task.Id)));
        Assert.IsType<TaskQueueLeaseAttempt.Empty>(
            SuccessValue(await queue.LeaseNextAsync("worker-1", TimeSpan.FromMinutes(1))));
    }

    [Fact]
    public async Task Task_transitions_and_outbox_events_are_persisted_together()
    {
        await using var database = await CreateDatabaseAsync();
        var queue = await CreateQueueAsync(database);
        var outbox = new PostgreSqlOutbox(database.GetConnectionString());
        var task = SuccessValue(await queue.EnqueueAsync(
            "generate-worker",
            """{"name":"terraform"}""",
            "request-1"));

        var queuedEvent = Assert.IsType<OutboxLeaseAttempt.Leased>(
            SuccessValue(await outbox.LeaseNextAsync("event-worker", TimeSpan.FromMinutes(1))));
        Assert.Contains(nameof(TaskQueued), queuedEvent.Lease.EventType);
        Assert.IsType<ServiceResult<bool>.Success>(await outbox.CompleteAsync(queuedEvent.Lease));

        var taskLease = Assert.IsType<TaskQueueLeaseAttempt.Leased>(
            SuccessValue(await queue.LeaseNextAsync("worker-1", TimeSpan.FromMinutes(1))));
        Assert.IsType<ServiceResult<bool>.Success>(await queue.CompleteAsync(taskLease.Lease));

        var completedEvent = Assert.IsType<OutboxLeaseAttempt.Leased>(
            SuccessValue(await outbox.LeaseNextAsync("event-worker", TimeSpan.FromMinutes(1))));
        Assert.Contains(nameof(TaskCompleted), completedEvent.Lease.EventType);
        Assert.Equal(TaskQueueStatus.Succeeded, SuccessValue(await queue.GetStatusAsync(task.Id)));
    }

    [Fact]
    public async Task Enqueue_rolls_back_when_the_transactional_outbox_cannot_record_its_event()
    {
        await using var database = await CreateDatabaseAsync();
        var queue = await CreateQueueAsync(database);
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand(
            """
            CREATE FUNCTION reject_outbox_insert() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'outbox write unavailable';
            END;
            $$;
            CREATE TRIGGER reject_outbox_insert
            BEFORE INSERT ON specfleet_outbox
            FOR EACH ROW EXECUTE FUNCTION reject_outbox_insert();
            """,
            connection))
        {
            await command.ExecuteNonQueryAsync();
        }

        var result = await queue.EnqueueAsync(
            "generate-worker",
            """{"name":"terraform"}""",
            "request-1");
        await using var countCommand = new NpgsqlCommand(
            "SELECT COUNT(*) FROM specfleet_tasks WHERE idempotency_key = @key;",
            connection);
        countCommand.Parameters.AddWithValue("key", "request-1");

        var failure = Assert.IsType<ServiceResult<EnqueuedTask>.Failure>(result);
        Assert.Equal(ServiceErrorCode.QueueUnavailable, failure.Error.Code);
        Assert.Equal(0L, (long)(await countCommand.ExecuteScalarAsync())!);
    }

    private static async Task<PostgreSqlContainer> CreateDatabaseAsync()
    {
        var database = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("specfleet_queue_test")
            .WithUsername("postgres")
            .WithPassword("specfleet_test")
            .Build();
        await database.StartAsync();
        return database;
    }

    private static async Task<PostgreSqlTaskQueue> CreateQueueAsync(PostgreSqlContainer database)
    {
        var queue = new PostgreSqlTaskQueue(database.GetConnectionString());
        Assert.IsType<ServiceResult<bool>.Success>(await queue.InitializeAsync());
        return queue;
    }

    private static TValue SuccessValue<TValue>(ServiceResult<TValue> result)
        where TValue : notnull =>
        Assert.IsType<ServiceResult<TValue>.Success>(result).Value;
}
