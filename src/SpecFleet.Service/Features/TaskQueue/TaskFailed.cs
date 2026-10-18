using SpecFleet.Service.Shared.Events;

namespace SpecFleet.Service.Features.TaskQueue;

public sealed record TaskFailed(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid TaskId,
    string TaskType,
    int Attempts,
    string Reason) : IDomainEvent;
