namespace SpecFleet.Service.Features.GitHubActions;

/// <summary>
/// Identifies a GitHub repository targeted by a confirmed action.
/// </summary>
public sealed record GitHubRepositoryRef(string Owner, string Name)
{
    public string FullName => $"{Owner}/{Name}";
}

public enum GitHubActionKind
{
    CreateIssue,
    WriteFiles,
    CreatePullRequest,
    DispatchWorkflow
}

public enum GitHubConfirmationDecision
{
    Confirmed,
    Cancelled
}

public sealed record GitHubPermission(bool CanWrite);

public sealed record GitHubIssue(long Number, string Url);

public sealed record GitHubPullRequest(long Number, string Url);

public sealed record GitHubFileChange(string Path, string Content, bool IsNew);

public sealed record GitHubFileWriteResult(string ReviewBranch, IReadOnlyList<string> ChangedPaths);
