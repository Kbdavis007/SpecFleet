using SpecFleet.Service.Features.GitHubActions;
using SpecFleet.Test.Support;

namespace SpecFleet.Test.Features.GitHub;

/// <summary>
/// Exercises the real <see cref="HttpGitHubClient"/> against the local GitHub-protocol
/// server for the review-branch flow. The protocol server validates ref creation the way
/// GitHub does, so a client that sends a branch name where a commit SHA is required is
/// caught here rather than silently succeeding against a permissive stub.
/// </summary>
public sealed class HttpGitHubClientTests
{
    private static readonly GitHubRepositoryRef Repository = new("example", "repo");

    [Fact]
    public async Task Review_branch_creation_resolves_the_base_branch_to_a_commit_sha()
    {
        await using var server = GitHubProtocolServer.Authorizing(canWrite: true);
        var client = new HttpGitHubClient(
            new HttpClient { BaseAddress = new Uri(server.BaseUrl) },
            "test-token");

        var result = await client.WriteFilesAsync(
            Repository,
            "review/acceptance",
            "main",
            [new GitHubFileChange("docs/readme.md", "content", IsNew: true)]);

        Assert.Equal("review/acceptance", result.ReviewBranch);
        Assert.Contains("docs/readme.md", result.ChangedPaths);
    }

    [Fact]
    public async Task Review_branch_creation_surfaces_a_genuine_422_instead_of_treating_it_as_exists()
    {
        await using var server = GitHubProtocolServer.RejectingReviewBranch();
        var client = new HttpGitHubClient(
            new HttpClient { BaseAddress = new Uri(server.BaseUrl) },
            "test-token");

        var exception = await Assert.ThrowsAsync<GitHubApiException>(() =>
            client.WriteFilesAsync(
                Repository,
                "review/acceptance",
                "main",
                [new GitHubFileChange("docs/readme.md", "content", IsNew: true)]));

        Assert.Equal(422, exception.StatusCode);
    }

    [Fact]
    public async Task Review_branch_creation_tolerates_an_existing_reference()
    {
        await using var server = GitHubProtocolServer.Authorizing(canWrite: true);
        var client = new HttpGitHubClient(
            new HttpClient { BaseAddress = new Uri(server.BaseUrl) },
            "test-token");

        // The second call targets the same review branch, so the server reports
        // "Reference already exists" and the idempotent apply must still succeed.
        await client.WriteFilesAsync(
            Repository,
            "review/idempotent",
            "main",
            [new GitHubFileChange("docs/one.md", "one", IsNew: true)]);

        var second = await client.WriteFilesAsync(
            Repository,
            "review/idempotent",
            "main",
            [new GitHubFileChange("docs/two.md", "two", IsNew: true)]);

        Assert.Contains("docs/two.md", second.ChangedPaths);
    }
}
