namespace SpecFleet.Service.Features.TaskQueue;

public sealed record EnqueuedTask(
    Guid Id,
    string TaskType,
    string Payload,
    string IdempotencyKey,
    TaskQueueStatus Status,
    int Attempts,
    int MaximumAttempts);
