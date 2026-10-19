namespace SpecFleet.Service.Features.Chat.Artifacts;

/// <summary>
/// A generated artifact presented for review. <see cref="PreviewDigest"/> is the immutable
/// binding used by later approval so a changed preview cannot be applied.
/// </summary>
public sealed record ArtifactPreview(
    string ArtifactId,
    ArtifactType ArtifactType,
    string ProposedPath,
    string Content,
    string? Language,
    string PreviewDigest,
    bool IsValid,
    IReadOnlyList<ArtifactValidationFailure> ValidationFailures);
