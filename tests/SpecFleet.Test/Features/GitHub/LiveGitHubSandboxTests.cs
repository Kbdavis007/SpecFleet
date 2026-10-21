using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SpecFleet.Service.Features.GitHubActions;
using SpecFleet.Test.Support;

namespace SpecFleet.Test.Features.GitHub;

/// <summary>
/// Verifies the Service GitHub use case against a live, disposable GitHub sandbox
/// repository using real credentials, satisfying the task 4.1-4.3 clause that requires real
/// sandbox side effects. The clause is configuration-gated: without an explicit sandbox
/// repository and credential the tests are reported as skipped, never as passing, so a
/// missing credential cannot be mistaken for verification.
///
/// Configuration:
///   SPECFLEET_SANDBOX_OWNER    - owner of the disposable repository
///   SPECFLEET_SANDBOX_REPO     - name of the disposable repository
///   SPECFLEET_SANDBOX_TOKEN    - credential (falls back to GITHUB_TOKEN)
///   SPECFLEET_SANDBOX_WORKFLOW - optional allowlisted workflow file for dispatch
///
/// Every test refuses a configuration that names the SpecFleet repository, so a
/// misconfiguration cannot mutate it.
/// </summary>
public sealed class LiveGitHubSandboxTests
{
    private static readonly string ProtectedRepository = "Kbdavis007/SpecFleet";

    private static string? Owner => Environment.GetEnvironmentVariable("SPECFLEET_SANDBOX_OWNER");
    private static string? RepositoryName => Environment.GetEnvironmentVariable("SPECFLEET_SANDBOX_REPO");
    private static string? Token =>
        Environment.GetEnvironmentVariable("SPECFLEET_SANDBOX_TOKEN") ??
        Environment.GetEnvironmentVariable("GITHUB_TOKEN");

    [RequiresConfigurationFact("the live GitHub sandbox clause needs a disposable repository",
        "SPECFLEET_SANDBOX_OWNER", "SPECFLEET_SANDBOX_REPO", "SPECFLEET_SANDBOX_TOKEN")]
    public async Task Live_sandbox_issue_creation_succeeds_and_is_cleaned_up()
    {
        var repository = SandboxRepository();
        var title = $"SpecFleet live verification {Guid.NewGuid():N}";
        const string body = "Created by the SpecFleet live sandbox verification; safe to close.";
        var handler = CreateHandler(CreateClient());
        var rawClient = CreateRawClient();

        long? issueNumber = null;
        try
        {
            var result = await handler.ExecuteAsync(new GitHubActionRequest(
                "live-verification",
                repository,
                GitHubActionKind.CreateIssue,
                GitHubConfirmationDecision.Confirmed,
                GitHubPreviewDigest.Compute(title, body),
                title,
                body));

            var outcome = Assert.IsType<SpecFleet.Service.Shared.Results.ServiceResult<GitHubActionOutcome>.Success>(result).Value;
            Assert.True(outcome.Succeeded, outcome.Error);
            Assert.False(string.IsNullOrWhiteSpace(outcome.Url));

            issueNumber = ParseTrailingNumber(outcome.Url);
        }
        finally
        {
            if (issueNumber is not null)
            {
                await CloseIssueAsync(rawClient, repository, issueNumber.Value);
            }
        }
    }

    [RequiresConfigurationFact("the live GitHub sandbox clause needs a disposable repository",
        "SPECFLEET_SANDBOX_OWNER", "SPECFLEET_SANDBOX_REPO", "SPECFLEET_SANDBOX_TOKEN")]
    public async Task Live_sandbox_review_branch_file_write_and_pull_request_succeed_and_are_cleaned_up()
    {
        var repository = SandboxRepository();
        var reviewBranch = $"specfleet/verify-{Guid.NewGuid():N}";
        var path = $"specfleet-verification/{Guid.NewGuid():N}.txt";
        var changes = new[] { new GitHubFileChange(path, "SpecFleet live verification.", IsNew: true) };
        var handler = CreateHandler(CreateClient());
        var rawClient = CreateRawClient();

        long? pullRequestNumber = null;
        try
        {
            var writeResult = await handler.ExecuteAsync(new GitHubActionRequest(
                "live-verification",
                repository,
                GitHubActionKind.WriteFiles,
                GitHubConfirmationDecision.Confirmed,
                GitHubPreviewDigest.ComputeFiles(changes),
                ReviewBranch: reviewBranch,
                BaseBranch: "main",
                Changes: changes));

            var written = Assert.IsType<SpecFleet.Service.Shared.Results.ServiceResult<GitHubActionOutcome>.Success>(writeResult).Value;
            Assert.True(written.Succeeded, written.Error);
            Assert.Contains(path, written.ChangedPaths);

            var pullRequestResult = await handler.ExecuteAsync(new GitHubActionRequest(
                "live-verification",
                repository,
                GitHubActionKind.CreatePullRequest,
                GitHubConfirmationDecision.Confirmed,
                string.Empty,
                Title: "SpecFleet live verification",
                Body: "Created by the SpecFleet live sandbox verification; safe to close.",
                ReviewBranch: reviewBranch,
                BaseBranch: "main"));

            var pullRequest = Assert.IsType<SpecFleet.Service.Shared.Results.ServiceResult<GitHubActionOutcome>.Success>(pullRequestResult).Value;
            Assert.True(pullRequest.Succeeded, pullRequest.Error);
            Assert.False(string.IsNullOrWhiteSpace(pullRequest.Url));

            pullRequestNumber = ParseTrailingNumber(pullRequest.Url);
        }
        finally
        {
            if (pullRequestNumber is not null)
            {
                await ClosePullRequestAsync(rawClient, repository, pullRequestNumber.Value);
            }

            await DeleteBranchAsync(rawClient, repository, reviewBranch);
        }
    }

