using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Workers.Consumers;

public static class ServiceResultDisposition
{
    public static WorkerDisposition For<TValue>(ServiceResult<TValue> result)
        where TValue : notnull =>
        result switch
        {
            ServiceResult<TValue>.Success => WorkerDisposition.Acknowledge,
            ServiceResult<TValue>.Failure { Error.IsRetryable: true } => WorkerDisposition.Retry,
            ServiceResult<TValue>.Failure => WorkerDisposition.Reject,
            _ => WorkerDisposition.Reject
        };
}
