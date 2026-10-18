namespace SpecFleet.Service.Shared.Events;

public interface IDomainEvent
{
    Guid Id { get; }

    DateTimeOffset OccurredAt { get; }
}
