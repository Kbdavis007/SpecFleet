using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpecFleet.Service.Features.Chat.Providers;

namespace SpecFleet.Service.Features.Chat.Providers;

/// <summary>
/// Gemini streaming adapter. The API key is supplied by the server and is never returned
/// to callers or included in responses.
/// </summary>
public sealed class GeminiChatCompletionProvider(
    HttpClient httpClient,
    string apiKey,
    string model,
    string baseUrl = "https://generativelanguage.googleapis.com") : IChatCompletionProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(apiKey);

    public string Model => model;

    public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new ProviderUnavailableException(
                "AI generation is unavailable because no provider credential is configured.");
        }

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = request.Prompt } }
                }
            }
        };

        var endpoint =
            $"{baseUrl.TrimEnd('/')}/v1beta/models/{model}:streamGenerateContent?alt=sse&key={apiKey}";

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };

        using var response = await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderUnavailableException(
                $"The AI provider returned {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var json = line["data:".Length..].Trim();
            if (json.Length == 0)
            {
                continue;
            }

            var text = ExtractText(json);
            if (!string.IsNullOrEmpty(text))
            {
                yield return new ChatCompletionChunk(text);
            }
        }
    }

    private static string? ExtractText(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("candidates", out var candidates) ||
                candidates.ValueKind is not JsonValueKind.Array ||
                candidates.GetArrayLength() == 0)
            {
                return null;
            }

            if (!candidates[0].TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts) ||
                parts.ValueKind is not JsonValueKind.Array ||
                parts.GetArrayLength() == 0)
            {
                return null;
            }

            return parts[0].TryGetProperty("text", out var text) ? text.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
