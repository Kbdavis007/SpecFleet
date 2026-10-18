namespace SpecFleet.Service.Features.TaskQueue;

public sealed record TaskQueueRetryResult(
    TaskQueueStatus Status,
    int Attempts);
