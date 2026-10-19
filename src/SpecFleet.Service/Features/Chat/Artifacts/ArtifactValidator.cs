using System.Text.Json;

namespace SpecFleet.Service.Features.Chat.Artifacts;

/// <summary>
/// Structural validation applied to generated content before it is offered for apply.
/// Validation is intentionally conservative: it rejects clearly malformed content and
/// does not claim that passing proves the artifact is correct.
/// </summary>
public sealed class ArtifactValidator
{
    public IReadOnlyList<ArtifactValidationFailure> Validate(ArtifactType artifactType, string content)
    {
        var failures = new List<ArtifactValidationFailure>();

        if (string.IsNullOrWhiteSpace(content))
        {
            failures.Add(new ArtifactValidationFailure("content.nonempty", "Generated content must not be empty."));
            return failures;
        }

        switch (artifactType)
        {
            case ArtifactType.RabbitMqConfig:
                AddJsonFailure(content, failures);
                break;
            case ArtifactType.CloudflareTunnelConfig:
                AddYamlFailure(content, failures);
                break;
            case ArtifactType.TerraformModule:
                AddTerraformFailure(content, failures);
                break;
            case ArtifactType.Documentation:
            case ArtifactType.OpenSpecProposal:
                AddMarkdownFailure(content, failures);
                break;
            case ArtifactType.Code:
            case ArtifactType.Worker:
                AddBalancedDelimiterFailure(content, failures);
                break;
        }

        return failures;
    }

    private static void AddJsonFailure(string content, List<ArtifactValidationFailure> failures)
    {
        try
        {
            using var _ = JsonDocument.Parse(content);
        }
        catch (JsonException exception)
        {
            failures.Add(new ArtifactValidationFailure("json.parse", $"Content is not valid JSON: {exception.Message}"));
        }
    }

    private static void AddYamlFailure(string content, List<ArtifactValidationFailure> failures)
    {
        if (!content.Contains(':', StringComparison.Ordinal))
        {
            failures.Add(new ArtifactValidationFailure("yaml.structure", "YAML content must contain at least one key/value mapping."));
        }

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.TrimEnd();
            if (trimmed.Contains('\t'))
            {
                failures.Add(new ArtifactValidationFailure("yaml.tabs", "YAML content must not use tab characters for indentation."));
                break;
            }
        }
    }

    private static void AddTerraformFailure(string content, List<ArtifactValidationFailure> failures)
    {
        if (!content.Contains("resource", StringComparison.Ordinal) &&
            !content.Contains("module", StringComparison.Ordinal) &&
            !content.Contains("variable", StringComparison.Ordinal))
        {
            failures.Add(new ArtifactValidationFailure(
                "terraform.block",
                "Terraform content must declare at least one resource, module, or variable block."));
        }

        AddBalancedDelimiterFailure(content, failures);
    }

    private static void AddMarkdownFailure(string content, List<ArtifactValidationFailure> failures)
    {
        if (!content.TrimStart().StartsWith('#'))
        {
            failures.Add(new ArtifactValidationFailure("markdown.heading", "Markdown content must start with a heading."));
        }
    }

    private static void AddBalancedDelimiterFailure(string content, List<ArtifactValidationFailure> failures)
    {
        var braces = 0;
        var brackets = 0;
        foreach (var character in content)
        {
            switch (character)
            {
                case '{':
                    braces++;
                    break;
                case '}':
                    braces--;
                    break;
                case '[':
                    brackets++;
                    break;
                case ']':
                    brackets--;
                    break;
            }

            if (braces < 0 || brackets < 0)
            {
                failures.Add(new ArtifactValidationFailure(
                    "syntax.balanced",
                    "Content contains unbalanced delimiters."));
                return;
            }
        }

        if (braces != 0 || brackets != 0)
        {
            failures.Add(new ArtifactValidationFailure(
                "syntax.balanced",
                "Content contains unbalanced delimiters."));
        }
    }
}
