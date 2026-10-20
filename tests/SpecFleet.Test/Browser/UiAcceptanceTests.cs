using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using SpecFleet.Test.Support;

namespace SpecFleet.Test.Browser;

/// <summary>
/// Browser-driven acceptance tests for the independently deployed React/TypeScript dashboard.
/// Each test serves the real UI build from its own origin, starts the real API process against
/// real PostgreSQL, and drives Chromium with Playwright. Nothing here mocks Service or
/// persistence; the UI talks to the genuine API over HTTP and streaming.
/// </summary>
public sealed class UiAcceptanceTests : PageTest
{
    private static string UiUrl(UiHostHarness ui, ApiProcessHarness api) =>
        $"{ui.BaseUrl}/?apiBase={Uri.EscapeDataString(api.BaseUrl)}&token={ApiProcessHarness.TestToken}";

    [Fact]
    public async Task Dashboard_streams_answer_incrementally_and_marks_it_complete()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();

        await using var ui = await UiHostHarness.StartAsync();
        await using var gemini = GeminiProtocolServer.StreamingText("Hello", " from", " the assistant");
        await using var api = await ApiProcessHarness.StartAsync(
            state.ConnectionString,
            geminiApiKey: "server-only-secret",
            geminiModel: "gemini-2.0-flash",
            geminiBaseUrl: gemini.BaseUrl,
            uiOrigin: ui.BaseUrl);

        await Page.GotoAsync(UiUrl(ui, api));
        await Expect(Page.GetByTestId("prompt")).ToBeVisibleAsync();

        await Page.GetByTestId("prompt").FillAsync("Explain the queue.");
        await Page.GetByTestId("send").ClickAsync();

