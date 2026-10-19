namespace SpecFleet.Service.Features.GitHubActions;

/// <summary>
/// A reviewed, confirmable GitHub action. <see cref="PreviewDigest"/> binds the approval to
/// the exact reviewed content so a changed preview cannot be submitted.
/// </summary>
public sealed record GitHubActionRequest(
    string ActorId,
    GitHubRepositoryRef Repository,
    GitHubActionKind Kind,
    GitHubConfirmationDecision Decision,
    string PreviewDigest,
    string? Title = null,
    string? Body = null,
    string? ReviewBranch = null,
    string? BaseBranch = null,
    string? WorkflowFileName = null,
    string? Reference = null,
    IReadOnlyList<GitHubFileChange>? Changes = null,
    IReadOnlyDictionary<string, string>? WorkflowInputs = null);

/// <summary>
/// The reported outcome of a confirmed GitHub action. <see cref="Succeeded"/> is only true
/// when the action actually completed; failures carry the actionable error.
/// </summary>
public sealed record GitHubActionOutcome(
    bool Succeeded,
    GitHubActionKind Kind,
    string? Url,
    IReadOnlyList<string> ChangedPaths,
    string? Error);

public static class GitHubPreviewDigest
{
    public static string Compute(string title, string body) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{title}\u001f{body}")));

    public static string ComputeFiles(IReadOnlyList<GitHubFileChange> changes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join(
                "\u001e",
                changes
                    .OrderBy(change => change.Path, StringComparer.Ordinal)
                    .Select(change => $"{change.Path}\u001f{change.Content}\u001f{change.IsNew}")))));

    public static string ComputeWorkflow(string workflowFileName, IReadOnlyDictionary<string, string> inputs) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{workflowFileName}\u001f" + string.Join(
                "\u001e",
                inputs.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}\u001f{pair.Value}")))));
}
