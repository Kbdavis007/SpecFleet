using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SpecFleet.Service.Features.GitHubActions;

/// <summary>
/// Real GitHub REST client. It performs the transport calls the Service handler authorizes
/// and translates non-success responses into <see cref="GitHubApiException"/> so the handler
/// can report an actionable failure instead of a success-shaped outcome.
/// </summary>
public sealed class HttpGitHubClient(HttpClient httpClient, string accessToken) : IGitHubClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GitHubPermission> GetPermissionAsync(
        GitHubRepositoryRef repository,
        GitHubActionKind action,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            $"repos/{repository.FullName}");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return new GitHubPermission(false);
        }

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var canPush = document.RootElement.TryGetProperty("permissions", out var permissions) &&
            permissions.TryGetProperty("push", out var push) &&
            push.ValueKind == JsonValueKind.True;
        return new GitHubPermission(canPush);
    }

    public async Task<GitHubIssue> CreateIssueAsync(
        GitHubRepositoryRef repository,
        string title,
        string body,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(
            HttpMethod.Post,
            $"repos/{repository.FullName}/issues",
            new { title, body });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "create the issue", cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        return new GitHubIssue(
            root.GetProperty("number").GetInt64(),
            root.GetProperty("html_url").GetString() ?? string.Empty);
    }

    public async Task<GitHubFileWriteResult> WriteFilesAsync(
        GitHubRepositoryRef repository,
        string reviewBranch,
        string baseBranch,
        IReadOnlyList<GitHubFileChange> changes,
        CancellationToken cancellationToken = default)
    {
        await EnsureReviewBranchAsync(repository, reviewBranch, baseBranch, cancellationToken);

        var changedPaths = new List<string>();
        foreach (var change in changes)
        {
            using var request = CreateRequest(
                HttpMethod.Put,
                $"repos/{repository.FullName}/contents/{change.Path.TrimStart('/')}",
                new
                {
                    message = $"Apply reviewed change to {change.Path}",
                    content = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(change.Content)),
                    branch = reviewBranch
                });

            using var response = await httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, $"write '{change.Path}'", cancellationToken);
            changedPaths.Add(change.Path);
        }

        return new GitHubFileWriteResult(reviewBranch, changedPaths);
    }

    public async Task<GitHubPullRequest> CreatePullRequestAsync(
        GitHubRepositoryRef repository,
        string headBranch,
        string baseBranch,
        string title,
        string body,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(
            HttpMethod.Post,
            $"repos/{repository.FullName}/pulls",
            new { title, body, head = headBranch, @base = baseBranch });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "create the pull request", cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        return new GitHubPullRequest(
            root.GetProperty("number").GetInt64(),
            root.GetProperty("html_url").GetString() ?? string.Empty);
    }

    public async Task DispatchWorkflowAsync(
        GitHubRepositoryRef repository,
        string workflowFileName,
        string reference,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(
            HttpMethod.Post,
            $"repos/{repository.FullName}/actions/workflows/{workflowFileName}/dispatches",
            new { @ref = reference, inputs });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "dispatch the workflow", cancellationToken);
    }

    private async Task EnsureReviewBranchAsync(
        GitHubRepositoryRef repository,
        string reviewBranch,
        string baseBranch,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Post,
            $"repos/{repository.FullName}/git/refs",
            new { @ref = $"refs/heads/{reviewBranch}", sha = baseBranch });

        using var response = await httpClient.SendAsync(request, cancellationToken);

        // 422 means the review branch already exists, which is acceptable for an idempotent apply.
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            return;
        }

        await EnsureSuccessAsync(response, "create the review branch", cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath, object? body = null)
    {
        var request = new HttpRequestMessage(method, relativePath);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("SpecFleet", "1.0"));

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return request;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string attemptedAction,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = string.IsNullOrWhiteSpace(detail)
            ? $"GitHub rejected the request to {attemptedAction} ({(int)response.StatusCode})."
            : $"GitHub rejected the request to {attemptedAction}: {detail}";
        throw new GitHubApiException(message, (int)response.StatusCode);
    }
}
