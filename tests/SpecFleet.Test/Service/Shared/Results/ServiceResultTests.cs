using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Test.Service.Shared.Results;

public sealed class ServiceResultTests
{
    [Fact]
    public void Success_result_retains_its_value()
    {
        var result = ServiceResult<string>.FromValue("generated artifact");

        var success = Assert.IsType<ServiceResult<string>.Success>(result);
        Assert.Equal("generated artifact", success.Value);
    }

    [Theory]
    [InlineData(ServiceErrorCode.Validation)]
    [InlineData(ServiceErrorCode.Unauthorized)]
    [InlineData(ServiceErrorCode.Conflict)]
    [InlineData(ServiceErrorCode.ProviderUnavailable)]
    [InlineData(ServiceErrorCode.QueueUnavailable)]
    public void Expected_failure_result_retains_its_error(ServiceErrorCode code)
    {
        var error = new ServiceError(code, "Expected failure", IsRetryable: true);

        var result = ServiceResult<string>.FromError(error);

        var failure = Assert.IsType<ServiceResult<string>.Failure>(result);
        Assert.Equal(error, failure.Error);
    }

    [Fact]
    public void Match_invokes_the_success_branch_for_a_value()
    {
        var result = ServiceResult<int>.FromValue(42);

        var outcome = result.Match(
            value => $"value:{value}",
            error => $"error:{error.Code}");

        Assert.Equal("value:42", outcome);
    }

    [Fact]
    public void Match_invokes_the_failure_branch_for_an_expected_error()
    {
        var result = ServiceResult<int>.FromError(
            new ServiceError(ServiceErrorCode.Conflict, "State changed."));

        var outcome = result.Match(
            value => $"value:{value}",
            error => $"error:{error.Code}");

        Assert.Equal("error:Conflict", outcome);
    }
}
