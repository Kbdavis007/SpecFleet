namespace SpecFleet.Service.Shared.Events;

public abstract record OutboxLeaseAttempt
{
    private OutboxLeaseAttempt()
    {
    }

    public sealed record Empty : OutboxLeaseAttempt;

    public sealed record Leased(OutboxLease Lease) : OutboxLeaseAttempt;
}
