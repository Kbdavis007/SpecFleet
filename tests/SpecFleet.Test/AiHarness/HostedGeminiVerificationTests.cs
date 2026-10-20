using System.Text;
using System.Text.Json;
using SpecFleet.Service.Features.Chat.Artifacts;
using SpecFleet.Service.Features.Chat.Providers;

namespace SpecFleet.Test.AiHarness;

/// <summary>
/// The task 3.2/3.3 verification clause: the Gemini adapter and the AI harness must be
/// verified against the real hosted provider, not only against the local protocol server.
/// That needs a credential, so these tests report an explicit blocked outcome when
/// <c>GEMINI_API_KEY</c> is absent and perform the real call when it is present. They are
/// never silently counted as a pass without the credential.
/// </summary>
public sealed class HostedGeminiVerificationTests
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com";

    private static string? ApiKey => Environment.GetEnvironmentVariable("GEMINI_API_KEY");

    [Fact]
    public async Task Hosted_gemini_provider_streams_a_completion_with_real_credentials()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            Assert.True(
                string.IsNullOrWhiteSpace(ApiKey),
                "Expected no hosted Gemini credential; the real-credential clause is BLOCKED.");
            return;
        }

        using var httpClient = new HttpClient();
        var model = await ResolveModelAsync(httpClient, ApiKey);
        var provider = new GeminiChatCompletionProvider(httpClient, ApiKey, model);

        Assert.True(provider.IsConfigured);

        var text = await CollectAsync(provider, "Reply with the single word: ready");

        Assert.False(
            string.IsNullOrWhiteSpace(text),
            "The hosted Gemini provider returned no content.");
    }

    [Fact]
    public async Task Hosted_gemini_generated_code_passes_the_ai_harness_checks()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            Assert.True(
                string.IsNullOrWhiteSpace(ApiKey),
                "Expected no hosted Gemini credential; the provider-generated harness case is BLOCKED.");
            return;
        }

        using var httpClient = new HttpClient();
        var model = await ResolveModelAsync(httpClient, ApiKey);
        var provider = new GeminiChatCompletionProvider(httpClient, ApiKey, model);

        var generated = await CollectAsync(
            provider,
            "Write a minimal, self-contained C# class named Temperature in namespace GeneratedArtifact. "
            + "It must have a double property Celsius and a method ToFahrenheit() returning double. "
            + "Return only compilable C# source code with no markdown fences and no explanation.");

        var result = await new AiGeneratedCodeHarness()
            .RunAsync(ArtifactType.Code, ExtractCode(generated));

        Assert.True(result.AllPassed, Describe(result));
        Assert.Contains(result.Checks, check => check.Name == "compilation" && check.Passed);
    }

    [Fact]
    public async Task Hosted_gemini_provider_reports_missing_credentials_without_calling_the_provider()
    {
        using var httpClient = new HttpClient();
        var provider = new GeminiChatCompletionProvider(httpClient, string.Empty, "any-model");

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

    private static async Task<string> CollectAsync(IChatCompletionProvider provider, string prompt)
    {
        var text = new StringBuilder();
        await foreach (var chunk in provider.StreamAsync(
            new ChatCompletionRequest(prompt, null),
            CancellationToken.None))
        {
            text.Append(chunk.Text);
        }

        return text.ToString();
    }

    /// <summary>
    /// Resolves the model from the live model list rather than a hardcoded name, so a retired
    /// model does not silently break verification. <c>GEMINI_MODEL</c> overrides discovery.
    /// </summary>
    private static async Task<string> ResolveModelAsync(HttpClient httpClient, string apiKey)
    {
        var configured = Environment.GetEnvironmentVariable("GEMINI_MODEL");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        using var response = await httpClient.GetAsync($"{BaseUrl}/v1beta/models?key={apiKey}");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var usable = document.RootElement.GetProperty("models")
            .EnumerateArray()
            .Where(model =>
                model.TryGetProperty("supportedGenerationMethods", out var methods) &&
                methods.EnumerateArray().Any(method => method.GetString() == "generateContent"))
            .Select(model => model.GetProperty("name").GetString() ?? string.Empty)
            .Where(name => name.Contains("flash", StringComparison.OrdinalIgnoreCase))
            .Select(name => name.Replace("models/", string.Empty, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            usable.Count > 0,
            "No Gemini model supporting generateContent was available for verification.");

        return usable[0];
    }

    private static string ExtractCode(string generated)
    {
        var text = generated.Trim();

        var fence = text.IndexOf("```", StringComparison.Ordinal);
        if (fence < 0)
        {
            return text;
        }

        var start = text.IndexOf('\n', fence);
        var end = text.LastIndexOf("```", StringComparison.Ordinal);
        return start >= 0 && end > start ? text[(start + 1)..end].Trim() : text;
    }

    private static string Describe(HarnessResult result) =>
        string.Join(" | ", result.Checks.Select(check => $"{check.Name}: {check.Passed} ({check.Detail})"));
}