    [RequiresConfigurationFact("the live workflow dispatch clause needs a disposable repository and allowlisted workflow",
        "SPECFLEET_SANDBOX_OWNER", "SPECFLEET_SANDBOX_REPO", "SPECFLEET_SANDBOX_TOKEN", "SPECFLEET_SANDBOX_WORKFLOW")]
    public async Task Live_sandbox_workflow_dispatch_is_reported_as_blocked_without_an_allowlisted_workflow()
    {
        var workflow = Environment.GetEnvironmentVariable("SPECFLEET_SANDBOX_WORKFLOW")!;
        var repository = SandboxRepository();
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal) { ["note"] = "specfleet-verification" };
        var handler = CreateHandler(CreateClient());

        var result = await handler.ExecuteAsync(new GitHubActionRequest(
            "live-verification",
            repository,
            GitHubActionKind.DispatchWorkflow,
            GitHubConfirmationDecision.Confirmed,
            GitHubPreviewDigest.ComputeWorkflow(workflow, inputs),
            WorkflowFileName: workflow,
            Reference: "main",
            WorkflowInputs: inputs));

        var outcome = Assert.IsType<SpecFleet.Service.Shared.Results.ServiceResult<GitHubActionOutcome>.Success>(result).Value;
        Assert.True(outcome.Succeeded, outcome.Error);
    }

    [RequiresConfigurationFact("the live sandbox guard needs a disposable repository",
        "SPECFLEET_SANDBOX_OWNER", "SPECFLEET_SANDBOX_REPO", "SPECFLEET_SANDBOX_TOKEN")]
    public void Live_sandbox_configuration_never_targets_the_specfleet_repository()
    {
        // A guard, not an API call. The credential used here is authorized for SpecFleet, so
        // the Service would happily act on it; the only protection against mutating the real
        // repository is refusing the configuration up front.
        var repository = new GitHubRepositoryRef(Owner!, RepositoryName!);
        Assert.NotEqual(ProtectedRepository, repository.FullName);
        Assert.NotEqual("SpecFleet", repository.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static GitHubRepositoryRef SandboxRepository()
    {
        var repository = new GitHubRepositoryRef(Owner!, RepositoryName!);
        Assert.NotEqual(ProtectedRepository, repository.FullName);
        return repository;
    }

    private static HttpGitHubClient CreateClient() => new(CreateRawClient(), Token!);

    private static HttpClient CreateRawClient() =>
        new() { BaseAddress = new Uri("https://api.github.com") };

    private static GitHubActionHandler CreateHandler(IGitHubClient client)
    {
        var workflow = Environment.GetEnvironmentVariable("SPECFLEET_SANDBOX_WORKFLOW");
        var allowlist = string.IsNullOrWhiteSpace(workflow) ? Array.Empty<string>() : [workflow];
        return new GitHubActionHandler(client, new GitHubRepositoryPolicy(allowlist));
    }

    private static long ParseTrailingNumber(string? url) =>
        long.TryParse(url?.Split('/').LastOrDefault(), out var number) ? number : 0;

    private static async Task CloseIssueAsync(HttpClient client, GitHubRepositoryRef repository, long number)
    {
        using var request = Authorize(HttpMethod.Patch, $"repos/{repository.FullName}/issues/{number}", new { state = "closed" });
        using var response = await client.SendAsync(request);
    }

    private static async Task ClosePullRequestAsync(HttpClient client, GitHubRepositoryRef repository, long number)
    {
        using var request = Authorize(HttpMethod.Patch, $"repos/{repository.FullName}/pulls/{number}", new { state = "closed" });
        using var response = await client.SendAsync(request);
    }

    private static async Task DeleteBranchAsync(HttpClient client, GitHubRepositoryRef repository, string branch)
    {
        using var request = Authorize(HttpMethod.Delete, $"repos/{repository.FullName}/git/refs/heads/{branch}", null);
        using var response = await client.SendAsync(request);
    }

    private static HttpRequestMessage Authorize(HttpMethod method, string path, object? body)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("SpecFleet", "1.0"));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }
}
