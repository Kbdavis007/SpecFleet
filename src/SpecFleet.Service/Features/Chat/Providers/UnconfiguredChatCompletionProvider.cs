using System.Runtime.CompilerServices;
using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Service.Features.Chat.Providers;

/// <summary>
/// Provider used when no usable credential is configured. It never performs a request and
/// reports that AI generation is unavailable, so callers cannot accidentally issue an
/// unauthenticated provider call.
/// </summary>
public sealed class UnconfiguredChatCompletionProvider : IChatCompletionProvider
{
    public bool IsConfigured => false;

    public string Model => "unconfigured";

    public IAsyncEnumerable<ChatCompletionChunk> StreamAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken)
    {
        _ = request;
        return ThrowUnavailable(cancellationToken);
    }

    private static async IAsyncEnumerable<ChatCompletionChunk> ThrowUnavailable(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        await Task.CompletedTask;
        throw new ProviderUnavailableException(
            "AI generation is unavailable because no provider credential is configured.");
        // Unreachable: satisfies the compiler's iterator contract.
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}

public sealed class ProviderUnavailableException(string message) : Exception(message);

public static class ChatCompletionProviderExtensions
{
    public static async Task<ServiceResult<ChatCompletionResult>> CollectAsync(
        this IChatCompletionProvider provider,
        ChatCompletionRequest request,
        CancellationToken cancellationToken)
    {
        var builder = new System.Text.StringBuilder();
        try
        {
            await foreach (var chunk in provider.StreamAsync(request, cancellationToken)
                .WithCancellation(cancellationToken))
            {
                builder.Append(chunk.Text);
            }

            return ServiceResult<ChatCompletionResult>.FromValue(
                new ChatCompletionResult(builder.ToString(), provider.Model, WasCancelled: false, Error: null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ServiceResult<ChatCompletionResult>.FromValue(
                new ChatCompletionResult(builder.ToString(), provider.Model, WasCancelled: true, Error: null));
        }
        catch (ProviderUnavailableException exception)
        {
            return ServiceResult<ChatCompletionResult>.FromError(
                new ServiceError(ServiceErrorCode.ProviderUnavailable, exception.Message));
        }
    }
}
