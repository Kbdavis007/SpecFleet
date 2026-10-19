using SpecFleet.Service.Features.TaskOrchestration;
using SpecFleet.Service.Features.TaskQueue;
using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Service.Features.WorkerTasks;

/// <summary>
/// Service-owned worker task use case. It validates typed payloads, dispatches approved work
/// through the durable orchestrator, and reports observable status without representing an
/// incomplete task as finished.
/// </summary>
public sealed class WorkerTaskHandler(
    WorkerTaskOrchestrator orchestrator,
    WorkerTaskPayloadValidator validator,
    PostgreSqlTaskQueue queue)
{
    public async Task<ServiceResult<WorkerTaskDispatch>> DispatchAsync(
        WorkerTaskType taskType,
        string payload,
        string idempotencyKey,
        WorkerTaskApproval approval,
        CancellationToken cancellationToken = default)
    {
        var preview = validator.Preview(taskType, payload);
        if (preview is ServiceResult<WorkerTaskPreview>.Failure failure)
        {
            return ServiceResult<WorkerTaskDispatch>.FromError(failure.Error);
        }

        return await orchestrator.DispatchAsync(
            new WorkerTaskDispatchRequest(taskType, payload, idempotencyKey, approval),
            cancellationToken);
    }

    public async Task<ServiceResult<WorkerTaskStatusReport>> GetStatusAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var status = await queue.GetStatusAsync(taskId, cancellationToken);
        return status switch
        {
            ServiceResult<TaskQueueStatus>.Success success =>
                ServiceResult<WorkerTaskStatusReport>.FromValue(
                    new WorkerTaskStatusReport(
                        taskId,
                        success.Value,
                        InProgress: success.Value is TaskQueueStatus.Queued or TaskQueueStatus.Leased)),
            ServiceResult<TaskQueueStatus>.Failure failure =>
                ServiceResult<WorkerTaskStatusReport>.FromError(failure.Error),
            _ => ServiceResult<WorkerTaskStatusReport>.FromError(
                new ServiceError(ServiceErrorCode.Validation, "The task queue returned an unsupported result."))
        };
    }
}

public sealed record WorkerTaskStatusReport(Guid TaskId, TaskQueueStatus Status, bool InProgress);
