using System.Text;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SpecFleet.Api.Features.Shared;
using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Test.Api;

public sealed class ServiceResultHttpMapperTests
{
    [Theory]
    [InlineData(ServiceErrorCode.Validation, 400)]
    [InlineData(ServiceErrorCode.Unauthorized, 403)]
    [InlineData(ServiceErrorCode.NotFound, 404)]
    [InlineData(ServiceErrorCode.Conflict, 409)]
    [InlineData(ServiceErrorCode.ProviderUnavailable, 503)]
    [InlineData(ServiceErrorCode.QueueUnavailable, 503)]
    public async Task Expected_service_errors_map_to_explicit_http_statuses(
        ServiceErrorCode errorCode,
        int expectedStatusCode)
    {
        using var services = CreateServices();
        var httpContext = CreateHttpContext(services);
        httpContext.Response.Body = new MemoryStream();
        var result = ServiceResultHttpMapper.ToHttpResult(
            ServiceResult<string>.FromError(new ServiceError(errorCode, "Expected failure.")));

        await result.ExecuteAsync(httpContext);

        Assert.Equal(expectedStatusCode, httpContext.Response.StatusCode);
        var responseBody = Encoding.UTF8.GetString(((MemoryStream)httpContext.Response.Body).ToArray());
        Assert.Contains(errorCode.ToString(), responseBody);
        Assert.Contains("Expected failure.", responseBody);
    }

    [Fact]
    public async Task Successful_service_result_maps_to_ok_with_value()
    {
        using var services = CreateServices();
        var httpContext = CreateHttpContext(services);
        httpContext.Response.Body = new MemoryStream();
        var result = ServiceResultHttpMapper.ToHttpResult(
            ServiceResult<string>.FromValue("result"));

        await result.ExecuteAsync(httpContext);

        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        Assert.Contains("result", Encoding.UTF8.GetString(((MemoryStream)httpContext.Response.Body).ToArray()));
    }

    private static ServiceProvider CreateServices() =>
        new ServiceCollection()
            .AddLogging()
            .AddRouting()
            .AddProblemDetails()
            .BuildServiceProvider();

    private static DefaultHttpContext CreateHttpContext(IServiceProvider services) =>
        new()
        {
            RequestServices = services
        };
}
