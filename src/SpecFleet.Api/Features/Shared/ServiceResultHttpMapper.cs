using Microsoft.AspNetCore.Http;
using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Api.Features.Shared;

public static class ServiceResultHttpMapper
{
    public static IResult ToHttpResult<TValue>(ServiceResult<TValue> result)
        where TValue : notnull =>
        result.Match(
            Microsoft.AspNetCore.Http.Results.Ok,
            error => Microsoft.AspNetCore.Http.Results.Problem(
                statusCode: GetStatusCode(error.Code),
                title: error.Code.ToString(),
                detail: error.Message,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = error.Code.ToString(),
                    ["retryable"] = error.IsRetryable
                }));

    private static int GetStatusCode(ServiceErrorCode errorCode) =>
        errorCode switch
        {
            ServiceErrorCode.Validation => StatusCodes.Status400BadRequest,
            ServiceErrorCode.Unauthorized => StatusCodes.Status403Forbidden,
            ServiceErrorCode.NotFound => StatusCodes.Status404NotFound,
            ServiceErrorCode.Conflict => StatusCodes.Status409Conflict,
            ServiceErrorCode.ProviderUnavailable => StatusCodes.Status503ServiceUnavailable,
            ServiceErrorCode.QueueUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };
}
