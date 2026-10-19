using Npgsql;
using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Service.Features.Chat.Actions;

/// <summary>
/// Durable sink for explicitly confirmed artifact actions. It records the reviewed artifact
/// and confirmed target so the dispatched action is auditable and retryable. The GitHub
/// transport is a separate adapter; this sink never writes to a default branch itself.
/// </summary>
public sealed class PostgreSqlArtifactActionSink(string connectionString) : IArtifactActionSink
{
    public async Task<ServiceResult<bool>> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS specfleet_artifact_actions (
                action_id UUID PRIMARY KEY,
                artifact_id TEXT NOT NULL,
                artifact_type TEXT NOT NULL,
                target TEXT NOT NULL,
                content_digest TEXT NOT NULL,
                approved_by TEXT NOT NULL,
                submitted_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            """;

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return ServiceResult<bool>.FromValue(true);
        }
        catch (NpgsqlException)
        {
            return ServiceResult<bool>.FromError(
                new ServiceError(ServiceErrorCode.QueueUnavailable, "The action store is unavailable.", IsRetryable: true));
        }
    }

    public async Task<ServiceResult<bool>> SubmitAsync(
        ArtifactActionRequest request,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO specfleet_artifact_actions (
                action_id, artifact_id, artifact_type, target, content_digest, approved_by)
            VALUES (
                @action_id, @artifact_id, @artifact_type, @target, @content_digest, @approved_by);
            """;

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("action_id", Guid.NewGuid());
            command.Parameters.AddWithValue("artifact_id", request.ArtifactId);
            command.Parameters.AddWithValue("artifact_type", request.ArtifactType.ToString());
            command.Parameters.AddWithValue("target", request.Target);
            command.Parameters.AddWithValue("content_digest", request.PreviewDigest.ToUpperInvariant());
            command.Parameters.AddWithValue("approved_by", request.OwnerId);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return ServiceResult<bool>.FromValue(true);
        }
        catch (NpgsqlException)
        {
            return ServiceResult<bool>.FromError(
                new ServiceError(ServiceErrorCode.QueueUnavailable, "The action store is unavailable.", IsRetryable: true));
        }
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM specfleet_artifact_actions;", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }
}
