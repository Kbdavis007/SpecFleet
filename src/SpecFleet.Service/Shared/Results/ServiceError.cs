namespace SpecFleet.Service.Shared.Results;

public sealed record ServiceError(
    ServiceErrorCode Code,
    string Message,
    bool IsRetryable = false);
