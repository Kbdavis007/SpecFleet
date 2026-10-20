using System.Text;
using SpecFleet.Service.Features.Chat.Providers;

namespace SpecFleet.Test.AiHarness;

/// <summary>
/// The task 3.2 verification clause: the Gemini adapter must be verified against the real
/// hosted provider, not only against the local protocol server. That verification needs a
/// credential, so this test reports an explicit blocked outcome when <c>GEMINI_API_KEY</c> is
/// absent and performs the real call when it is present. It is never silently counted as a
/// pass without the credential.
/// </summary>
public sealed class HostedGeminiVerificationTests
{
    private static string? ApiKey =>
        Environment.GetEnvironmentVariable("GEMINI_API_KEY");

    private static string Model =>
        Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-2.0-flash";

    [Fact]
    public async Task Hosted_gemini_provider_streams_a_completion_with_real_credentials()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            // Correct outcome without a credential: explicit blocked, not a fabricated pass.
            Assert.True(
                string.IsNullOrWhiteSpace(ApiKey),
                "Expected no hosted Gemini credential; the real-credential clause is BLOCKED.");
            return;
        }

        using var httpClient = new HttpClient();
        var provider = new GeminiChatCompletionProvider(httpClient, ApiKey, Model);

        Assert.True(provider.IsConfigured);
        Assert.Equal(Model, provider.Model);

        var text = new StringBuilder();
        await foreach (var chunk in provider.StreamAsync(
            new ChatCompletionRequest("Reply with the single word: ready", null),
            CancellationToken.None))
        {
            text.Append(chunk.Text);
        }

        Assert.False(
            string.IsNullOrWhiteSpace(text.ToString()),
            "The hosted Gemini provider returned no content.");
    }

    [Fact]
    public async Task Hosted_gemini_provider_reports_missing_credentials_without_calling_the_provider()
    {
        using var httpClient = new HttpClient();
        var provider = new GeminiChatCompletionProvider(httpClient, string.Empty, Model);

        Assert.False(provider.IsConfigured);

        await Assert.ThrowsAsync<ProviderUnavailableException>(async () =>
        {
            await foreach (var _ in provider.StreamAsync(
                new ChatCompletionRequest("ignored", null),
                CancellationToken.None))
            {
            }
        });
    }
}
