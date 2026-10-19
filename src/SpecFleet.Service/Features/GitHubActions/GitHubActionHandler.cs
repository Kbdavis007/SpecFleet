using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Service.Features.GitHubActions;

/// <summary>
/// Service-owned GitHub action use case. It enforces authorization and repository policy
/// before any state-changing call, requires explicit confirmation, and reports failures as
/// typed results instead of success-shaped outcomes.
/// </summary>
public sealed class GitHubActionHandler(IGitHubClient client, GitHubRepositoryPolicy policy)
{
    public async Task<ServiceResult<GitHubActionOutcome>> ExecuteAsync(
        GitHubActionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ActorId))
        {
            return ServiceResult<GitHubActionOutcome>.FromError(
                new ServiceError(ServiceErrorCode.Unauthorized, "An authenticated actor is required."));
        }

        if (request.Repository is null || string.IsNullOrWhiteSpace(request.Repository.Owner) ||
            string.IsNullOrWhiteSpace(request.Repository.Name))
        {
            return Validation("A repository owner and name are required.");
        }

        if (!Enum.IsDefined(request.Kind))
        {
            return Validation("A supported GitHub action is required.");
        }

        if (request.Decision != GitHubConfirmationDecision.Confirmed)
        {
            // Cancelled or unconfirmed actions must not reach the API at all.
            return ServiceResult<GitHubActionOutcome>.FromValue(
                new GitHubActionOutcome(false, request.Kind, null, [], null));
        }

        if (!PolicyAllows(request, out var policyError))
        {
            return ServiceResult<GitHubActionOutcome>.FromError(policyError!);
        }

        var permission = await client.GetPermissionAsync(request.Repository, request.Kind, cancellationToken);
        if (!permission.CanWrite)
        {
            return ServiceResult<GitHubActionOutcome>.FromError(
                new ServiceError(
                    ServiceErrorCode.Unauthorized,
                    "The actor is not authorized for the requested repository action."));
        }

        try
        {
            return request.Kind switch
            {
                GitHubActionKind.CreateIssue => await CreateIssueAsync(request, cancellationToken),
                GitHubActionKind.WriteFiles => await WriteFilesAsync(request, cancellationToken),
                GitHubActionKind.CreatePullRequest => await CreatePullRequestAsync(request, cancellationToken),
                GitHubActionKind.DispatchWorkflow => await DispatchWorkflowAsync(request, cancellationToken),
                _ => Validation("A supported GitHub action is required.")
            };
        }
        catch (GitHubApiException exception)
        {
            return ServiceResult<GitHubActionOutcome>.FromError(
                new ServiceError(ServiceErrorCode.ProviderUnavailable, exception.Message));
        }
    }

    private bool PolicyAllows(GitHubActionRequest request, out ServiceError? error)
    {
        error = null;

        if (request.Kind is GitHubActionKind.WriteFiles or GitHubActionKind.CreatePullRequest)
        {
            foreach (var change in request.Changes ?? [])
            {
                if (!policy.IsPathAllowed(change.Path))
                {
                    error = new ServiceError(
                        ServiceErrorCode.Validation,
                        $"The path '{change.Path}' is invalid or disallowed by repository policy.");
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(request.BaseBranch) ||
                string.IsNullOrWhiteSpace(request.ReviewBranch))
            {
                error = new ServiceError(
                    ServiceErrorCode.Validation,
                    "A review branch and base branch are required.");
                return false;
            }
        }

        if (request.Kind == GitHubActionKind.DispatchWorkflow &&
            !policy.IsWorkflowAllowed(request.WorkflowFileName ?? string.Empty))
        {
            error = new ServiceError(
                ServiceErrorCode.Validation,
                "The requested workflow is not allowlisted.");
            return false;
        }

        return true;
    }

    private async Task<ServiceResult<GitHubActionOutcome>> CreateIssueAsync(
        GitHubActionRequest request,
        CancellationToken cancellationToken)
    {
        var title = request.Title ?? string.Empty;
        var body = request.Body ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            return Validation("An issue title is required.");
        }

        if (!Matches(request.PreviewDigest, GitHubPreviewDigest.Compute(title, body)))
        {
            return Conflict("The issue content no longer matches the reviewed preview.");
        }

        var issue = await client.CreateIssueAsync(request.Repository, title, body, cancellationToken);
        return ServiceResult<GitHubActionOutcome>.FromValue(
            new GitHubActionOutcome(true, request.Kind, issue.Url, [], null));
    }

    private async Task<ServiceResult<GitHubActionOutcome>> WriteFilesAsync(
        GitHubActionRequest request,
        CancellationToken cancellationToken)
    {
        var changes = request.Changes ?? [];
        if (changes.Count == 0)
        {
            return Validation("At least one file change is required.");
        }

        if (!Matches(request.PreviewDigest, GitHubPreviewDigest.ComputeFiles(changes)))
        {
            return Conflict("The file changes no longer match the reviewed preview.");
        }

        var result = await client.WriteFilesAsync(
            request.Repository,
            request.ReviewBranch!,
            request.BaseBranch!,
            changes,
            cancellationToken);
        return ServiceResult<GitHubActionOutcome>.FromValue(
            new GitHubActionOutcome(true, request.Kind, null, result.ChangedPaths, null));
    }

    private async Task<ServiceResult<GitHubActionOutcome>> CreatePullRequestAsync(
        GitHubActionRequest request,
        CancellationToken cancellationToken)
    {
        var pullRequest = await client.CreatePullRequestAsync(
            request.Repository,
            request.ReviewBranch!,
            request.BaseBranch!,
            request.Title ?? string.Empty,
            request.Body ?? string.Empty,
            cancellationToken);
        return ServiceResult<GitHubActionOutcome>.FromValue(
            new GitHubActionOutcome(true, request.Kind, pullRequest.Url, [], null));
    }

    private async Task<ServiceResult<GitHubActionOutcome>> DispatchWorkflowAsync(
        GitHubActionRequest request,
        CancellationToken cancellationToken)
    {
        var workflow = request.WorkflowFileName!;
        var inputs = request.WorkflowInputs ?? new Dictionary<string, string>();
        if (!Matches(request.PreviewDigest, GitHubPreviewDigest.ComputeWorkflow(workflow, inputs)))
        {
            return Conflict("The workflow dispatch no longer matches the reviewed preview.");
        }

        await client.DispatchWorkflowAsync(
            request.Repository,
            workflow,
            request.Reference ?? request.BaseBranch ?? "main",
            inputs,
            cancellationToken);
        return ServiceResult<GitHubActionOutcome>.FromValue(
            new GitHubActionOutcome(true, request.Kind, null, [], null));
    }

    private static bool Matches(string? provided, string expected) =>
        !string.IsNullOrWhiteSpace(provided) &&
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(provided.ToUpperInvariant()),
            System.Text.Encoding.ASCII.GetBytes(expected));

    private static ServiceResult<GitHubActionOutcome> Validation(string message) =>
        ServiceResult<GitHubActionOutcome>.FromError(new ServiceError(ServiceErrorCode.Validation, message));

    private static ServiceResult<GitHubActionOutcome> Conflict(string message) =>
        ServiceResult<GitHubActionOutcome>.FromError(new ServiceError(ServiceErrorCode.Conflict, message));
}
