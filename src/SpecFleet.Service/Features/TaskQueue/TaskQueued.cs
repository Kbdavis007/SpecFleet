using SpecFleet.Service.Shared.Events;

namespace SpecFleet.Service.Features.TaskQueue;

public sealed record TaskQueued(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid TaskId,
    string TaskType,
    string IdempotencyKey) : IDomainEvent;
