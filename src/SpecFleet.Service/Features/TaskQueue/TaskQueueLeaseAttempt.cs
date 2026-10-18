namespace SpecFleet.Service.Features.TaskQueue;

public abstract record TaskQueueLeaseAttempt
{
    private TaskQueueLeaseAttempt()
    {
    }

    public sealed record Empty : TaskQueueLeaseAttempt;

    public sealed record Leased(TaskQueueLease Lease) : TaskQueueLeaseAttempt;
}
