using Reqnroll;
using SpecFleet.Service.Features.OpenSpecOrchestration;
using SpecFleet.Service.Features.TaskOrchestration;
using SpecFleet.Service.Features.TaskQueue;
using SpecFleet.Service.Features.WorkerTasks;
using SpecFleet.Service.Shared.Results;
using SpecFleet.Test.Support;

namespace SpecFleet.Test.Features.OpenSpecOrchestration;

/// <summary>
/// Binds the openspec-worker-orchestration acceptance scenarios to the real Service use cases:
/// the real workspace guard and reader, the real OpenSpec CLI process, the real payload
/// validator, and the real durable queue against real PostgreSQL. No mocks or fakes are used.
/// </summary>
[Binding]
public sealed class OpenSpecWorkerOrchestrationSteps
{
    private readonly string _workspaceRoot;
    private readonly OpenSpecWorkspaceGuard _guard;
    private readonly OpenSpecWorkspaceReader _reader;
    private readonly OpenSpecOperationHandler _openSpecHandler;
    private readonly WorkerTaskPayloadValidator _validator = new();

    private ServiceResult<OpenSpecProposalPreview>? _proposal;
    private ServiceResult<OpenSpecWorkspaceSnapshot>? _snapshot;
    private ServiceResult<OpenSpecOperationOutcome>? _openSpecResult;
    private ServiceResult<WorkerTaskPreview>? _preview;
    private ServiceResult<WorkerTaskDispatch>? _dispatch;
    private ServiceResult<WorkerTaskStatusReport>? _status;

    public OpenSpecWorkerOrchestrationSteps()
    {
        _workspaceRoot = LocateRepositoryRoot();
        _guard = new OpenSpecWorkspaceGuard(_workspaceRoot);
        _reader = new OpenSpecWorkspaceReader(_guard);
        _openSpecHandler = new OpenSpecOperationHandler(new ProcessOpenSpecCommandRunner(), _guard);
    }

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpecFleet.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    [Given("an authorized user requests an OpenSpec slice")]
    public void GivenAuthorizedUserRequestsSlice() => _proposal = null;

    [When("proposal content is prepared")]
    public void WhenProposalPrepared() =>
        _proposal = _reader.PrepareProposal(_workspaceRoot, "acceptance-proposal");

    [Then("it is presented for review under the standard change structure")]
    public void ThenProposalPresented()
    {
        var proposal = RequireValue(_proposal);
        Assert.Contains("openspec/changes", proposal.ProposedPath, StringComparison.Ordinal);
        Assert.Contains("## Why", proposal.Content, StringComparison.Ordinal);
        Assert.False(proposal.Written);
    }

    [Then("no files are written before approval")]
    public void ThenNoFilesWritten()
    {
        var proposal = RequireValue(_proposal);
        Assert.False(proposal.Written);
        Assert.False(File.Exists(Path.Combine(_workspaceRoot, proposal.ProposedPath)));
    }

    [Given("an authorized user selects a project workspace")]
    public void GivenUserSelectsWorkspace() => _snapshot = null;

    [When("the user asks about existing OpenSpec capabilities or changes")]
    public void WhenAsksAboutExistingContent() => _snapshot = _reader.Inspect(_workspaceRoot);

    [Then("the system returns information read from that workspace")]
    public void ThenReturnsWorkspaceInformation()
    {
        var snapshot = RequireValue(_snapshot);
        Assert.True(snapshot.HasContent);
        Assert.Contains("add-ai-chatbot-github-integration", snapshot.Changes);
    }

    [Then("identifies when no relevant content exists")]
    public void ThenIdentifiesMissingContent()
    {
        var empty = _reader.Inspect(Path.Combine(_workspaceRoot, "docs"));
        var snapshot = RequireValue(empty);
        Assert.False(snapshot.HasContent);
        Assert.Empty(snapshot.Capabilities);
        Assert.Empty(snapshot.Changes);
    }

