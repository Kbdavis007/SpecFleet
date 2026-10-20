using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SpecFleet.Test.Support;

namespace SpecFleet.Test.Features.Chat;

/// <summary>
/// Verifies the chat Minimal API transport end to end against a genuinely running API process
/// and the shared real PostgreSQL container: HTTP mappings, streaming, and authorization.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class ChatApiEndpointsTests
{
    [Fact]
    public async Task Chat_endpoints_require_authentication()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();
        await using var api = await ApiProcessHarness.StartAsync(state.ConnectionString);

        using var anonymous = new HttpClient { BaseAddress = new Uri(api.BaseUrl) };
        using var response = await anonymous.PostAsJsonAsync(
            "/api/chat/conversations",
            new { title = "Anonymous" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Conversation_and_messages_round_trip_through_the_running_api()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();
        await using var api = await ApiProcessHarness.StartAsync(state.ConnectionString);

        using var create = await api.Client.PostAsJsonAsync("/api/chat/conversations", new { title = "Round trip" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using var document = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var conversationId = document.RootElement.GetProperty("conversationId").GetGuid();

        using var messages = await api.Client.GetAsync($"/api/chat/conversations/{conversationId}/messages");
        Assert.Equal(HttpStatusCode.OK, messages.StatusCode);
    }

    [Fact]
    public async Task Streaming_ask_returns_incremental_events_and_never_leaks_the_credential()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();

        await using var gemini = GeminiProtocolServer.StreamingText("Streamed", " answer");
        await using var api = await ApiProcessHarness.StartAsync(
            state.ConnectionString,
            geminiApiKey: "server-only-secret",
            geminiModel: "gemini-2.0-flash",
            geminiBaseUrl: gemini.BaseUrl);

        using var create = await api.Client.PostAsJsonAsync("/api/chat/conversations", new { title = "Stream" });
        using var createDocument = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var conversationId = createDocument.RootElement.GetProperty("conversationId").GetGuid();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/ask")
        {
            Content = JsonContent.Create(new
            {
                conversationId,
                ownerId = "ignored-by-server",
                prompt = "Explain the queue.",
                artifactType = (string?)null
            })
        };

        using var response = await api.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Delta", body, StringComparison.Ordinal);
        Assert.Contains("Streamed", body, StringComparison.Ordinal);
        Assert.Contains("Completed", body, StringComparison.Ordinal);
        Assert.DoesNotContain("server-only-secret", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Artifact_preview_endpoint_reports_validation_failures()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();
        await using var api = await ApiProcessHarness.StartAsync(state.ConnectionString);

        using var response = await api.Client.PostAsJsonAsync(
            "/api/chat/artifacts/preview",
            new { artifactType = "RabbitMqConfig", content = "{ not json" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("isValid").GetBoolean());
        Assert.NotEmpty(document.RootElement.GetProperty("validationFailures").EnumerateArray());
    }
}
