namespace SpecFleet.Service.Features.Chat.Artifacts;

public sealed record ArtifactValidationFailure(string Rule, string Message);

public static class ArtifactCatalog
{
    private static readonly Dictionary<ArtifactType, ArtifactTemplate> Templates = new()
    {
        [ArtifactType.Code] = new("src/generated/Sample.cs", "csharp", "cs"),
        [ArtifactType.Documentation] = new("docs/generated.md", "markdown", "md"),
        [ArtifactType.Worker] = new("src/SpecFleet.Workers/Consumers/GeneratedWorkerConsumer.cs", "csharp", "cs"),
        [ArtifactType.TerraformModule] = new("infra/generated/main.tf", "hcl", "tf"),
        [ArtifactType.CloudflareTunnelConfig] = new("infra/generated/cloudflare-tunnel.yml", "yaml", "yml"),
        [ArtifactType.RabbitMqConfig] = new("infra/generated/rabbitmq.json", "json", "json"),
        [ArtifactType.OpenSpecProposal] = new("openspec/changes/generated/proposal.md", "markdown", "md")
    };

    public static bool TryGetTemplate(ArtifactType artifactType, out ArtifactTemplate template) =>
        Templates.TryGetValue(artifactType, out template!);

    public static IReadOnlyCollection<ArtifactType> SupportedTypes => Templates.Keys;
}

public sealed record ArtifactTemplate(string ProposedPath, string Language, string Extension);
