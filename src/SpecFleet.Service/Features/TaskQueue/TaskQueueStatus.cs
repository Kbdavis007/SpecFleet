namespace SpecFleet.Service.Features.TaskQueue;

public enum TaskQueueStatus
{
    Queued,
    Leased,
    Succeeded,
    Failed
}
