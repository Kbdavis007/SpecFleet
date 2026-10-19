using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Service.Features.OpenSpecOrchestration;

/// <summary>
/// Enforces workspace containment and command bounds for OpenSpec CLI operations. Only
/// allowlisted operations are permitted, only fixed argument vectors are built, and every
/// operation must resolve inside the authorized workspace root.
/// </summary>
public sealed class OpenSpecWorkspaceGuard(string workspaceRoot)
{
    private readonly string _workspaceRoot = Path.GetFullPath(workspaceRoot);

    public bool IsWithinWorkspace(string workspacePath, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return false;
        }

        string candidate;
        try
        {
            candidate = Path.GetFullPath(workspacePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (!IsUnder(candidate, _workspaceRoot))
        {
            return false;
        }

        resolvedPath = candidate;
        return true;
    }

    /// <summary>
    /// Rejects requests carrying unsupported commands, arbitrary arguments, or executables
    /// outside the allowlist. The Service never forwards a raw command string to a shell.
    /// </summary>
    public static bool IsSupported(OpenSpecOperationKind operation) => Enum.IsDefined(operation);

    /// <summary>
    /// A target is limited to a plain OpenSpec identifier. Shell metacharacters, path
    /// separators, and whitespace are rejected so no argument can smuggle a second command.
    /// </summary>
    public static bool IsSafeTarget(string? target) =>
        string.IsNullOrWhiteSpace(target) ||
        target.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static bool IsUnder(string candidate, string root)
    {
        if (string.Equals(candidate, root, StringComparison.Ordinal))
        {
            return true;
        }

        var normalizedRoot = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(normalizedRoot, StringComparison.Ordinal);
    }
}

/// <summary>
/// Service-owned OpenSpec operation use case. It previews state-changing operations and runs
/// them only after explicit confirmation, and it never reports an unexecuted operation as
/// successful.
/// </summary>
public sealed class OpenSpecOperationHandler(IOpenSpecCommandRunner runner, OpenSpecWorkspaceGuard guard)
{
    public async Task<ServiceResult<OpenSpecOperationOutcome>> ExecuteAsync(
        OpenSpecOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ActorId))
        {
            return ServiceResult<OpenSpecOperationOutcome>.FromError(
                new ServiceError(ServiceErrorCode.Unauthorized, "An authenticated actor is required."));
        }

        if (!OpenSpecWorkspaceGuard.IsSupported(request.Operation))
        {
            return ServiceResult<OpenSpecOperationOutcome>.FromError(
                new ServiceError(
                    ServiceErrorCode.Validation,
                    "The requested command is not supported; no command was executed."));
        }

        if (!OpenSpecWorkspaceGuard.IsSafeTarget(request.Target))
        {
            return ServiceResult<OpenSpecOperationOutcome>.FromError(
                new ServiceError(
                    ServiceErrorCode.Validation,
                    "The request contains an unsupported command, arbitrary arguments, or an executable; no command was executed."));
        }

        if (!guard.IsWithinWorkspace(request.WorkspacePath, out var workspace))
        {
            return ServiceResult<OpenSpecOperationOutcome>.FromError(
                new ServiceError(
                    ServiceErrorCode.Validation,
                    "The workspace is outside the authorized root; no command was executed."));
        }

        var stateChanging = OpenSpecOperations.IsStateChanging(request.Operation);
        if (stateChanging && !request.Confirmed)
        {
            // Preview the operation and affected change without executing it.
            return ServiceResult<OpenSpecOperationOutcome>.FromValue(
                new OpenSpecOperationOutcome(
                    false,
                    request.Operation,
                    StateChanging: true,
                    PreviewRequired: true,
                    Output: Describe(request),
                    Error: null));
        }

        var arguments = OpenSpecOperations.BuildArguments(request.Operation, request.Target);
        if (arguments.Count == 0)
        {
            return ServiceResult<OpenSpecOperationOutcome>.FromError(
                new ServiceError(ServiceErrorCode.Validation, "The requested command is not supported."));
        }

        var result = await runner.RunAsync(workspace, arguments, cancellationToken);
        if (result.ExitCode != 0)
        {
            return ServiceResult<OpenSpecOperationOutcome>.FromValue(
                new OpenSpecOperationOutcome(
                    false,
                    request.Operation,
                    stateChanging,
                    PreviewRequired: false,
                    Output: result.StandardOutput,
                    Error: string.IsNullOrWhiteSpace(result.StandardError)
                        ? $"The OpenSpec operation exited with code {result.ExitCode}."
                        : result.StandardError));
        }

        return ServiceResult<OpenSpecOperationOutcome>.FromValue(
            new OpenSpecOperationOutcome(
                true,
                request.Operation,
                stateChanging,
                PreviewRequired: false,
                Output: result.StandardOutput,
                Error: null));
    }

    public static string Describe(OpenSpecOperationRequest request) =>
        $"Preview: openspec {string.Join(' ', OpenSpecOperations.BuildArguments(request.Operation, request.Target))} in {request.WorkspacePath}";
}