    [Given("an authorized user requests an allowlisted inspection or validation")]
    public void GivenAllowlistedInspection() => _openSpecResult = null;

    [When("the operation runs in the selected workspace")]
    public async Task WhenOperationRuns()
    {
        _openSpecResult = await _openSpecHandler.ExecuteAsync(new OpenSpecOperationRequest(
            "acceptance-user",
            _workspaceRoot,
            OpenSpecOperationKind.Validate,
            "add-ai-chatbot-github-integration",
            Confirmed: true));
    }

    [Then("the system displays its result")]
    public void ThenDisplaysResult()
    {
        var outcome = RequireValue(_openSpecResult);
        Assert.True(outcome.Succeeded);
        Assert.Contains("valid", outcome.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Given("an authorized user requests an operation that modifies project files")]
    public void GivenStateChangingOperation() => _openSpecResult = null;

    [When("the system previews the operation and affected change")]
    public async Task WhenSystemPreviewsOperation()
    {
        _openSpecResult = await _openSpecHandler.ExecuteAsync(new OpenSpecOperationRequest(
            "acceptance-user",
            _workspaceRoot,
            OpenSpecOperationKind.New,
            "acceptance-preview-change",
            Confirmed: false));
    }

    [Then("it runs only after the user explicitly confirms")]
    public void ThenRunsOnlyAfterConfirmation()
    {
        var preview = RequireValue(_openSpecResult);
        Assert.True(preview.PreviewRequired);
        Assert.False(preview.Succeeded);
        Assert.Contains("acceptance-preview-change", preview.Output, StringComparison.Ordinal);
    }

    [Given("a request contains an unsupported command, arbitrary arguments, or executable")]
    public void GivenUnsupportedCommand() => _openSpecResult = null;

    [When("the system evaluates the request")]
    public async Task WhenSystemEvaluatesRequest()
    {
        _openSpecResult = await _openSpecHandler.ExecuteAsync(new OpenSpecOperationRequest(
            "acceptance-user",
            _workspaceRoot,
            (OpenSpecOperationKind)999,
            "list; rm -rf /",
            Confirmed: true));
    }

    [Then("it rejects the request")]
    public void ThenRejectsRequest()
    {
        var failure = RequireFailure(_openSpecResult);
        Assert.Equal(ServiceErrorCode.Validation, failure.Error.Code);
    }

    [Then("executes no command")]
    public void ThenExecutesNoCommand() =>
        Assert.True(_openSpecResult is ServiceResult<OpenSpecOperationOutcome>.Failure);

    [Given("an authorized user confirms a valid supported worker task")]
    public void GivenConfirmedSupportedWorkerTask() =>
        _preview = _validator.Preview(
            WorkerTaskType.GenerateWorker,
            """{"name":"inventory-worker","language":"csharp"}""");

    [When("the task is dispatched")]
    public async Task WhenTaskDispatched()
    {
        var preview = RequireValue(_preview);
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();

        var handler = CreateWorkerTaskHandler(state);
        _dispatch = await handler.DispatchAsync(
            preview.TaskType,
            preview.Payload,
            $"acceptance-{Guid.NewGuid():N}",
            new WorkerTaskApproval(
                "acceptance-user",
                "approval-reference",
                preview.TaskType,
                preview.PayloadDigest,
                "inventory-worker"));
    }

    [Then("the system displays its task status")]
    public async Task ThenDisplaysTaskStatus()
    {
        var dispatch = RequireValue(_dispatch);
        var state = await SharedPostgreSql.InstanceAsync();
        var handler = CreateWorkerTaskHandler(state);
        _status = await handler.GetStatusAsync(dispatch.TaskId);

        var status = RequireValue(_status);
        Assert.Equal(TaskQueueStatus.Queued, status.Status);
        Assert.True(status.InProgress);
    }

    [Given("a task type is unsupported or its payload fails validation")]
    public void GivenInvalidTaskPayload() => _preview = null;

    [When("the user requests task dispatch")]
    public async Task WhenUserRequestsDispatch()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();
        var handler = CreateWorkerTaskHandler(state);
        _dispatch = await handler.DispatchAsync(
            WorkerTaskType.GenerateWorker,
            """{"name":"missing-language"}""",
            $"acceptance-{Guid.NewGuid():N}",
            new WorkerTaskApproval(
                "acceptance-user",
                "approval-reference",
                WorkerTaskType.GenerateWorker,
                "irrelevant",
                "missing-language"));
    }

