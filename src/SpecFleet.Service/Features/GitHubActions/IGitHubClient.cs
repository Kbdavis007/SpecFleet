namespace SpecFleet.Service.Features.GitHubActions;

/// <summary>
/// Port for the GitHub API. The Service owns authorization, confirmation, and policy
/// decisions; this port only performs the transport calls those decisions permit.
/// Implementations must surface failures as typed results rather than throwing.
/// </summary>
public interface IGitHubClient
{
    /// <summary>Returns the effective write permission for the requested repository.</summary>
    Task<GitHubPermission> GetPermissionAsync(
        GitHubRepositoryRef repository,
        GitHubActionKind action,
        CancellationToken cancellationToken = default);

    Task<GitHubIssue> CreateIssueAsync(
        GitHubRepositoryRef repository,
        string title,
        string body,
        CancellationToken cancellationToken = default);

    /// <summary>Writes the confirmed files onto a review branch and returns the changed paths.</summary>
    Task<GitHubFileWriteResult> WriteFilesAsync(
        GitHubRepositoryRef repository,
        string reviewBranch,
        string baseBranch,
        IReadOnlyList<GitHubFileChange> changes,
        CancellationToken cancellationToken = default);

    Task<GitHubPullRequest> CreatePullRequestAsync(
        GitHubRepositoryRef repository,
        string headBranch,
        string baseBranch,
        string title,
        string body,
        CancellationToken cancellationToken = default);

    Task DispatchWorkflowAsync(
        GitHubRepositoryRef repository,
        string workflowFileName,
        string reference,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Raised by an <see cref="IGitHubClient"/> when GitHub rejects a request or the credential
/// is missing, expired, or insufficient. The handler converts this into an explicit failure.
/// </summary>
public sealed class GitHubApiException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
