using Npgsql;
using SpecFleet.Service.Features.Chat.Conversations;
using SpecFleet.Service.Features.Chat.Secrets;
using SpecFleet.Service.Features.Chat.Actions;
using SpecFleet.Service.Shared.Results;
using Testcontainers.PostgreSql;

namespace SpecFleet.Test.Support;

/// <summary>
/// Process-wide real PostgreSQL container shared by Gherkin acceptance bindings. Reqnroll
/// step classes are instantiated per scenario, so the container is held in a lazily-started
/// singleton and each scenario truncates the Service-owned tables it uses.
/// </summary>
public static class SharedPostgreSql
{
    private static readonly Lazy<Task<State>> LazyState = new(StartAsync);

    public static Task<State> InstanceAsync() => LazyState.Value;

    private static async Task<State> StartAsync()
    {
        var container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("specfleet_test")
            .WithUsername("postgres")
            .WithPassword("specfleet_test")
            .Build();

        await container.StartAsync();
        var connectionString = container.GetConnectionString();

        var conversations = new PostgreSqlConversationStore(connectionString);
        var credentials = new PostgreSqlProviderCredentialStore(
            connectionString,
            new AesGcmSecretProtector(Convert.ToBase64String(new byte[32])));
        var actions = new PostgreSqlArtifactActionSink(connectionString);

        RequireSuccess(await conversations.InitializeAsync(), "conversation");
        RequireSuccess(await credentials.InitializeAsync(), "credential");
        RequireSuccess(await actions.InitializeAsync(), "artifact action");

        return new State(connectionString, conversations, credentials, actions);
    }

    private static void RequireSuccess(ServiceResult<bool> result, string name)
    {
        if (result is not ServiceResult<bool>.Success)
        {
            throw new InvalidOperationException($"Failed to initialize the {name} schema.");
        }
    }

    public sealed record State(
        string ConnectionString,
        PostgreSqlConversationStore Conversations,
        PostgreSqlProviderCredentialStore Credentials,
        PostgreSqlArtifactActionSink Actions)
    {
        public async Task ResetAsync()
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "TRUNCATE specfleet_chat_messages, specfleet_conversations, specfleet_provider_credentials, specfleet_artifact_actions;",
                connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
