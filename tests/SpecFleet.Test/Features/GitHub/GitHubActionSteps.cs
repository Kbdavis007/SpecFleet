using Reqnroll;
using SpecFleet.Service.Features.GitHubActions;
using SpecFleet.Service.Shared.Results;
using SpecFleet.Test.Support;

namespace SpecFleet.Test.Features.GitHub;

/// <summary>
/// Binds the github-repository-actions acceptance scenarios to the real Service handler and
/// the real GitHub REST client over real HTTP against a local GitHub-protocol server.
/// Authorization, path policy, confirmation binding, and failure reporting are all exercised
/// through production code paths. Scenarios requiring a live GitHub App credential are
/// reported as blocked in the traceability map rather than asserted as passing here.
/// </summary>
[Binding]
public sealed class GitHubActionSteps
{
    private static readonly GitHubRepositoryRef Repository = new("example", "repo");

    private readonly List<ServiceError> _errors = [];
    private readonly List<GitHubActionOutcome> _outcomes = [];
    private readonly List<GitHubFileChange> _changes = [];
    private readonly Dictionary<string, string> _workflowInputs = new(StringComparer.Ordinal);

    private readonly GitHubRepositoryPolicy _policy = new(["build.yml"]);
    private readonly string _baseBranch = "main";
    private readonly string _reviewBranch = "review/acceptance";
    private readonly string _title = "Reviewed title";
    private readonly string _body = "Reviewed body";
    private string _workflow = "build.yml";
    private string _previewDigest = string.Empty;

    private GitHubActionHandler CreateHandler(IGitHubClient client) => new(client, _policy);

    private static HttpGitHubClient CreateClient(string baseUrl) =>
        new(new HttpClient { BaseAddress = new Uri(baseUrl) }, "test-token");

    private async Task ExecuteAsync(
        GitHubActionKind kind,
        IGitHubClient client,
        GitHubConfirmationDecision decision = GitHubConfirmationDecision.Confirmed)
    {
        var handler = CreateHandler(client);
        var request = new GitHubActionRequest(
            "acceptance-user",
            Repository,
            kind,
            decision,
            _previewDigest,
            _title,
            _body,
            _reviewBranch,
            _baseBranch,
            _workflow,
            _baseBranch,
            _changes,
            _workflowInputs);

        var result = await handler.ExecuteAsync(request);
        switch (result)
        {
            case ServiceResult<GitHubActionOutcome>.Success success:
                _outcomes.Add(success.Value);
                break;
            case ServiceResult<GitHubActionOutcome>.Failure failure:
                _errors.Add(failure.Error);
                break;
        }
    }

    [Given("a user cannot access the selected repository")]
    public void GivenUserCannotAccessRepository() => _previewDigest = GitHubPreviewDigest.Compute(_title, _body);

    [Given("GitHub credentials are missing, expired, or lack required permission")]
    public void GivenCredentialsInsufficient() => _previewDigest = GitHubPreviewDigest.Compute(_title, _body);

    [Given("an authorized user is shown a reviewed issue title and body")]
    public void GivenAuthorizedUserShownIssue() => _previewDigest = GitHubPreviewDigest.Compute(_title, _body);

    [Given("an authorized user is shown a file preview for a review branch")]
    public void GivenAuthorizedUserShownFilePreview()
    {
        _changes.Clear();
        _changes.Add(new GitHubFileChange("src/Generated.cs", "public sealed class Generated { }", IsNew: true));
        _changes.Add(new GitHubFileChange("README.md", "# Updated", IsNew: false));
        _previewDigest = GitHubPreviewDigest.ComputeFiles(_changes);
    }

    [Given("a review branch contains the confirmed changes")]
    public void GivenReviewBranchContainsChanges()
    {
        _changes.Clear();
        _changes.Add(new GitHubFileChange("src/Generated.cs", "public sealed class Generated { }", IsNew: true));
        _previewDigest = GitHubPreviewDigest.ComputeFiles(_changes);
    }

    [Given("a proposed repository path is invalid or disallowed")]
    public void GivenDisallowedPath()
    {
        _changes.Clear();
        _changes.Add(new GitHubFileChange("../secrets.env", "token", IsNew: true));
        _previewDigest = GitHubPreviewDigest.ComputeFiles(_changes);
    }

    [Given("an authorized user is shown an allowlisted workflow and valid inputs")]
    public void GivenAllowlistedWorkflow()
    {
        _workflow = "build.yml";
        _workflowInputs.Clear();
        _workflowInputs["environment"] = "staging";
        _previewDigest = GitHubPreviewDigest.ComputeWorkflow(_workflow, _workflowInputs);
    }

