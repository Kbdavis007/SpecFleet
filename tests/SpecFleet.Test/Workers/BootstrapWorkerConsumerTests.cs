using SpecFleet.Service.Features.TaskQueue;
using SpecFleet.Service.Shared.Results;
using SpecFleet.Workers.Consumers;
using Testcontainers.PostgreSql;

namespace SpecFleet.Test.Workers;

public sealed class BootstrapWorkerConsumerTests
{
    [Fact]
    public async Task Consumer_completes_a_successful_task_and_marks_it_succeeded()
    {
        await using var database = await CreateDatabaseAsync();
        var queue = await CreateQueueAsync(database);
        var task = SuccessValue(await queue.EnqueueAsync(
            "generate-worker",
            """{"taskType":"generate-worker","payload":{"description":"Generate a worker"},"shouldFail":false}""",
            "worker-success"));

        using var consumer = new BootstrapWorkerConsumer(queue, TimeSpan.FromMilliseconds(50));
        await consumer.StartAsync(CancellationToken.None);
        await WaitForStatusAsync(queue, task.Id, TaskQueueStatus.Succeeded, TimeSpan.FromSeconds(5));
        await consumer.StopAsync(CancellationToken.None);

        Assert.Equal(TaskQueueStatus.Succeeded, SuccessValue(await queue.GetStatusAsync(task.Id)));
    }

    [Fact]
    public async Task Consumer_retries_and_then_marks_a_failed_task_as_failed()
    {
        await using var database = await CreateDatabaseAsync();
        var queue = await CreateQueueAsync(database);
        var task = SuccessValue(await queue.EnqueueAsync(
            "generate-worker",
            """{"taskType":"generate-worker","payload":{"description":"Generate a worker"},"shouldFail":true}""",
            "worker-failure",
            maximumAttempts: 2));

        using var consumer = new BootstrapWorkerConsumer(queue, TimeSpan.FromMilliseconds(50));
        await consumer.StartAsync(CancellationToken.None);
        await WaitForStatusAsync(queue, task.Id, TaskQueueStatus.Failed, TimeSpan.FromSeconds(5));
        await consumer.StopAsync(CancellationToken.None);

        Assert.Equal(TaskQueueStatus.Failed, SuccessValue(await queue.GetStatusAsync(task.Id)));
    }

    private static async Task<PostgreSqlContainer> CreateDatabaseAsync()
    {
        var database = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("specfleet_bootstrap_worker_test")
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

    private static async Task WaitForStatusAsync(
        PostgreSqlTaskQueue queue,
        Guid taskId,
        TaskQueueStatus expectedStatus,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            var status = SuccessValue(await queue.GetStatusAsync(taskId));
            if (status == expectedStatus)
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new Xunit.Sdk.XunitException($"Expected {expectedStatus} but the task never reached it.");
    }

    private static TValue SuccessValue<TValue>(ServiceResult<TValue> result)
        where TValue : notnull =>
        Assert.IsType<ServiceResult<TValue>.Success>(result).Value;
}
