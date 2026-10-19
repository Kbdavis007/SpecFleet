using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SpecFleet.Service.Features.TaskOrchestration;
using SpecFleet.Service.Features.TaskQueue;
using SpecFleet.Service.Shared.Results;
using Testcontainers.PostgreSql;

namespace SpecFleet.Test.Service.Features.TaskOrchestration;

public sealed class WorkerTaskOrchestratorTests
{
    private const string Payload = """{"description":"Generate a worker"}""";
    private const string Target = "Kbdavis007/SpecFleet";

    [Fact]
    public async Task Every_supported_worker_category_is_enqueued_with_approval_metadata()
    {
        await using var database = await CreateDatabaseAsync();
        var queue = await CreateQueueAsync(database);
        var orchestrator = new WorkerTaskOrchestrator(queue);
        var taskTypes = Enum.GetValues<WorkerTaskType>();

        foreach (var taskType in taskTypes)
        {
            var request = NewRequest(taskType, $"request-{taskType}");

            var dispatched = SuccessValue(await orchestrator.DispatchAsync(request));
            var lease = Assert.IsType<TaskQueueLeaseAttempt.Leased>(
                SuccessValue(await queue.LeaseNextAsync("worker-1", TimeSpan.FromMinutes(1))));
            var status = SuccessValue(await queue.GetStatusAsync(dispatched.TaskId));
            using var envelope = JsonDocument.Parse(lease.Lease.Payload);
            var root = envelope.RootElement;

            Assert.Equal(taskType, dispatched.TaskType);
            Assert.Equal(TaskQueueStatus.Queued, dispatched.Status);
            Assert.Equal(TaskQueueStatus.Leased, status);
            Assert.Equal(taskType.ToString(), root.GetProperty("taskType").GetString());
            Assert.Equal("user-1", root.GetProperty("approvedBy").GetString());
            Assert.Equal($"approval-{taskType}", root.GetProperty("approvalReference").GetString());
            Assert.Equal(Target, root.GetProperty("target").GetString());
            using var payload = JsonDocument.Parse(root.GetProperty("payload").GetString()!);
            Assert.Equal(
                "Generate a worker",
                payload.RootElement.GetProperty("description").GetString());
            Assert.IsType<ServiceResult<bool>.Success>(
                await queue.CompleteAsync(lease.Lease));
        }
    }

    [Fact]
    public async Task Unsupported_task_category_is_rejected_without_enqueuing()
    {
        await using var database = await CreateDatabaseAsync();
        var queue = await CreateQueueAsync(database);
        var orchestrator = new WorkerTaskOrchestrator(queue);
        var request = NewRequest((WorkerTaskType)999, "unsupported-request");

        var result = await orchestrator.DispatchAsync(request);

        var failure = Assert.IsType<ServiceResult<WorkerTaskDispatch>.Failure>(result);
        Assert.Equal(ServiceErrorCode.Validation, failure.Error.Code);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task Payload_must_be_a_json_object(string payload)
    {
        await using var database = await CreateDatabaseAsync();
        var queue = await CreateQueueAsync(database);
        var orchestrator = new WorkerTaskOrchestrator(queue);
        var request = NewRequest(
            WorkerTaskType.GenerateWorker,
            "invalid-payload",
            payload: payload);

        var result = await orchestrator.DispatchAsync(request);

        var failure = Assert.IsType<ServiceResult<WorkerTaskDispatch>.Failure>(result);
        Assert.Equal(ServiceErrorCode.Validation, failure.Error.Code);
    }

    [Fact]
    public async Task Payload_must_match_the_approved_preview_digest()
    {
        await using var database = await CreateDatabaseAsync();
        var queue = await CreateQueueAsync(database);
        var orchestrator = new WorkerTaskOrchestrator(queue);
        var request = NewRequest(
            WorkerTaskType.GenerateWorker,
            "changed-payload",
            payload: """{"description":"Changed after approval"}""",
            approval: new WorkerTaskApproval(
                "user-1",
                "approval-generate-worker",
                WorkerTaskType.GenerateWorker,
                Digest(Payload),
                Target));

        var result = await orchestrator.DispatchAsync(request);

        var failure = Assert.IsType<ServiceResult<WorkerTaskDispatch>.Failure>(result);
        Assert.Equal(ServiceErrorCode.Conflict, failure.Error.Code);
    }

    [Fact]
    public async Task Dispatch_requires_an_authenticated_actor_and_approval_reference()
    {
        await using var database = await CreateDatabaseAsync();
        var queue = await CreateQueueAsync(database);
        var orchestrator = new WorkerTaskOrchestrator(queue);
        var request = NewRequest(
            WorkerTaskType.GenerateWorker,
            "missing-approval",
            approval: new WorkerTaskApproval(
                "",
                "",
                WorkerTaskType.GenerateWorker,
                Digest(Payload),
                Target));

        var result = await orchestrator.DispatchAsync(request);

        var failure = Assert.IsType<ServiceResult<WorkerTaskDispatch>.Failure>(result);
        Assert.Equal(ServiceErrorCode.Unauthorized, failure.Error.Code);
    }

    private static WorkerTaskDispatchRequest NewRequest(
        WorkerTaskType taskType,
        string idempotencyKey,
        string? payload = null,
        WorkerTaskApproval? approval = null)
    {
        var taskPayload = payload ?? Payload;
        return new WorkerTaskDispatchRequest(
            taskType,
            taskPayload,
            idempotencyKey,
            approval ?? new WorkerTaskApproval(
                "user-1",
                $"approval-{taskType}",
                taskType,
                Digest(taskPayload),
                Target));
    }

    private static string Digest(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    private static async Task<PostgreSqlContainer> CreateDatabaseAsync()
    {
        var database = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("specfleet_orchestrator_test")
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
