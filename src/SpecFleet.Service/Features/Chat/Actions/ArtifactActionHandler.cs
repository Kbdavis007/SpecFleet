using System.Security.Cryptography;
using System.Text;
using SpecFleet.Service.Features.Chat.Artifacts;
using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Service.Features.Chat.Actions;

public enum ArtifactActionDecision
{
    Confirmed,
    Declined
}

public sealed record ArtifactActionRequest(
    string ArtifactId,
    string OwnerId,
    ArtifactType ArtifactType,
    string Target,
    string Content,
    string PreviewDigest,
    ArtifactActionDecision Decision);

public sealed record ArtifactActionOutcome(
    ArtifactActionDecision Decision,
    string ArtifactId,
    string Target,
    bool ExternalChangeSubmitted);

public interface IArtifactActionSink
{
    Task<ServiceResult<bool>> SubmitAsync(
        ArtifactActionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Applies a reviewed artifact only when the user explicitly confirms it and the confirmed
/// content still matches the immutable preview digest that was displayed. Declines submit
/// nothing. No external mutation happens without an explicit confirmation.
/// </summary>
public sealed class ArtifactActionHandler(IArtifactActionSink sink)
{
    public async Task<ServiceResult<ArtifactActionOutcome>> ApplyAsync(
        ArtifactActionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Validation("An artifact action request is required.");
        }

        if (string.IsNullOrWhiteSpace(request.OwnerId))
        {
            return ServiceResult<ArtifactActionOutcome>.FromError(
                new ServiceError(ServiceErrorCode.Unauthorized, "An authenticated user is required to apply an artifact."));
        }

        if (request.Decision == ArtifactActionDecision.Declined)
        {
            return ServiceResult<ArtifactActionOutcome>.FromValue(
                new ArtifactActionOutcome(
                    ArtifactActionDecision.Declined,
                    request.ArtifactId,
                    request.Target,
                    ExternalChangeSubmitted: false));
        }

        if (string.IsNullOrWhiteSpace(request.ArtifactId) ||
            string.IsNullOrWhiteSpace(request.Target) ||
            string.IsNullOrWhiteSpace(request.PreviewDigest))
        {
            return Validation("A reviewed artifact, target, and preview digest are required.");
        }

        if (!DigestMatches(request.Content, request.PreviewDigest))
        {
            return ServiceResult<ArtifactActionOutcome>.FromError(
                new ServiceError(ServiceErrorCode.Conflict, "The artifact no longer matches the approved preview."));
        }

        var submission = await sink.SubmitAsync(request, cancellationToken);
        if (submission is ServiceResult<bool>.Failure failure)
        {
            return ServiceResult<ArtifactActionOutcome>.FromError(failure.Error);
        }

        return ServiceResult<ArtifactActionOutcome>.FromValue(
            new ArtifactActionOutcome(
                ArtifactActionDecision.Confirmed,
                request.ArtifactId,
                request.Target,
                ExternalChangeSubmitted: true));
    }

    private static bool DigestMatches(string content, string expectedDigest)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content ?? string.Empty)));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(digest),
            Encoding.ASCII.GetBytes(expectedDigest.ToUpperInvariant()));
    }

    private static ServiceResult<ArtifactActionOutcome> Validation(string message) =>
        ServiceResult<ArtifactActionOutcome>.FromError(
            new ServiceError(ServiceErrorCode.Validation, message));
}
