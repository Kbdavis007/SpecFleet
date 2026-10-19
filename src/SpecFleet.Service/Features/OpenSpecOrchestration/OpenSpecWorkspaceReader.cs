using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Service.Features.OpenSpecOrchestration;

/// <summary>
/// Reads OpenSpec content from a project workspace. Inspection is read-only and confined to
/// the workspace root; the reader reports when no relevant content exists rather than
/// fabricating a result.
/// </summary>
public sealed class OpenSpecWorkspaceReader(OpenSpecWorkspaceGuard guard)
{
    public ServiceResult<OpenSpecWorkspaceSnapshot> Inspect(string workspacePath)
    {
        if (!guard.IsWithinWorkspace(workspacePath, out var workspace))
        {
            return ServiceResult<OpenSpecWorkspaceSnapshot>.FromError(
                new ServiceError(ServiceErrorCode.Validation, "The workspace is outside the authorized root."));
        }

        var specRoot = Path.Combine(workspace, "openspec");
        if (!Directory.Exists(specRoot))
        {
            return ServiceResult<OpenSpecWorkspaceSnapshot>.FromValue(
                new OpenSpecWorkspaceSnapshot(workspace, [], [], HasContent: false));
        }

        var capabilities = SafeEnumerate(Path.Combine(specRoot, "specs"));
        var changes = SafeEnumerate(Path.Combine(specRoot, "changes"));
        return ServiceResult<OpenSpecWorkspaceSnapshot>.FromValue(
            new OpenSpecWorkspaceSnapshot(
                workspace,
                capabilities,
                changes,
                HasContent: capabilities.Count > 0 || changes.Count > 0));
    }

    /// <summary>
    /// Prepares proposal content under the standard change structure for review. Nothing is
    /// written here; writing is a separate, confirmed state-changing operation.
    /// </summary>
    public ServiceResult<OpenSpecProposalPreview> PrepareProposal(string workspacePath, string changeId)
    {
        if (!guard.IsWithinWorkspace(workspacePath, out var workspace))
        {
            return ServiceResult<OpenSpecProposalPreview>.FromError(
                new ServiceError(ServiceErrorCode.Validation, "The workspace is outside the authorized root."));
        }

        if (string.IsNullOrWhiteSpace(changeId) || changeId.Any(character => !IsChangeIdCharacter(character)))
        {
            return ServiceResult<OpenSpecProposalPreview>.FromError(
                new ServiceError(ServiceErrorCode.Validation, "A valid OpenSpec change identifier is required."));
        }

        var proposalPath = Path.Combine("openspec", "changes", changeId, "proposal.md");
        var tasksPath = Path.Combine("openspec", "changes", changeId, "tasks.md");
        var content = $"""
            # {changeId}

            ## Why

            Describe the motivation for this change.

            ## What Changes

            Describe the behavior being added or modified.

            ## Tasks

            See `{tasksPath.Replace('\\', '/')}`.
            """;

        return ServiceResult<OpenSpecProposalPreview>.FromValue(
            new OpenSpecProposalPreview(changeId, proposalPath.Replace('\\', '/'), content, Written: false));
    }

    private static IReadOnlyList<string> SafeEnumerate(string path)
    {
        if (!Directory.Exists(path))
        {
            return [];
        }

        return Directory
            .EnumerateDirectories(path)
            .Select(directory => Path.GetFileName(directory))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsChangeIdCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_';
}

public sealed record OpenSpecWorkspaceSnapshot(
    string WorkspacePath,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Changes,
    bool HasContent);

public sealed record OpenSpecProposalPreview(
    string ChangeId,
    string ProposedPath,
    string Content,
    bool Written);
