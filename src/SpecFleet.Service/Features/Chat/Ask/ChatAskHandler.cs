using System.Security.Cryptography;
using System.Text;
using SpecFleet.Service.Features.Chat.Artifacts;
using SpecFleet.Service.Features.Chat.Conversations;
using SpecFleet.Service.Features.Chat.Providers;
using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Service.Features.Chat.Ask;

public sealed record ChatAskRequest(
    Guid ConversationId,
    string OwnerId,
    string Prompt,
    ArtifactType? ArtifactType);

public sealed record ChatAskStreamEvent(
    ChatStreamEventKind Kind,
    string? Text,
    Guid MessageId,
    ArtifactPreview? Artifact,
    string? Error);

public enum ChatStreamEventKind
{
    Delta,
    Completed,
    Failed,
    Cancelled,
    Artifact
}

/// <summary>
/// Authenticated chat use case. Streams assistant output and, when an artifact is requested,
/// returns a validated, reviewable preview. Expected failures are returned as typed results;
/// no provider credential is ever placed in the returned events.
/// </summary>
public sealed class ChatAskHandler(
    PostgreSqlConversationStore conversations,
    IChatCompletionProvider provider,
    ArtifactValidator validator)
{
    public async IAsyncEnumerable<ServiceResult<ChatAskStreamEvent>> StreamAsync(
        ChatAskRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Prompt))
        {
            yield return Fail("A non-empty prompt is required.", ServiceErrorCode.Validation);
            yield break;
        }

        var userMessage = await conversations.AppendUserMessageAsync(
            request.ConversationId,
            request.OwnerId,
            request.Prompt,
            cancellationToken);
        if (userMessage is ServiceResult<ChatMessage>.Failure userFailure)
        {
            yield return ServiceResult<ChatAskStreamEvent>.FromError(userFailure.Error);
            yield break;
        }

        var assistantMessage = await conversations.BeginAssistantMessageAsync(
            request.ConversationId,
            request.OwnerId,
            cancellationToken);
        if (assistantMessage is ServiceResult<ChatMessage>.Failure beginFailure)
        {
            yield return ServiceResult<ChatAskStreamEvent>.FromError(beginFailure.Error);
            yield break;
        }

        var messageId = ((ServiceResult<ChatMessage>.Success)assistantMessage).Value.MessageId;
        var builder = new StringBuilder();
        var cancelled = false;

        var stream = provider.StreamAsync(
            new ChatCompletionRequest(request.Prompt, BuildSystemInstruction(request.ArtifactType)),
            cancellationToken);

        await using var enumerator = stream.GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            ChatCompletionChunk chunk;
            string? providerError = null;
            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    break;
                }

                chunk = enumerator.Current;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }
            catch (ProviderUnavailableException exception)
            {
                providerError = exception.Message;
                chunk = default!;
            }

            if (providerError is not null)
            {
                await conversations.FailAssistantMessageAsync(
                    messageId,
                    builder.ToString(),
                    providerError,
                    CancellationToken.None);
                yield return Fail(providerError, ServiceErrorCode.ProviderUnavailable);
                yield break;
            }

            builder.Append(chunk.Text);
            yield return ServiceResult<ChatAskStreamEvent>.FromValue(
                new ChatAskStreamEvent(ChatStreamEventKind.Delta, chunk.Text, messageId, null, null));
        }

        if (cancelled)
        {
            await conversations.CancelAssistantMessageAsync(
                messageId,
                builder.ToString(),
                CancellationToken.None);
            yield return ServiceResult<ChatAskStreamEvent>.FromValue(
                new ChatAskStreamEvent(ChatStreamEventKind.Cancelled, null, messageId, null, null));
            yield break;
        }

        await conversations.CompleteAssistantMessageAsync(
            messageId,
            builder.ToString(),
            CancellationToken.None);

        if (request.ArtifactType is { } artifactType)
        {
            var preview = BuildPreview(artifactType, builder.ToString());
            yield return ServiceResult<ChatAskStreamEvent>.FromValue(
                new ChatAskStreamEvent(ChatStreamEventKind.Artifact, null, messageId, preview, null));
        }

        yield return ServiceResult<ChatAskStreamEvent>.FromValue(
            new ChatAskStreamEvent(ChatStreamEventKind.Completed, null, messageId, null, null));
    }

    public ArtifactPreview BuildPreview(ArtifactType artifactType, string content)
    {
        ArtifactCatalog.TryGetTemplate(artifactType, out var template);
        var failures = validator.Validate(artifactType, content);
        var digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(content ?? string.Empty)));

        return new ArtifactPreview(
            Guid.NewGuid().ToString("N"),
            artifactType,
            template?.ProposedPath ?? string.Empty,
            content ?? string.Empty,
            template?.Language,
            digest,
            failures.Count == 0,
            failures);
    }

    private static string BuildSystemInstruction(ArtifactType? artifactType) =>
        artifactType is null
            ? "You are the SpecFleet project assistant. Answer concisely and accurately."
            : $"You generate {artifactType} artifacts. Return only the artifact content.";

    private static ServiceResult<ChatAskStreamEvent> Fail(string message, ServiceErrorCode code) =>
        ServiceResult<ChatAskStreamEvent>.FromError(new ServiceError(code, message));
}
