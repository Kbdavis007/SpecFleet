namespace SpecFleet.Service.Features.TaskOrchestration;

public sealed record WorkerTaskDispatchRequest(
    WorkerTaskType TaskType,
    string Payload,
    string IdempotencyKey,
    WorkerTaskApproval Approval);