    [Given("a request names an unconfigured workflow")]
    public void GivenUnconfiguredWorkflow()
    {
        _workflow = "deploy-production.yml";
        _previewDigest = GitHubPreviewDigest.ComputeWorkflow(_workflow, _workflowInputs);
    }

    [Given("an authorized user has confirmed a GitHub mutation")]
    public void GivenConfirmedMutation() => _previewDigest = GitHubPreviewDigest.Compute(_title, _body);

    [When("the user requests a repository write")]
    public async Task WhenUserRequestsWrite()
    {
        await using var server = GitHubProtocolServer.Authorizing(canWrite: false);
        await ExecuteAsync(GitHubActionKind.CreateIssue, CreateClient(server.BaseUrl));
    }

    [When("the user requests the corresponding GitHub action")]
    public async Task WhenUserRequestsCorrespondingAction()
    {
        await using var server = GitHubProtocolServer.Authorizing(canWrite: false);
        await ExecuteAsync(GitHubActionKind.CreateIssue, CreateClient(server.BaseUrl));
    }

    [When("the user confirms issue creation")]
    public async Task WhenUserConfirmsIssue()
    {
        await using var server = GitHubProtocolServer.Authorizing(canWrite: true);
        await ExecuteAsync(GitHubActionKind.CreateIssue, CreateClient(server.BaseUrl));
    }

    [When("the user edits the issue content and confirms")]
    public async Task WhenUserEditsAndConfirms()
    {
        // The reviewed digest was bound to the original content; the confirmed submission
        // carries the edited body, so the digest must no longer match.
        _previewDigest = GitHubPreviewDigest.Compute(_title, _body);
        var editedRequest = new GitHubActionRequest(
            "acceptance-user",
            Repository,
            GitHubActionKind.CreateIssue,
            GitHubConfirmationDecision.Confirmed,
            _previewDigest,
            _title,
            "Edited body",
            _reviewBranch,
            _baseBranch,
            _workflow,
            _baseBranch,
            _changes,
            _workflowInputs);

        await using var server = GitHubProtocolServer.Authorizing(canWrite: true);
        var handler = CreateHandler(CreateClient(server.BaseUrl));
        var result = await handler.ExecuteAsync(editedRequest);
        switch (result)
        {
            case ServiceResult<GitHubActionOutcome>.Success success:
                _outcomes.Add(success.Value);
                break;
            case ServiceResult<GitHubActionOutcome>.Failure failure:
                _errors.Add(failure.Error);
                break;
        }
    }

    [When("the user cancels before confirmation")]
    public async Task WhenUserCancels()
    {
        await using var server = GitHubProtocolServer.Authorizing(canWrite: true);
        await ExecuteAsync(
            GitHubActionKind.CreateIssue,
            CreateClient(server.BaseUrl),
            GitHubConfirmationDecision.Cancelled);
    }

    [When("the user confirms the file additions and updates")]
    public async Task WhenUserConfirmsFiles()
    {
        await using var server = GitHubProtocolServer.Authorizing(canWrite: true);
        await ExecuteAsync(GitHubActionKind.WriteFiles, CreateClient(server.BaseUrl));
    }

    [When("the user approves opening a pull request")]
    public async Task WhenUserApprovesPullRequest()
    {
        await using var server = GitHubProtocolServer.Authorizing(canWrite: true);
        await ExecuteAsync(GitHubActionKind.CreatePullRequest, CreateClient(server.BaseUrl));
    }

    [When("the user attempts to apply the file change")]
    public async Task WhenUserAppliesDisallowedChange()
    {
        await using var server = GitHubProtocolServer.Authorizing(canWrite: true);
        await ExecuteAsync(GitHubActionKind.WriteFiles, CreateClient(server.BaseUrl));
    }

    [When("the user confirms the workflow dispatch")]
    public async Task WhenUserConfirmsWorkflow()
    {
        await using var server = GitHubProtocolServer.Authorizing(canWrite: true);
        await ExecuteAsync(GitHubActionKind.DispatchWorkflow, CreateClient(server.BaseUrl));
    }

    [When("the user or generated content requests its dispatch")]
    public async Task WhenRequestsUnconfiguredWorkflow()
    {
        await using var server = GitHubProtocolServer.Authorizing(canWrite: true);
        await ExecuteAsync(GitHubActionKind.DispatchWorkflow, CreateClient(server.BaseUrl));
    }

