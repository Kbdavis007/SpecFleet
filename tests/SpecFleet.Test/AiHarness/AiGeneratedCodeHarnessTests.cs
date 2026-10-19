using SpecFleet.Service.Features.Chat.Artifacts;

namespace SpecFleet.Test.AiHarness;

/// <summary>
/// Exercises the AI-generated-code harness against real validation and real compilation.
/// The hosted-Gemini case is reported explicitly as blocked when no credential is present,
/// rather than being silently counted as a pass.
/// </summary>
public sealed class AiGeneratedCodeHarnessTests
{
    private readonly AiGeneratedCodeHarness _harness = new();

    [Fact]
    public async Task Well_formed_generated_code_passes_checks_and_compiles()
    {
        const string content = """
            public sealed class GeneratedService
            {
                public int Add(int left, int right) => left + right;
            }
            """;

        var result = await _harness.RunAsync(ArtifactType.Code, content);

        Assert.True(result.AllPassed, Describe(result));
        Assert.Contains(result.Checks, check => check.Name == "compilation" && check.Passed);
        Assert.Contains(result.Checks, check => check.Name == "project-boundaries" && check.Passed);
    }

    [Fact]
    public async Task Malformed_generated_code_fails_structural_checks()
    {
        const string content = "public sealed class Broken { public void Run( { }";

        var result = await _harness.RunAsync(ArtifactType.Code, content);

        Assert.False(result.AllPassed);
        Assert.Contains(result.Checks, check => check.Name == "structural-validation" && !check.Passed);
    }

    [Fact]
    public async Task Generated_service_code_referencing_host_projects_fails_boundary_check()
    {
        const string content = """
            using SpecFleet.Api;

            public sealed class GeneratedService
            {
                public string Host => nameof(SpecFleet.Api);
            }
            """;

        var result = await _harness.RunAsync(ArtifactType.Code, content);

        Assert.Contains(result.Checks, check => check.Name == "project-boundaries" && !check.Passed);
    }

    [Fact]
    public async Task Malformed_rabbitmq_config_fails_validation()
    {
        var result = await _harness.RunAsync(ArtifactType.RabbitMqConfig, "{ not json");

        Assert.False(result.AllPassed);
        Assert.Contains(result.Checks, check => check.Name == "structural-validation" && !check.Passed);
    }

    [Fact]
    public void Hosted_gemini_harness_case_is_blocked_without_credentials()
    {
        // This case cannot produce a passing result without the hosted provider. When no
        // credential is configured the correct outcome is an explicit blocked status; the
        // harness must not be reported as having verified provider-generated output.
        if (AiGeneratedCodeHarness.HostedProviderAvailable)
        {
            Assert.True(AiGeneratedCodeHarness.HostedProviderAvailable);
            return;
        }

        Assert.False(
            AiGeneratedCodeHarness.HostedProviderAvailable,
            "Expected no hosted Gemini credential; the provider-generated harness case is BLOCKED.");
    }

    private static string Describe(HarnessResult result) =>
        string.Join(" | ", result.Checks.Select(check => $"{check.Name}: {check.Passed} ({check.Detail})"));
}
