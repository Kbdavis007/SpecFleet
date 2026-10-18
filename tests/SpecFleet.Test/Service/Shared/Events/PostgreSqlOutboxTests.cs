using Npgsql;
using SpecFleet.Service.Shared.Events;
using SpecFleet.Service.Shared.Results;
using Testcontainers.PostgreSql;

namespace SpecFleet.Test.Service.Shared.Events;

public sealed class PostgreSqlOutboxTests
{
    [Fact]
    public async Task Event_is_persisted_and_delivered_once()
    {
        await using var database = await CreateDatabaseAsync();
        var outbox = new PostgreSqlOutbox(database.GetConnectionString());
        Assert.IsType<ServiceResult<bool>.Success>(await outbox.InitializeAsync());
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var domainEvent = NewEvent();

        var appendResult = await outbox.AppendAsync(domainEvent, connection, transaction);
        await transaction.CommitAsync();

        Assert.IsType<ServiceResult<Guid>.Success>(appendResult);
        var firstLease = await outbox.LeaseNextAsync("worker-1", TimeSpan.FromMinutes(1));
        var lease = Assert.IsType<OutboxLeaseAttempt.Leased>(SuccessValue(firstLease));

        Assert.Equal(domainEvent.Id, lease.Lease.EventId);
        Assert.Equal(typeof(WorkspaceCreated).FullName, lease.Lease.EventType);
        Assert.Contains("workspace-42", lease.Lease.Payload);
        Assert.IsType<ServiceResult<bool>.Success>(
            await outbox.CompleteAsync(lease.Lease));

        var nextLease = await outbox.LeaseNextAsync("worker-1", TimeSpan.FromMinutes(1));

        Assert.IsType<OutboxLeaseAttempt.Empty>(SuccessValue(nextLease));
    }

    [Fact]
    public async Task Failed_delivery_retries_and_becomes_terminal_after_attempt_limit()
    {
        await using var database = await CreateDatabaseAsync();
        var outbox = new PostgreSqlOutbox(database.GetConnectionString());
        Assert.IsType<ServiceResult<bool>.Success>(await outbox.InitializeAsync());
        await AppendEventAsync(outbox, database.GetConnectionString(), NewEvent());

        var firstLease = Assert.IsType<OutboxLeaseAttempt.Leased>(
            SuccessValue(await outbox.LeaseNextAsync("worker-1", TimeSpan.FromMinutes(1))));
        var firstFailure = Assert.IsType<ServiceResult<OutboxRetryResult>.Success>(
            await outbox.RetryAsync(firstLease.Lease, TimeSpan.Zero));
        Assert.Equal(OutboxDeliveryStatus.Pending, firstFailure.Value.Status);
        Assert.Equal(1, firstFailure.Value.Attempts);

        var secondLease = Assert.IsType<OutboxLeaseAttempt.Leased>(
            SuccessValue(await outbox.LeaseNextAsync("worker-1", TimeSpan.FromMinutes(1))));
        var secondFailure = Assert.IsType<ServiceResult<OutboxRetryResult>.Success>(
            await outbox.RetryAsync(secondLease.Lease, TimeSpan.Zero));
        Assert.Equal(OutboxDeliveryStatus.Pending, secondFailure.Value.Status);

        var thirdLease = Assert.IsType<OutboxLeaseAttempt.Leased>(
            SuccessValue(await outbox.LeaseNextAsync("worker-1", TimeSpan.FromMinutes(1))));
        var terminalFailure = Assert.IsType<ServiceResult<OutboxRetryResult>.Success>(
            await outbox.RetryAsync(thirdLease.Lease, TimeSpan.Zero));

        Assert.Equal(OutboxDeliveryStatus.Failed, terminalFailure.Value.Status);
        Assert.Equal(3, terminalFailure.Value.Attempts);
        Assert.IsType<OutboxLeaseAttempt.Empty>(
            SuccessValue(await outbox.LeaseNextAsync("worker-1", TimeSpan.FromMinutes(1))));
    }

    [Fact]
    public async Task Event_is_not_visible_when_the_domain_transaction_rolls_back()
    {
        await using var database = await CreateDatabaseAsync();
        var outbox = new PostgreSqlOutbox(database.GetConnectionString());
        Assert.IsType<ServiceResult<bool>.Success>(await outbox.InitializeAsync());
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            Assert.IsType<ServiceResult<Guid>.Success>(
                await outbox.AppendAsync(NewEvent(), connection, transaction));
        }

        Assert.IsType<OutboxLeaseAttempt.Empty>(
            SuccessValue(await outbox.LeaseNextAsync("worker-1", TimeSpan.FromMinutes(1))));
    }

    private static async Task<PostgreSqlContainer> CreateDatabaseAsync()
    {
        var database = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("specfleet_outbox_test")
            .WithUsername("postgres")
            .WithPassword("specfleet_test")
            .Build();
        await database.StartAsync();
        return database;
    }

    private static async Task AppendEventAsync(
        PostgreSqlOutbox outbox,
        string connectionString,
        IDomainEvent domainEvent)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        Assert.IsType<ServiceResult<Guid>.Success>(
            await outbox.AppendAsync(domainEvent, connection, transaction));
        await transaction.CommitAsync();
    }

    private static TValue SuccessValue<TValue>(ServiceResult<TValue> result)
        where TValue : notnull =>
        Assert.IsType<ServiceResult<TValue>.Success>(result).Value;

    private static WorkspaceCreated NewEvent() =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, "workspace-42");

    private sealed record WorkspaceCreated(
        Guid Id,
        DateTimeOffset OccurredAt,
        string WorkspaceId) : IDomainEvent;
}
