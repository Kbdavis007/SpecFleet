using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SpecFleet.Api.Features.Shared;
using SpecFleet.Service.Features.Chat.Actions;
using SpecFleet.Service.Features.Chat.Artifacts;
using SpecFleet.Service.Features.Chat.Ask;
using SpecFleet.Service.Features.Chat.Conversations;
using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Api.Features.Chat;

/// <summary>
/// Minimal API transport for chat. Route handlers bind input, delegate to Service use cases,
/// and map typed results to HTTP. No business logic lives here.
/// </summary>
public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/chat").RequireAuthorization();

        group.MapPost(
            "/conversations",
            async (
                HttpContext context,
                StartConversationRequest request,
                PostgreSqlConversationStore conversations,
                CancellationToken cancellationToken) =>
            {
                var owner = ResolveOwner(context);
                var result = await conversations.StartConversationAsync(
                    owner,
                    request.Title ?? "New conversation",
                    cancellationToken);
                return result.Match(
                    conversation => Results.Created(
                        $"/api/chat/conversations/{conversation.ConversationId}",
                        ToResponse(conversation)),
                    error => ServiceResultHttpMapper.ToHttpResult(
                        ServiceResult<Conversation>.FromError(error)));
            });

        group.MapGet(
            "/conversations/{conversationId:guid}/messages",
            async (
                HttpContext context,
                Guid conversationId,
                PostgreSqlConversationStore conversations,
                CancellationToken cancellationToken) =>
            {
                var owner = ResolveOwner(context);
                var result = await conversations.ListMessagesAsync(conversationId, owner, cancellationToken);
                return result.Match(
                    messages => Results.Ok(messages.Select(ToResponse)),
                    error => ServiceResultHttpMapper.ToHttpResult(
                        ServiceResult<IReadOnlyList<ChatMessage>>.FromError(error)));
            });

        group.MapPost(
            "/ask",
            async (
                HttpContext context,
                ChatAskRequest request,
                ChatAskHandler handler,
                CancellationToken cancellationToken) =>
            {
                var owner = ResolveOwner(context);
                var effective = request with { OwnerId = owner };
                return Results.ServerSentEvents(StreamAsync(handler, effective, cancellationToken));
            });

        group.MapPost(
            "/artifacts/preview",
            (
                HttpContext context,
                ArtifactPreviewRequest request,
                ChatAskHandler handler) =>
            {
                var owner = ResolveOwner(context);
                if (string.IsNullOrWhiteSpace(owner))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status403Forbidden,
                        title: nameof(ServiceErrorCode.Unauthorized),
                        detail: "An authenticated user is required.");
                }

                if (!Enum.TryParse<ArtifactType>(request.ArtifactType, ignoreCase: true, out var artifactType))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: nameof(ServiceErrorCode.Validation),
                        detail: "A supported artifact type is required.");
                }

                var preview = handler.BuildPreview(artifactType, request.Content ?? string.Empty);
                return Results.Ok(ToResponse(preview));
            });

        group.MapPost(
            "/artifacts/apply",
            async (
                HttpContext context,
                ArtifactActionRequestBody request,
                ArtifactActionHandler handler,
                CancellationToken cancellationToken) =>
            {
                var owner = ResolveOwner(context);
                if (string.IsNullOrWhiteSpace(owner))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status403Forbidden,
                        title: nameof(ServiceErrorCode.Unauthorized),
                        detail: "An authenticated user is required.");
                }

                if (!Enum.TryParse<ArtifactType>(request.ArtifactType, ignoreCase: true, out var artifactType) ||
                    !Enum.TryParse<ArtifactActionDecision>(
                        request.Decision,
                        ignoreCase: true,
                        out var decision))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: nameof(ServiceErrorCode.Validation),
                        detail: "A supported artifact type and decision are required.");
                }

                var result = await handler.ApplyAsync(
                    new ArtifactActionRequest(
                        request.ArtifactId ?? string.Empty,
                        owner,
                        artifactType,
                        request.Target ?? string.Empty,
                        request.Content ?? string.Empty,
                        request.PreviewDigest ?? string.Empty,
                        decision),
                    cancellationToken);

                return ServiceResultHttpMapper.ToHttpResult(result);
            });

        return endpoints;
    }

    private static async IAsyncEnumerable<string> StreamAsync(
        ChatAskHandler handler,
        ChatAskRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in handler.StreamAsync(request, cancellationToken))
        {
            if (item is ServiceResult<ChatAskStreamEvent>.Success success)
            {
                yield return Serialize(success.Value);
            }
            else if (item is ServiceResult<ChatAskStreamEvent>.Failure failure)
            {
                yield return Serialize(new ChatAskStreamEvent(
                    ChatStreamEventKind.Failed,
                    null,
                    Guid.Empty,
                    null,
                    failure.Error.Message));
            }
        }
    }

    private static readonly System.Text.Json.JsonSerializerOptions StreamJsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

    private static string Serialize(ChatAskStreamEvent streamEvent) =>
        System.Text.Json.JsonSerializer.Serialize(streamEvent, StreamJsonOptions);

    private static string ResolveOwner(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? context.User.Identity?.Name
        ?? string.Empty;

    private static object ToResponse(Conversation conversation) => new
    {
        conversation.ConversationId,
        conversation.Title,
        conversation.CreatedAt
    };

    private static object ToResponse(ChatMessage message) => new
    {
        message.MessageId,
        message.ConversationId,
        Role = message.Role.ToString(),
        message.Content,
        Status = message.Status.ToString(),
        message.CreatedAt,
        message.UpdatedAt,
        message.Error
    };

    private static object ToResponse(ArtifactPreview preview) => new
    {
        preview.ArtifactId,
        ArtifactType = preview.ArtifactType.ToString(),
        preview.ProposedPath,
        preview.Content,
        preview.Language,
        preview.PreviewDigest,
        preview.IsValid,
        ValidationFailures = preview.ValidationFailures.Select(failure => new
        {
            failure.Rule,
            failure.Message
        })
    };
}

public sealed record StartConversationRequest(string? Title);

public sealed record ArtifactPreviewRequest(string ArtifactType, string? Content);

public sealed record ArtifactActionRequestBody(
    string? ArtifactId,
    string ArtifactType,
    string? Target,
    string? Content,
    string? PreviewDigest,
    string Decision);
