using SpecFleet.Service.Features.TaskQueue;

namespace SpecFleet.Service.Features.TaskOrchestration;

public sealed record WorkerTaskDispatch(
    Guid TaskId,
    WorkerTaskType TaskType,
    TaskQueueStatus Status,
    string ApprovalReference,
    string Target);
