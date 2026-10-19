namespace SpecFleet.Service.Features.GitHubActions;

/// <summary>
/// Repository write policy. Paths must stay inside the workspace and outside protected
/// areas; workflows must be explicitly allowlisted before dispatch.
/// </summary>
public sealed class GitHubRepositoryPolicy
{
    private static readonly string[] ProtectedSegments = [".git", ".github/workflows"];

    private static readonly string[] DisallowedPrefixes = ["/", "..", "~"];

    private readonly HashSet<string> _allowedWorkflows;

    public GitHubRepositoryPolicy(IEnumerable<string>? allowedWorkflows = null) =>
        _allowedWorkflows = new HashSet<string>(
            allowedWorkflows ?? [],
            StringComparer.OrdinalIgnoreCase);

    public bool IsPathAllowed(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0'))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/').Trim();

        if (DisallowedPrefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.Ordinal)) ||
            normalized.Contains("../", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalized))
        {
            return false;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment is "." or "..")
            {
                return false;
            }

            if (ProtectedSegments.Contains(segment, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public bool IsWorkflowAllowed(string workflowFileName) =>
        !string.IsNullOrWhiteSpace(workflowFileName) &&
        _allowedWorkflows.Contains(workflowFileName);

    public IReadOnlyCollection<string> AllowedWorkflows => _allowedWorkflows;
}