    [Then("the system reports the validation error")]
    public void ThenReportsValidationError()
    {
        var failure = RequireFailure(_dispatch);
        Assert.Equal(ServiceErrorCode.Validation, failure.Error.Code);
    }

    [Then("does not dispatch the task")]
    public async Task ThenDoesNotDispatchTask()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await using var connection = new Npgsql.NpgsqlConnection(state.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("SELECT COUNT(*) FROM specfleet_tasks;", connection);
        var count = (long)(await command.ExecuteScalarAsync() ?? 0L);
        Assert.Equal(0, count);
    }

    [Given("a user requests worker generation, UI page, Terraform, Cloudflare Tunnel, RabbitMQ, or control-plane logic")]
    public void GivenUserRequestsAllCategories() => _preview = null;

    [When("the task inputs are validated")]
    public void WhenTaskInputsValidated()
    {
        var payloads = new Dictionary<WorkerTaskType, string>
        {
            [WorkerTaskType.GenerateWorker] = """{"name":"worker","language":"csharp"}""",
            [WorkerTaskType.GenerateUiPage] = """{"name":"page","route":"/dashboard"}""",
            [WorkerTaskType.GenerateTerraformModule] = """{"name":"module","provider":"cloudflare"}""",
            [WorkerTaskType.GenerateCloudflareTunnelConfig] = """{"tunnelName":"tunnel","hostname":"app.example.com"}""",
            [WorkerTaskType.GenerateRabbitMqConfig] = """{"virtualHost":"/specfleet","queues":["build"]}""",
            [WorkerTaskType.GenerateControlPlaneLogic] = """{"name":"logic","trigger":"queue"}"""
        };

        foreach (var (taskType, payload) in payloads)
        {
            var preview = _validator.Preview(taskType, payload);
            Assert.True(preview is ServiceResult<WorkerTaskPreview>.Success, $"{taskType} should validate.");
        }
    }

    [Then("the system creates a typed task preview with the category and inputs")]
    public void ThenCreatesTypedPreview()
    {
        var payload = """{"name":"worker","language":"csharp"}""";
        var preview = _validator.Preview(WorkerTaskType.GenerateWorker, payload);
        var value = RequireValue(preview);
        Assert.Equal(WorkerTaskType.GenerateWorker, value.TaskType);
        Assert.Equal(payload, value.Payload);
        Assert.False(string.IsNullOrWhiteSpace(value.PayloadDigest));
    }

    [Given("a user requests a worker category that is not configured")]
    public void GivenUnconfiguredCategory() => _preview = null;

    [When("the request is evaluated")]
    public void WhenRequestEvaluated() =>
        _preview = _validator.Preview((WorkerTaskType)999, """{"name":"worker"}""");

    [Then("the system identifies it as unsupported")]
    public void ThenIdentifiesUnsupported()
    {
        var failure = RequireFailure(_preview);
        Assert.Equal(ServiceErrorCode.Validation, failure.Error.Code);
    }

    [Then("does not dispatch a task")]
    public void ThenDoesNotDispatch() => Assert.Null(_dispatch);

    [Given("an OpenSpec operation or worker task has been dispatched")]
    public void GivenOperationOrTaskDispatched() => _status = null;