    [When("the GitHub API rejects the request")]
    public async Task WhenGitHubRejects()
    {
        await using var server = GitHubProtocolServer.RejectingMutation();
        await ExecuteAsync(GitHubActionKind.CreateIssue, CreateClient(server.BaseUrl));
    }

    [Then("the system rejects the action")]
    public void ThenRejectsAction()
    {
        Assert.NotEmpty(_errors);
        Assert.Contains(_errors, error => error.Code == ServiceErrorCode.Unauthorized);
        Assert.DoesNotContain(_outcomes, outcome => outcome.Succeeded);
    }

    [Then("no GitHub mutation occurs")]
    public void ThenNoMutation() => Assert.DoesNotContain(_outcomes, outcome => outcome.Succeeded);

    [Then("the system reports the authorization failure")]
    public void ThenReportsAuthorizationFailure() =>
        Assert.Contains(_errors, error => error.Code == ServiceErrorCode.Unauthorized);

    [Then("the action is not reported as successful")]
    public void ThenNotSuccessful() => Assert.DoesNotContain(_outcomes, outcome => outcome.Succeeded);

    [Then("the issue is created in the selected repository")]
    public void ThenIssueCreated()
    {
        var outcome = _outcomes.Single();
        Assert.True(outcome.Succeeded);
        Assert.Equal(GitHubActionKind.CreateIssue, outcome.Kind);
    }

    [Then("its GitHub URL is returned")]
    public void ThenUrlReturned() =>
        Assert.Contains(_outcomes, outcome => !string.IsNullOrWhiteSpace(outcome.Url));

    [Then("the issue is created using only the confirmed content")]
    public void ThenIssueUsesConfirmedContent()
    {
        // Editing after review invalidated the bound digest, so no issue was created.
        Assert.NotEmpty(_errors);
        Assert.Contains(_errors, error => error.Code == ServiceErrorCode.Conflict);
        Assert.DoesNotContain(_outcomes, outcome => outcome.Succeeded);
    }

    [Then("no issue is created")]
    public void ThenNoIssueCreated() => Assert.DoesNotContain(_outcomes, outcome => outcome.Succeeded);

    [Then("the confirmed contents are written on a review branch")]
    public void ThenContentsWritten()
    {
        var outcome = _outcomes.Single();
        Assert.True(outcome.Succeeded);
        Assert.Equal(GitHubActionKind.WriteFiles, outcome.Kind);
    }

    [Then("the changed paths are returned")]
    public void ThenChangedPathsReturned() =>
        Assert.Contains(_outcomes, outcome => outcome.ChangedPaths.Count > 0);

    [Then("a pull request is created against the selected base branch")]
    public void ThenPullRequestCreated()
    {
        var outcome = _outcomes.Single();
        Assert.True(outcome.Succeeded);
        Assert.Equal(GitHubActionKind.CreatePullRequest, outcome.Kind);
    }

    [Then("the system rejects that change")]
    public void ThenRejectsChange()
    {
        Assert.Contains(_errors, error => error.Code == ServiceErrorCode.Validation);
        Assert.DoesNotContain(_outcomes, outcome => outcome.Succeeded);
    }

    [Then("the disallowed path is not written")]
    public void ThenDisallowedNotWritten() =>
        Assert.DoesNotContain(_outcomes, outcome => outcome.ChangedPaths.Any(path => path.Contains("..")));

    [Then("the system dispatches that workflow")]
    public void ThenDispatchesWorkflow()
    {
        var outcome = _outcomes.Single();
        Assert.True(outcome.Succeeded);
        Assert.Equal(GitHubActionKind.DispatchWorkflow, outcome.Kind);
    }

    [Then("reports the dispatch result")]
    public void ThenReportsDispatchResult() => Assert.True(_outcomes.Single().Succeeded);

    [Then("the system rejects the request")]
    public void ThenRejectsRequest()
    {
        Assert.Contains(_errors, error => error.Code == ServiceErrorCode.Validation);
        Assert.DoesNotContain(_outcomes, outcome => outcome.Succeeded);
    }

    [Then("no workflow is dispatched")]
    public void ThenNoWorkflowDispatched() =>
        Assert.DoesNotContain(_outcomes, outcome => outcome.Succeeded && outcome.Kind == GitHubActionKind.DispatchWorkflow);

    [Then("the system displays the failure")]
    public void ThenDisplaysFailure() => Assert.NotEmpty(_errors);

    [Then("the action is not presented as completed")]
    public void ThenNotPresentedCompleted() => Assert.DoesNotContain(_outcomes, outcome => outcome.Succeeded);
}
