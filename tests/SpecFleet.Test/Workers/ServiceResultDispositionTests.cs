using SpecFleet.Service.Shared.Results;
using SpecFleet.Workers.Consumers;

namespace SpecFleet.Test.Workers;

public sealed class ServiceResultDispositionTests
{
    [Fact]
    public void Successful_result_is_acknowledged()
    {
        var disposition = ServiceResultDisposition.For(
            ServiceResult<string>.FromValue("completed"));

        Assert.Equal(WorkerDisposition.Acknowledge, disposition);
    }

    [Fact]
    public void Retryable_failure_is_retried()
    {
        var disposition = ServiceResultDisposition.For(
            ServiceResult<string>.FromError(
                new ServiceError(ServiceErrorCode.ProviderUnavailable, "Try later.", IsRetryable: true)));

        Assert.Equal(WorkerDisposition.Retry, disposition);
    }

    [Fact]
    public void Non_retryable_failure_is_rejected()
    {
        var disposition = ServiceResultDisposition.For(
            ServiceResult<string>.FromError(
                new ServiceError(ServiceErrorCode.Validation, "Invalid payload.")));

        Assert.Equal(WorkerDisposition.Reject, disposition);
    }
}