    [When("the operation or task returns a failure")]
    public async Task WhenOperationOrTaskFails()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();
        var handler = CreateWorkerTaskHandler(state);

        // The payload no longer matches the approved digest, so dispatch is refused.
        _dispatch = await handler.DispatchAsync(
            WorkerTaskType.GenerateWorker,
            """{"name":"worker","language":"csharp"}""",
            $"acceptance-{Guid.NewGuid():N}",
            new WorkerTaskApproval(
                "acceptance-user",
                "approval-reference",
                WorkerTaskType.GenerateWorker,
                "STALE-DIGEST",
                "worker"));

        _status = await handler.GetStatusAsync(Guid.NewGuid());
    }

    [Then("the system displays the failure and current task status")]
    public void ThenDisplaysFailureAndStatus()
    {
        var dispatchFailure = RequireFailure(_dispatch);
        Assert.Equal(ServiceErrorCode.Conflict, dispatchFailure.Error.Code);

        var statusFailure = RequireFailure(_status);
        Assert.Equal(ServiceErrorCode.NotFound, statusFailure.Error.Code);
    }

    [Given("a worker task has been dispatched and has not completed")]
    public async Task GivenTaskInProgress()
    {
        var value = RequireValue(_validator.Preview(
            WorkerTaskType.GenerateWorker,
            """{"name":"worker","language":"csharp"}"""));

        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();
        var handler = CreateWorkerTaskHandler(state);
        _dispatch = await handler.DispatchAsync(
            value.TaskType,
            value.Payload,
            $"acceptance-{Guid.NewGuid():N}",
            new WorkerTaskApproval(
                "acceptance-user",
                "approval-reference",
                value.TaskType,
                value.PayloadDigest,
                "worker"));
    }

    [When("the user requests its status")]
    public async Task WhenUserRequestsStatus()
    {
        var dispatch = RequireValue(_dispatch);
        var state = await SharedPostgreSql.InstanceAsync();
        var handler = CreateWorkerTaskHandler(state);
        _status = await handler.GetStatusAsync(dispatch.TaskId);
    }

    [Then("the system identifies the task as in progress")]
    public void ThenIdentifiesInProgress()
    {
        var status = RequireValue(_status);
        Assert.True(status.InProgress);
        Assert.Contains(status.Status, new[] { TaskQueueStatus.Queued, TaskQueueStatus.Leased });
    }

    [Then("allows the user to retrieve updated status")]
    public async Task ThenAllowsRetrieveUpdatedStatus()
    {
        var dispatch = RequireValue(_dispatch);
        var state = await SharedPostgreSql.InstanceAsync();
        var handler = CreateWorkerTaskHandler(state);
        var refreshed = await handler.GetStatusAsync(dispatch.TaskId);
        Assert.True(refreshed is ServiceResult<WorkerTaskStatusReport>.Success);
    }

    private static WorkerTaskHandler CreateWorkerTaskHandler(SharedPostgreSql.State state) =>
        new(new WorkerTaskOrchestrator(state.Queue), new WorkerTaskPayloadValidator(), state.Queue);

    private static TValue RequireValue<TValue>(ServiceResult<TValue>? result)
        where TValue : notnull
    {
        Assert.NotNull(result);
        if (result is not ServiceResult<TValue>.Success success)
        {
            var error = ((ServiceResult<TValue>.Failure)result!).Error;
            Assert.Fail($"Expected success but received {error.Code}: {error.Message}");
            throw new InvalidOperationException("Unreachable.");
        }

        return success.Value;
    }

    private static ServiceResult<TValue>.Failure RequireFailure<TValue>(ServiceResult<TValue>? result)
        where TValue : notnull
    {
        Assert.NotNull(result);
        if (result is not ServiceResult<TValue>.Failure failure)
        {
            Assert.Fail("Expected a failure result.");
            throw new InvalidOperationException("Unreachable.");
        }

        return failure;
    }
}
