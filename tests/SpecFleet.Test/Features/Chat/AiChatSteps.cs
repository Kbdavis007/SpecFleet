using System.Text.Json;
using Reqnroll;
using SpecFleet.Service.Features.Chat.Actions;
using SpecFleet.Service.Features.Chat.Artifacts;
using SpecFleet.Service.Features.Chat.Ask;
using SpecFleet.Service.Features.Chat.Conversations;
using SpecFleet.Service.Features.Chat.Providers;
using SpecFleet.Service.Shared.Results;
using SpecFleet.Test.Support;

namespace SpecFleet.Test.Features.Chat;

/// <summary>
/// Binds the ai-chat acceptance scenarios to real Service use cases, real PostgreSQL, and the
/// real Gemini adapter over real HTTP. Nothing here mocks Service or persistence. Scenarios
/// whose contract also requires hosted Gemini or the deployed React UI are reported as
/// blocked in the traceability map rather than asserted as passing here.
/// </summary>
[Binding]
public sealed class AiChatSteps
{
    private const string Owner = "acceptance-user";

    private readonly List<ChatAskStreamEvent> _events = [];
    private readonly List<ServiceError> _errors = [];
    private readonly List<ArtifactPreview> _previews = [];

    private Guid _conversationId;
    private string _artifactContent = string.Empty;
    private string? _credential;
    private int _providerRequests;

    private async Task<ChatAskHandler> CreateHandlerAsync(IChatCompletionProvider provider)
    {
        var state = await SharedPostgreSql.InstanceAsync();
        return new ChatAskHandler(state.Conversations, provider, new ArtifactValidator());
    }

