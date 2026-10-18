namespace SpecFleet.Service.Shared.Results;

public abstract record ServiceResult<TValue>
    where TValue : notnull
{
    private ServiceResult()
    {
    }

    public sealed record Success(TValue Value) : ServiceResult<TValue>;

    public sealed record Failure(ServiceError Error) : ServiceResult<TValue>;

    public static ServiceResult<TValue> FromValue(TValue value) => new Success(value);

    public static ServiceResult<TValue> FromError(ServiceError error) => new Failure(error);

    public TResult Match<TResult>(
        Func<TValue, TResult> onSuccess,
        Func<ServiceError, TResult> onFailure)
    {
        if (this is Success success)
        {
            return onSuccess(success.Value);
        }

        return onFailure(((Failure)this).Error);
    }
}
