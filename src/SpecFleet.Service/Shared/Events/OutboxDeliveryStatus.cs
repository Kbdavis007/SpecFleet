namespace SpecFleet.Service.Shared.Events;

public enum OutboxDeliveryStatus
{
    Pending,
    Delivered,
    Failed
}
