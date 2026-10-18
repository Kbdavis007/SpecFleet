namespace SpecFleet.Service.Features.TaskOrchestration;

public sealed record WorkerTaskApproval(
    string ActorId,
    string ApprovalReference,
    WorkerTaskType TaskType,
    string PayloadDigest,
    string Target);
