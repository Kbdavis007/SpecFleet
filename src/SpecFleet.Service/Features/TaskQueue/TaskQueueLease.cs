namespace SpecFleet.Service.Features.TaskQueue;

public sealed record TaskQueueLease(
    Guid Id,
    string TaskType,
    string Payload,
    int Attempts,
    int MaximumAttempts,
    string WorkerId,
    DateTimeOffset LeaseUntil);
