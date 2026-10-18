namespace SpecFleet.Service.Shared.Events;

public sealed record OutboxRetryResult(
    OutboxDeliveryStatus Status,
    int Attempts);