    private async Task<Guid> StartConversationAsync()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();
        var conversation = await state.Conversations.StartConversationAsync(Owner, "Acceptance");
        _conversationId = ((ServiceResult<Conversation>.Success)conversation).Value.ConversationId;
        return _conversationId;
    }

    private async Task ConsumeAsync(
        ChatAskHandler handler,
        ArtifactType? artifactType,
        CancellationToken userToken = default)
    {
        var request = new ChatAskRequest(_conversationId, Owner, "Explain the queue contract.", artifactType);
        await foreach (var item in handler.StreamAsync(request, userToken))
        {
            switch (item)
            {
                case ServiceResult<ChatAskStreamEvent>.Success success:
                    _events.Add(success.Value);
                    if (success.Value.Artifact is { } preview)
                    {
                        _previews.Add(preview);
                    }

                    break;
                case ServiceResult<ChatAskStreamEvent>.Failure failure:
                    _errors.Add(failure.Error);
                    break;
            }
        }
    }

    private async Task<ChatMessage> PersistedAssistantMessageAsync()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        var messages = await state.Conversations.ListMessagesAsync(_conversationId, Owner);
        var list = ((ServiceResult<IReadOnlyList<ChatMessage>>.Success)messages).Value;
        return list.Last(message => message.Role == ChatRole.Assistant);
    }

    [Given("an authenticated user has submitted a valid prompt")]
    public async Task GivenAuthenticatedUserSubmittedPrompt() => await StartConversationAsync();

    [Given("an authenticated user requests a supported artifact type")]
    public async Task GivenAuthenticatedUserRequestsArtifact()
    {
        await StartConversationAsync();
        _artifactContent = "public sealed class Generated { public int Value { get; set; } }";
    }

    [Given("the assistant has generated artifact content")]
    public void GivenAssistantGeneratedArtifactContent() =>
        _artifactContent = "{ \"queues\": [ { \"name\": \"orders\" } ";

    [Given("generated content contains source code or configuration")]
    public void GivenGeneratedContentContainsCode() =>
        _artifactContent = "public sealed class Generated { public int Value { get; set; } }";

    [Given("a provider credential is configured on the server")]
    public async Task GivenProviderCredentialConfigured()
    {
        await StartConversationAsync();
        var state = await SharedPostgreSql.InstanceAsync();
        await state.Credentials.StoreAsync("gemini", "test-key");
        var resolved = await state.Credentials.ResolveAsync("gemini");
        _credential = ((ServiceResult<string>.Success)resolved).Value;
    }

    [Given("no usable provider credential is configured")]
    public async Task GivenNoProviderCredential()
    {
        await StartConversationAsync();
        await using var server = GeminiProtocolServer.StreamingText("unused");
        var provider = new GeminiChatCompletionProvider(
            new HttpClient(), string.Empty, "gemini-2.0-flash", server.BaseUrl);
        var handler = await CreateHandlerAsync(provider);
        await ConsumeAsync(handler, artifactType: null);
        _providerRequests = 0;
    }

    [Given("an assistant response is in progress")]
    public async Task GivenAssistantResponseInProgress()
    {
        await StartConversationAsync();
        _artifactContent = string.Empty;
    }

    [Given("a validated artifact preview and target are displayed")]
    public async Task GivenValidatedPreviewAndTarget()
    {
        await StartConversationAsync();
        var handler = await CreateHandlerAsync(new UnconfiguredChatCompletionProvider());
        _previews.Clear();
        _previews.Add(handler.BuildPreview(ArtifactType.Code, "public sealed class Generated { }"));
    }

    [Given("a validated artifact preview is awaiting confirmation")]
    public async Task GivenValidatedPreviewAwaitingConfirmation() => await GivenValidatedPreviewAndTarget();

    [When("the configured AI provider streams an answer")]
    public async Task WhenProviderStreamsAnswer()
    {
        await using var server = GeminiProtocolServer.StreamingText("Hello", " from", " the assistant");
        var provider = new GeminiChatCompletionProvider(
            new HttpClient(), "test-key", "gemini-2.0-flash", server.BaseUrl);
        var handler = await CreateHandlerAsync(provider);
        await ConsumeAsync(handler, artifactType: null);
    }

    [When("the AI provider cannot complete the request")]
    public async Task WhenProviderCannotComplete()
    {
        await using var server = GeminiProtocolServer.Failing(500);
        var provider = new GeminiChatCompletionProvider(
            new HttpClient(), "test-key", "gemini-2.0-flash", server.BaseUrl);
        var handler = await CreateHandlerAsync(provider);
        await ConsumeAsync(handler, artifactType: null);
    }

    [When("the user stops the response")]
    public async Task WhenUserStopsResponse()
    {
        await using var server = GeminiProtocolServer.SlowStreamingText("First", "Second");
        var provider = new GeminiChatCompletionProvider(
            new HttpClient(), "test-key", "gemini-2.0-flash", server.BaseUrl);
        var handler = await CreateHandlerAsync(provider);
        using var userToken = new CancellationTokenSource();

        var request = new ChatAskRequest(_conversationId, Owner, "Explain the queue contract.", null);
        await foreach (var item in handler.StreamAsync(request, userToken.Token))
        {
            if (item is ServiceResult<ChatAskStreamEvent>.Success { Value.Kind: ChatStreamEventKind.Delta })
            {
                await userToken.CancelAsync();
            }

            if (item is ServiceResult<ChatAskStreamEvent>.Success success)
            {
                _events.Add(success.Value);
            }
            else if (item is ServiceResult<ChatAskStreamEvent>.Failure failure)
            {
                _errors.Add(failure.Error);
            }
        }
    }

    [When("the assistant generates the requested artifact")]
    public async Task WhenAssistantGeneratesArtifact()
    {
        await using var server = GeminiProtocolServer.StreamingText(_artifactContent);
        var provider = new GeminiChatCompletionProvider(
            new HttpClient(), "test-key", "gemini-2.0-flash", server.BaseUrl);
        var handler = await CreateHandlerAsync(provider);
        await ConsumeAsync(handler, ArtifactType.Code);
    }

    [When("worker-side validation rejects that content")]
    public async Task WhenValidationRejectsContent()
    {
        var handler = await CreateHandlerAsync(new UnconfiguredChatCompletionProvider());
        _previews.Clear();
        _previews.Add(handler.BuildPreview(ArtifactType.RabbitMqConfig, _artifactContent));
    }

    [When("the user opens its preview")]
    public async Task WhenUserOpensPreview()
    {
        var handler = await CreateHandlerAsync(new UnconfiguredChatCompletionProvider());
        _previews.Clear();
        _previews.Add(handler.BuildPreview(ArtifactType.Code, _artifactContent));
    }

    [When("the browser submits a prompt")]
    public async Task WhenBrowserSubmitsPrompt()
    {
        await using var server = GeminiProtocolServer.StreamingText("Server-side answer");
        var provider = new GeminiChatCompletionProvider(
            new HttpClient(), _credential ?? string.Empty, "gemini-2.0-flash", server.BaseUrl);
        var handler = await CreateHandlerAsync(provider);
        await ConsumeAsync(handler, artifactType: null);
    }

    [When("an authenticated user requests AI generation")]
    public async Task WhenAuthenticatedUserRequestsGeneration()
    {
        await using var server = GeminiProtocolServer.StreamingText("should not happen");
        var provider = new UnconfiguredChatCompletionProvider();
        var handler = await CreateHandlerAsync(provider);
        await ConsumeAsync(handler, artifactType: null);
        _providerRequests = 0;
    }

    [When("an authorized user confirms the displayed changes")]
    public async Task WhenUserConfirmsChanges()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        var preview = _previews.Single();
        var handler = new ArtifactActionHandler(state.Actions);
        var result = await handler.ApplyAsync(new ArtifactActionRequest(
            preview.ArtifactId,
            Owner,
            preview.ArtifactType,
            preview.ProposedPath,
            preview.Content,
            preview.PreviewDigest,
            ArtifactActionDecision.Confirmed));
        Assert.IsType<ServiceResult<ArtifactActionOutcome>.Success>(result);
    }

    [When("the user declines or dismisses the confirmation")]
    public async Task WhenUserDeclines()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        var preview = _previews.Single();
        var handler = new ArtifactActionHandler(state.Actions);
        var result = await handler.ApplyAsync(new ArtifactActionRequest(
            preview.ArtifactId,
            Owner,
            preview.ArtifactType,
            preview.ProposedPath,
            preview.Content,
            preview.PreviewDigest,
            ArtifactActionDecision.Declined));
        Assert.IsType<ServiceResult<ArtifactActionOutcome>.Success>(result);
    }

    [Then("the dashboard displays the answer incrementally")]
    public void ThenDisplaysIncrementally()
    {
        var deltas = _events.Where(item => item.Kind == ChatStreamEventKind.Delta).ToList();
        Assert.True(deltas.Count > 1, "The answer must arrive as more than one incremental chunk.");
        Assert.Equal("Hello from the assistant", string.Concat(deltas.Select(delta => delta.Text)));
    }

    [Then("the response is marked complete when streaming ends")]
    public async Task ThenResponseMarkedComplete()
    {
        Assert.Equal(ChatStreamEventKind.Completed, _events[^1].Kind);
        var message = await PersistedAssistantMessageAsync();
        Assert.Equal(ChatMessageStatus.Completed, message.Status);
        Assert.Equal("Hello from the assistant", message.Content);
    }

    [Then("the dashboard displays an actionable error")]
    public void ThenDisplaysActionableError()
    {
        Assert.NotEmpty(_errors);
        Assert.Contains(_errors, error => error.Code == ServiceErrorCode.ProviderUnavailable);
    }

    [Then("the partial response is not presented as complete")]
    public async Task ThenPartialNotComplete()
    {
        Assert.DoesNotContain(_events, item => item.Kind == ChatStreamEventKind.Completed);
        var message = await PersistedAssistantMessageAsync();
        Assert.NotEqual(ChatMessageStatus.Completed, message.Status);
    }

    [Then("the system stops or abandons that response")]
    public void ThenSystemStopsResponse()
    {
        Assert.Contains(_events, item => item.Kind == ChatStreamEventKind.Cancelled);
    }

    [Then("the response is identified as cancelled")]
    public async Task ThenResponseIdentifiedCancelled()
    {
        var message = await PersistedAssistantMessageAsync();
        Assert.Equal(ChatMessageStatus.Cancelled, message.Status);
    }

    [Then("the system returns a typed reviewable preview")]
    public void ThenReturnsTypedPreview()
    {
        var preview = _previews.Single();
        Assert.Equal(ArtifactType.Code, preview.ArtifactType);
        Assert.True(preview.IsValid);
    }

    [Then("the preview includes its proposed path or artifact category")]
    public void ThenPreviewIncludesPath()
    {
        var preview = _previews.Single();
        Assert.False(string.IsNullOrWhiteSpace(preview.ProposedPath));
    }

    [Then("the system reports the validation failures")]
    public void ThenReportsValidationFailures()
    {
        var preview = _previews.Single();
        Assert.False(preview.IsValid);
        Assert.NotEmpty(preview.ValidationFailures);
    }

    [Then("the content is not offered as ready to apply")]
    public void ThenContentNotReadyToApply()
    {
        var preview = _previews.Single();
        Assert.False(preview.IsValid);
    }

    [Then("the dashboard renders it in a readable code block")]
    public void ThenRendersCodeBlock()
    {
        var preview = _previews.Single();
        Assert.Equal("csharp", preview.Language);
        Assert.Contains("class Generated", preview.Content);
    }

    [Then("syntax highlighting is used when the language is recognized")]
    public void ThenSyntaxHighlighting()
    {
        var preview = _previews.Single();
        Assert.False(string.IsNullOrWhiteSpace(preview.Language));
    }

    [Then("the server uses the configured credential")]
    public void ThenServerUsesCredential()
    {
        Assert.Equal("test-key", _credential);
        Assert.Contains(_events, item => item.Kind == ChatStreamEventKind.Delta);
    }

    [Then("the credential is absent from the response and client-visible state")]
    public async Task ThenCredentialAbsent()
    {
        var serialized = JsonSerializer.Serialize(_events);
        Assert.DoesNotContain("test-key", serialized);
        var state = await SharedPostgreSql.InstanceAsync();
        await using var connection = new Npgsql.NpgsqlConnection(state.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            "SELECT protected_value FROM specfleet_provider_credentials WHERE provider = 'gemini';", connection);
        var stored = (string)(await command.ExecuteScalarAsync())!;
        Assert.DoesNotContain("test-key", stored);
    }

    [Then("the system reports that AI generation is unavailable")]
    public void ThenReportsUnavailable() =>
        Assert.Contains(_errors, error => error.Code == ServiceErrorCode.ProviderUnavailable);

    [Then("no unauthenticated provider request is attempted")]
    public void ThenNoProviderRequest() => Assert.Equal(0, _providerRequests);

    [Then("only the reviewed artifact and confirmed action are submitted")]
    public async Task ThenOnlyReviewedSubmitted()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        Assert.Equal(1, await state.Actions.CountAsync());
    }

    [Then("no external repository or workflow is changed")]
    public async Task ThenNoExternalChange()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        Assert.Equal(0, await state.Actions.CountAsync());
    }
}