        await Expect(Page.GetByTestId("streaming-response")).ToContainTextAsync("Hello from the assistant");
        await Expect(Page.GetByTestId("message-history")).ToContainTextAsync("Hello from the assistant");
        await Expect(Page.GetByTestId("message-history").Locator(".message-assistant:has(.status[data-status='Completed'])")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task Dashboard_shows_actionable_error_and_does_not_mark_partial_response_complete()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();

        await using var ui = await UiHostHarness.StartAsync();
        await using var gemini = GeminiProtocolServer.Failing(503);
        await using var api = await ApiProcessHarness.StartAsync(
            state.ConnectionString,
            geminiApiKey: "server-only-secret",
            geminiModel: "gemini-2.0-flash",
            geminiBaseUrl: gemini.BaseUrl,
            uiOrigin: ui.BaseUrl);

        await Page.GotoAsync(UiUrl(ui, api));
        await Page.GetByTestId("prompt").FillAsync("Trigger a provider failure.");
        await Page.GetByTestId("send").ClickAsync();

        await Expect(Page.GetByTestId("chat-error")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("message-history").Locator(".message-assistant:has(.status[data-status='Completed'])")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Dashboard_reports_ai_unavailable_when_no_credential_is_configured()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();

        await using var ui = await UiHostHarness.StartAsync();
        await using var api = await ApiProcessHarness.StartAsync(
            state.ConnectionString,
            uiOrigin: ui.BaseUrl);

        await Page.GotoAsync(UiUrl(ui, api));
        await Page.GetByTestId("prompt").FillAsync("Are you available?");
        await Page.GetByTestId("send").ClickAsync();

        await Expect(Page.GetByTestId("chat-error")).ToContainTextAsync("unavailable");
        await Expect(Page.GetByTestId("message-history").Locator(".message-assistant:has(.status[data-status='Completed'])")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Dashboard_cancels_an_in_progress_response()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();

        await using var ui = await UiHostHarness.StartAsync();
        await using var gemini = GeminiProtocolServer.SlowStreamingText("First", "Second", "Third", "Fourth");
        await using var api = await ApiProcessHarness.StartAsync(
            state.ConnectionString,
            geminiApiKey: "server-only-secret",
            geminiModel: "gemini-2.0-flash",
            geminiBaseUrl: gemini.BaseUrl,
            uiOrigin: ui.BaseUrl);

        await Page.GotoAsync(UiUrl(ui, api));
        await Page.GetByTestId("prompt").FillAsync("Give me a long answer.");
        await Page.GetByTestId("send").ClickAsync();

        await Expect(Page.GetByTestId("streaming-response")).ToContainTextAsync("First");
        await Page.GetByTestId("cancel").ClickAsync();

        await Expect(Page.GetByTestId("streaming-response")).ToHaveAttributeAsync("data-state", "cancelled");
        await Expect(Page.GetByTestId("message-history").Locator(".message-assistant:has(.status[data-status='Completed'])")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Dashboard_renders_a_typed_reviewable_preview_with_its_path()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();

        await using var ui = await UiHostHarness.StartAsync();
        await using var gemini = GeminiProtocolServer.StreamingText("public class Sample { }");
        await using var api = await ApiProcessHarness.StartAsync(
            state.ConnectionString,
            geminiApiKey: "server-only-secret",
            geminiModel: "gemini-2.0-flash",
            geminiBaseUrl: gemini.BaseUrl,
            uiOrigin: ui.BaseUrl);

        await Page.GotoAsync(UiUrl(ui, api));
        await Page.GetByTestId("artifact-type").SelectOptionAsync("Code");
        await Page.GetByTestId("prompt").FillAsync("Generate a sample class.");
        await Page.GetByTestId("send").ClickAsync();

        await Expect(Page.GetByTestId("artifact-type-label")).ToHaveTextAsync("Code");
        await Expect(Page.GetByTestId("artifact-path")).ToContainTextAsync("src/generated/Sample.cs");
        await Expect(Page.GetByTestId("artifact-valid")).ToHaveTextAsync("valid");
    }

    [Fact]
    public async Task Dashboard_reports_validation_failures_and_does_not_offer_invalid_content()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();

        await using var ui = await UiHostHarness.StartAsync();
        // Unbalanced delimiters fail the code artifact validator.
        await using var gemini = GeminiProtocolServer.StreamingText("public class Broken {");
        await using var api = await ApiProcessHarness.StartAsync(
            state.ConnectionString,
            geminiApiKey: "server-only-secret",
            geminiModel: "gemini-2.0-flash",
            geminiBaseUrl: gemini.BaseUrl,
            uiOrigin: ui.BaseUrl);

        await Page.GotoAsync(UiUrl(ui, api));
        await Page.GetByTestId("artifact-type").SelectOptionAsync("Code");
        await Page.GetByTestId("prompt").FillAsync("Generate broken code.");
        await Page.GetByTestId("send").ClickAsync();

        await Expect(Page.GetByTestId("validation-failures")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("not-applyable")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("confirm-apply")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Dashboard_renders_generated_code_in_a_highlighted_code_block()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();

        await using var ui = await UiHostHarness.StartAsync();
        await using var gemini = GeminiProtocolServer.StreamingText("public class Sample { }");
        await using var api = await ApiProcessHarness.StartAsync(
            state.ConnectionString,
            geminiApiKey: "server-only-secret",
            geminiModel: "gemini-2.0-flash",
            geminiBaseUrl: gemini.BaseUrl,
            uiOrigin: ui.BaseUrl);

        await Page.GotoAsync(UiUrl(ui, api));
        await Page.GetByTestId("artifact-type").SelectOptionAsync("Code");
        await Page.GetByTestId("prompt").FillAsync("Generate a sample class.");
        await Page.GetByTestId("send").ClickAsync();

        var code = Page.GetByTestId("artifact-code");
        await Expect(code).ToBeVisibleAsync();
        await Expect(code).ToHaveAttributeAsync("data-language", "csharp");
        await Expect(code.Locator(".hljs-keyword, .hljs-type, .hljs-title").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Dashboard_submits_only_the_confirmed_artifact_action()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();

        await using var ui = await UiHostHarness.StartAsync();
        await using var gemini = GeminiProtocolServer.StreamingText("public class Sample { }");
        await using var api = await ApiProcessHarness.StartAsync(
            state.ConnectionString,
            geminiApiKey: "server-only-secret",
            geminiModel: "gemini-2.0-flash",
            geminiBaseUrl: gemini.BaseUrl,
            uiOrigin: ui.BaseUrl);

        await Page.GotoAsync(UiUrl(ui, api));
        await Page.GetByTestId("artifact-type").SelectOptionAsync("Code");
        await Page.GetByTestId("prompt").FillAsync("Generate a sample class.");
        await Page.GetByTestId("send").ClickAsync();

        await Expect(Page.GetByTestId("confirm-apply")).ToBeVisibleAsync();
        await Page.GetByTestId("confirm-apply").ClickAsync();

        // Confirmation clears the pending preview and records the action.
        await Expect(Page.GetByTestId("no-artifact")).ToBeVisibleAsync();

        await using var connection = new Npgsql.NpgsqlConnection(state.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            "SELECT COUNT(*) FROM specfleet_artifact_actions;",
            connection);
        var recorded = Convert.ToInt64(await command.ExecuteScalarAsync());
        Assert.Equal(1, recorded);
    }

    [Fact]
    public async Task Dashboard_declining_leaves_external_state_unchanged()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();

        await using var ui = await UiHostHarness.StartAsync();
        await using var gemini = GeminiProtocolServer.StreamingText("public class Sample { }");
        await using var api = await ApiProcessHarness.StartAsync(
            state.ConnectionString,
            geminiApiKey: "server-only-secret",
            geminiModel: "gemini-2.0-flash",
            geminiBaseUrl: gemini.BaseUrl,
            uiOrigin: ui.BaseUrl);

        await Page.GotoAsync(UiUrl(ui, api));
        await Page.GetByTestId("artifact-type").SelectOptionAsync("Code");
        await Page.GetByTestId("prompt").FillAsync("Generate a sample class.");
        await Page.GetByTestId("send").ClickAsync();

        await Expect(Page.GetByTestId("decline-apply")).ToBeVisibleAsync();
        await Page.GetByTestId("decline-apply").ClickAsync();
        await Expect(Page.GetByTestId("no-artifact")).ToBeVisibleAsync();

        await using var connection = new Npgsql.NpgsqlConnection(state.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            "SELECT COUNT(*) FROM specfleet_artifact_actions;",
            connection);
        var recorded = Convert.ToInt64(await command.ExecuteScalarAsync());
        Assert.Equal(0, recorded);
    }

    [Fact]
    public async Task Dashboard_runs_an_allowlisted_openspec_read_only_operation()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();

        var workspace = Directory.CreateTempSubdirectory("SpecFleet.Ui.OpenSpec.");

        try
        {
            await using var ui = await UiHostHarness.StartAsync();
            await using var api = await ApiProcessHarness.StartAsync(
                state.ConnectionString,
                openSpecWorkspaceRoot: workspace.FullName,
                uiOrigin: ui.BaseUrl);

            await Page.GotoAsync(UiUrl(ui, api));
            await Page.GetByTestId("openspec-workspace").FillAsync(workspace.FullName);
            await Page.GetByTestId("openspec-operation").SelectOptionAsync("List");
            await Page.GetByTestId("openspec-run").ClickAsync();

            await Expect(Page.GetByTestId("openspec-result")).ToBeVisibleAsync();
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Dashboard_dispatches_a_supported_worker_task_and_shows_status()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();

        await using var ui = await UiHostHarness.StartAsync();
        await using var api = await ApiProcessHarness.StartAsync(
            state.ConnectionString,
            uiOrigin: ui.BaseUrl);

        await Page.GotoAsync(UiUrl(ui, api));
        await Page.GetByTestId("worker-task-type").SelectOptionAsync("GenerateWorker");
        await Page.GetByTestId("worker-payload").FillAsync("{\"name\":\"example\",\"language\":\"csharp\"}");
        await Page.GetByTestId("worker-dispatch").ClickAsync();

        await Expect(Page.GetByTestId("worker-status")).ToContainTextAsync("Queued");

        await Page.GetByTestId("worker-refresh").ClickAsync();
        await Expect(Page.GetByTestId("worker-status")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Dashboard_reports_an_unsupported_worker_task_without_dispatching()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();

        await using var ui = await UiHostHarness.StartAsync();
        await using var api = await ApiProcessHarness.StartAsync(
            state.ConnectionString,
            uiOrigin: ui.BaseUrl);

        await Page.GotoAsync(UiUrl(ui, api));
        // Missing the required 'language' field for GenerateWorker.
        await Page.GetByTestId("worker-payload").FillAsync("{\"name\":\"example\"}");
        await Page.GetByTestId("worker-dispatch").ClickAsync();

        await Expect(Page.GetByTestId("chat-error")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("worker-status")).ToHaveCountAsync(0);
    }
}
