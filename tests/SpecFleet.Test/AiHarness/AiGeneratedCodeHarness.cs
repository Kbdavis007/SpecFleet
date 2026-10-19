using System.Diagnostics;
using System.Text;
using SpecFleet.Service.Features.Chat.Artifacts;

namespace SpecFleet.Test.AiHarness;

/// <summary>
/// The AI-generated-code harness. It runs generated artifacts through the real validation
/// path and real build/compilation, then applies structural and project-boundary checks.
/// It reports what it actually verified; it never claims arbitrary generated code is
/// defect-free. Harness cases that require the hosted Gemini provider are reported blocked
/// when no credential is configured.
/// </summary>
public sealed class AiGeneratedCodeHarness
{
    private readonly ArtifactValidator _validator = new();

    public async Task<HarnessResult> RunAsync(ArtifactType artifactType, string generatedContent)
    {
        var checks = new List<HarnessCheck>();

        var validationFailures = _validator.Validate(artifactType, generatedContent);
        checks.Add(new HarnessCheck(
            "structural-validation",
            validationFailures.Count == 0,
            validationFailures.Count == 0
                ? "Structural validation passed."
                : string.Join("; ", validationFailures.Select(failure => failure.Message))));

        if (artifactType is ArtifactType.Code or ArtifactType.Worker && validationFailures.Count == 0)
        {
            checks.Add(await CompileAsync(generatedContent));
            checks.Add(CheckProjectBoundaries(generatedContent));
        }

        return new HarnessResult(artifactType, checks);
    }

    private static HarnessCheck CheckProjectBoundaries(string content)
    {
        var violations = new List<string>();
        if (content.Contains("SpecFleet.Api", StringComparison.Ordinal))
        {
            violations.Add("Generated Service content must not reference the Api project.");
        }

        if (content.Contains("SpecFleet.Workers", StringComparison.Ordinal))
        {
            violations.Add("Generated Service content must not reference the Workers project.");
        }

        return new HarnessCheck(
            "project-boundaries",
            violations.Count == 0,
            violations.Count == 0 ? "No forbidden project references." : string.Join("; ", violations));
    }

    private static async Task<HarnessCheck> CompileAsync(string content)
    {
        var directory = Directory.CreateTempSubdirectory("SpecFleet.AiHarness.");
        try
        {
            var assemblyName = "Generated" + Guid.NewGuid().ToString("N");
            await File.WriteAllTextAsync(
                Path.Combine(directory.FullName, "Generated.cs"),
                "namespace GeneratedArtifact;" + Environment.NewLine + content);

            var project = new StringBuilder()
                .AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">")
                .AppendLine("  <PropertyGroup>")
                .AppendLine("    <TargetFramework>net10.0</TargetFramework>")
                .AppendLine("    <Nullable>enable</Nullable>")
                .AppendLine("    <ImplicitUsings>enable</ImplicitUsings>")
                .AppendLine($"    <AssemblyName>{assemblyName}</AssemblyName>")
                .AppendLine("    <EnableDefaultCompileItems>true</EnableDefaultCompileItems>")
                .AppendLine("  </PropertyGroup>")
                .AppendLine("</Project>")
                .ToString();
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "Generated.csproj"), project);

            var startInfo = new ProcessStartInfo("dotnet", "build Generated.csproj -nodeReuse:false -v:q")
            {
                WorkingDirectory = directory.FullName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start the compilation harness.");
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var succeeded = process.ExitCode == 0;
            return new HarnessCheck(
                "compilation",
                succeeded,
                succeeded ? "Generated code compiled." : Truncate(output + error));
        }
        finally
        {
            try
            {
                directory.Delete(recursive: true);
            }
            catch (IOException)
            {
                // Temporary harness directory cleanup is best-effort.
            }
        }
    }

    private static string Truncate(string value) =>
        value.Length <= 600 ? value : value[..600];

    public static bool HostedProviderAvailable =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY"));
}

public sealed record HarnessCheck(string Name, bool Passed, string Detail);

public sealed record HarnessResult(ArtifactType ArtifactType, IReadOnlyList<HarnessCheck> Checks)
{
    public bool AllPassed => Checks.All(check => check.Passed);
}
