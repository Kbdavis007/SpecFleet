using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Service.Features.OpenSpecOrchestration;

/// <summary>
/// The supported OpenSpec CLI operations. Read-only operations run immediately; state-changing
/// operations require explicit confirmation before they execute.
/// </summary>
public enum OpenSpecOperationKind
{
    List,
    Show,
    Validate,
    New,
    Apply,
    Archive
}

public static class OpenSpecOperations
{
    public static bool IsReadOnly(OpenSpecOperationKind kind) =>
        kind is OpenSpecOperationKind.List or OpenSpecOperationKind.Show or OpenSpecOperationKind.Validate;

    public static bool IsStateChanging(OpenSpecOperationKind kind) =>
        kind is OpenSpecOperationKind.New or OpenSpecOperationKind.Apply or OpenSpecOperationKind.Archive;

    /// <summary>Maps an operation to its fixed, non-arbitrary CLI argument vector.</summary>
    public static IReadOnlyList<string> BuildArguments(OpenSpecOperationKind kind, string? target) =>
        kind switch
        {
            OpenSpecOperationKind.List => ["list"],
            OpenSpecOperationKind.Show => ["show", Sanitize(target)],
            OpenSpecOperationKind.Validate => ["validate", Sanitize(target)],
            OpenSpecOperationKind.New => ["new", "change", Sanitize(target)],
            OpenSpecOperationKind.Apply => ["apply", Sanitize(target)],
            OpenSpecOperationKind.Archive => ["archive", Sanitize(target)],
            _ => []
        };

    private static string Sanitize(string? target) =>
        string.IsNullOrWhiteSpace(target) ? string.Empty : target.Trim();
}

public sealed record OpenSpecCommandResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// Runs an OpenSpec CLI operation. Implementations must execute the supplied argument vector
/// verbatim, without a shell, and confined to the requested working directory.
/// </summary>
public interface IOpenSpecCommandRunner
{
    Task<OpenSpecCommandResult> RunAsync(
        string workspacePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);
}

public sealed record OpenSpecOperationRequest(
    string ActorId,
    string WorkspacePath,
    OpenSpecOperationKind Operation,
    string? Target,
    bool Confirmed);

public sealed record OpenSpecOperationOutcome(
    bool Succeeded,
    OpenSpecOperationKind Operation,
    bool StateChanging,
    bool PreviewRequired,
    string Output,
    string? Error);
