using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SpecFleet.Service.Features.GitHubActions;
using SpecFleet.Test.Support;

namespace SpecFleet.Test.Features.GitHub;

/// <summary>
/// Verifies the GitHub Minimal API transport end to end against a genuinely running API
/// process, the shared real PostgreSQL container, and a real local GitHub-protocol server.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class GitHubActionEndpointsTests
{
    private const string Title = "Reviewed title";
    private const string Body = "Reviewed body";

    [Fact]
    public async Task GitHub_action_endpoint_requires_authentication()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();
        await using var api = await ApiProcessHarness.StartAsync(state.ConnectionString);

        using var anonymous = new HttpClient { BaseAddress = new Uri(api.BaseUrl) };
        using var response = await anonymous.PostAsJsonAsync("/api/github/actions/execute", new
        {
            repositoryOwner = "example",
            repositoryName = "repo",
            kind = "CreateIssue",
            decision = "Confirmed",
            previewDigest = GitHubPreviewDigest.Compute(Title, Body),
            title = Title,
            body = Body
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Confirmed_issue_creation_round_trips_through_the_running_api()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();

        await using var gitHub = GitHubProtocolServer.Authorizing(canWrite: true);
        await using var api = await ApiProcessHarness.StartAsync(
            state.ConnectionString,
            gitHubBaseUrl: gitHub.BaseUrl,
            gitHubAccessToken: "server-only-token");

        using var response = await api.Client.PostAsJsonAsync("/api/github/actions/execute", new
        {
            repositoryOwner = "example",
            repositoryName = "repo",
            kind = "CreateIssue",
            decision = "Confirmed",
            previewDigest = GitHubPreviewDigest.Compute(Title, Body),
            title = Title,
            body = Body
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("succeeded").GetBoolean());
        Assert.Contains("issues/1", document.RootElement.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Unallowlisted_workflow_is_rejected_without_dispatch()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();

        await using var gitHub = GitHubProtocolServer.Authorizing(canWrite: true);
        await using var api = await ApiProcessHarness.StartAsync(
            state.ConnectionString,
            gitHubBaseUrl: gitHub.BaseUrl,
            gitHubAccessToken: "server-only-token",
            gitHubAllowedWorkflows: ["build.yml"]);

        using var response = await api.Client.PostAsJsonAsync("/api/github/actions/execute", new
        {
            repositoryOwner = "example",
            repositoryName = "repo",
            kind = "DispatchWorkflow",
            decision = "Confirmed",
            previewDigest = GitHubPreviewDigest.ComputeWorkflow(
                "deploy-production.yml",
                new Dictionary<string, string>()),
            workflowFileName = "deploy-production.yml",
            reference = "main"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Insufficient_permission_is_reported_as_an_authorization_failure()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();

        await using var gitHub = GitHubProtocolServer.Authorizing(canWrite: false);
        await using var api = await ApiProcessHarness.StartAsync(
            state.ConnectionString,
            gitHubBaseUrl: gitHub.BaseUrl,
            gitHubAccessToken: "server-only-token");

        using var response = await api.Client.PostAsJsonAsync("/api/github/actions/execute", new
        {
            repositoryOwner = "example",
            repositoryName = "repo",
            kind = "CreateIssue",
            decision = "Confirmed",
            previewDigest = GitHubPreviewDigest.Compute(Title, Body),
            title = Title,
            body = Body
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
