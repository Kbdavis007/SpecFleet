namespace SpecFleet.Service.Shared.Events;

public sealed record OutboxLease(
    Guid EventId,
    string EventType,
    string Payload,
    int Attempts,
    string WorkerId,
    DateTimeOffset LeaseUntil);
