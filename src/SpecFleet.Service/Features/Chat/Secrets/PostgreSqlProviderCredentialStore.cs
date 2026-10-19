using Npgsql;
using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Service.Features.Chat.Secrets;

public sealed record ProviderCredential(string Provider, string ProtectedValue, DateTimeOffset UpdatedAt);

/// <summary>
/// Persists provider credentials encrypted at rest. Reads return protected values only;
/// callers must resolve them through <see cref="ResolveAsync"/> which never exposes the
/// plaintext outside the server.
/// </summary>
public sealed class PostgreSqlProviderCredentialStore(
    string connectionString,
    ISecretProtector protector)
{
    public async Task<ServiceResult<bool>> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS specfleet_provider_credentials (
                provider TEXT PRIMARY KEY,
                protected_value TEXT NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
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
            return Unavailable<bool>();
        }
    }

    public async Task<ServiceResult<bool>> StoreAsync(
        string provider,
        string credential,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(credential))
        {
            return ServiceResult<bool>.FromError(
                new ServiceError(ServiceErrorCode.Validation, "A provider name and credential are required."));
        }

        const string sql = """
            INSERT INTO specfleet_provider_credentials (provider, protected_value, updated_at)
            VALUES (@provider, @protected_value, @updated_at)
            ON CONFLICT (provider) DO UPDATE
                SET protected_value = EXCLUDED.protected_value, updated_at = EXCLUDED.updated_at;
            """;

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("provider", provider);
            command.Parameters.AddWithValue("protected_value", protector.Protect(credential));
            command.Parameters.AddWithValue("updated_at", DateTimeOffset.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return ServiceResult<bool>.FromValue(true);
        }
        catch (NpgsqlException)
        {
            return Unavailable<bool>();
        }
    }

    /// <summary>
    /// Resolves the plaintext credential for server-side use. Never return this value to a client.
    /// </summary>
    public async Task<ServiceResult<string>> ResolveAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT protected_value
            FROM specfleet_provider_credentials
            WHERE provider = @provider;
            """;

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("provider", provider);
            var value = await command.ExecuteScalarAsync(cancellationToken);

            if (value is not string protectedValue)
            {
                return ServiceResult<string>.FromError(
                    new ServiceError(ServiceErrorCode.NotFound, "No credential is configured for the provider."));
            }

            return ServiceResult<string>.FromValue(protector.Unprotect(protectedValue));
        }
        catch (NpgsqlException)
        {
            return Unavailable<string>();
        }
    }

    private static ServiceResult<T> Unavailable<T>() where T : notnull =>
        ServiceResult<T>.FromError(
            new ServiceError(ServiceErrorCode.ProviderUnavailable, "The credential store is unavailable.", IsRetryable: true));
}
