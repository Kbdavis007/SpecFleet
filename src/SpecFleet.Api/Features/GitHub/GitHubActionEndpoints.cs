using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SpecFleet.Api.Features.Shared;
using SpecFleet.Service.Features.GitHubActions;
using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Api.Features.GitHub;

/// <summary>
/// Minimal API transport for GitHub repository actions. Route handlers bind input, delegate
/// to the Service handler, and map typed results to HTTP. No business logic lives here.
/// </summary>
public static class GitHubActionEndpoints
{
    public static IEndpointRouteBuilder MapGitHubActionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/github/actions").RequireAuthorization();

        group.MapPost(
            "/execute",
            async (
                HttpContext context,
                GitHubActionRequestBody request,
                GitHubActionHandler handler,
                CancellationToken cancellationToken) =>
            {
                var actor = ResolveActor(context);
                if (string.IsNullOrWhiteSpace(actor))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status403Forbidden,
                        title: nameof(ServiceErrorCode.Unauthorized),
                        detail: "An authenticated user is required.");
                }

                if (!Enum.TryParse<GitHubActionKind>(request.Kind, ignoreCase: true, out var kind) ||
                    !Enum.TryParse<GitHubConfirmationDecision>(
                        request.Decision ?? nameof(GitHubConfirmationDecision.Cancelled),
                        ignoreCase: true,
                        out var decision))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: nameof(ServiceErrorCode.Validation),
                        detail: "A supported GitHub action and confirmation decision are required.");
                }

                var action = new GitHubActionRequest(
                    actor,
                    new GitHubRepositoryRef(request.RepositoryOwner, request.RepositoryName),
                    kind,
                    decision,
                    request.PreviewDigest ?? string.Empty,
                    request.Title,
                    request.Body,
                    request.ReviewBranch,
                    request.BaseBranch,
                    request.WorkflowFileName,
                    request.Reference,
                    request.Changes?
                        .Select(change => new GitHubFileChange(change.Path, change.Content, change.IsNew))
                        .ToArray(),
                    request.WorkflowInputs);

                var result = await handler.ExecuteAsync(action, cancellationToken);
                return ServiceResultHttpMapper.ToHttpResult(result);
            });

        return endpoints;
    }

    private static string ResolveActor(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? context.User.Identity?.Name
        ?? string.Empty;
}

public sealed record GitHubActionRequestBody(
    string RepositoryOwner,
    string RepositoryName,
    string Kind,
    string? Decision,
    string? PreviewDigest,
    string? Title,
    string? Body,
    string? ReviewBranch,
    string? BaseBranch,
    string? WorkflowFileName,
    string? Reference,
    IReadOnlyList<GitHubFileChangeBody>? Changes,
    IReadOnlyDictionary<string, string>? WorkflowInputs);

public sealed record GitHubFileChangeBody(string Path, string Content, bool IsNew);
