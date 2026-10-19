namespace SpecFleet.Service.Features.Chat.Providers;

/// <summary>
/// Streaming AI provider. Implementations must not expose credentials to callers.
/// </summary>
public interface IChatCompletionProvider
{
    bool IsConfigured { get; }

    string Model { get; }

    IAsyncEnumerable<ChatCompletionChunk> StreamAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken);
}
