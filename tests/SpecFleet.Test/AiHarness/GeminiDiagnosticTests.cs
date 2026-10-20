using System.Text;
using System.Text.Json;

namespace SpecFleet.Test.AiHarness;

// Temporary CI diagnostic: reports the live model list and the exact response for a
// streamGenerateContent call so the 404 can be diagnosed from the CI log.
public sealed class GeminiDiagnosticTests
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com";

    [Fact]
    public async Task Diagnose_model_list_and_stream_response()
    {
        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Assert.Fail("DIAG: no GEMINI_API_KEY");
        }

        using var httpClient = new HttpClient();
        var report = new StringBuilder();

        using var listResponse = await httpClient.GetAsync($"{BaseUrl}/v1beta/models?key={apiKey}");
        report.AppendLine($"DIAG list status={(int)listResponse.StatusCode}");
        var listBody = await listResponse.Content.ReadAsStringAsync();

        var names = new List<string>();
        using (var document = JsonDocument.Parse(listBody))
        {
            foreach (var model in document.RootElement.GetProperty("models").EnumerateArray())
            {
                var name = model.GetProperty("name").GetString() ?? "";
                var methods = model.TryGetProperty("supportedGenerationMethods", out var m)
                    ? string.Join(",", m.EnumerateArray().Select(x => x.GetString()))
                    : "none";
                if (name.Contains("flash", StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(name.Replace("models/", ""));
                    report.AppendLine($"DIAG model={name} methods={methods}");
                }
            }
        }

        foreach (var candidate in names.Take(4))
        {
            var endpoint = $"{BaseUrl}/v1beta/models/{candidate}:streamGenerateContent?alt=sse&key={apiKey}";
            using var streamRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(new
                {
                    contents = new[] { new { parts = new[] { new { text = "say ready" } } } }
                })
            };
            using var streamResponse = await httpClient.SendAsync(streamRequest);
            var body = await streamResponse.Content.ReadAsStringAsync();
            report.AppendLine(
                $"DIAG stream model={candidate} status={(int)streamResponse.StatusCode} body={body[..Math.Min(300, body.Length)]}");
        }

        Assert.Fail(report.ToString());
    }
}
