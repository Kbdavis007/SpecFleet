using Npgsql;
using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Service.Features.Chat.Conversations;

public sealed class PostgreSqlConversationStore(string connectionString)
{
    public async Task<ServiceResult<bool>> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS specfleet_conversations (
                conversation_id UUID PRIMARY KEY,
                owner_id TEXT NOT NULL,
                title TEXT NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE TABLE IF NOT EXISTS specfleet_chat_messages (
                message_id UUID PRIMARY KEY,
                conversation_id UUID NOT NULL REFERENCES specfleet_conversations (conversation_id) ON DELETE CASCADE,
                role TEXT NOT NULL CHECK (role IN ('User', 'Assistant')),
                content TEXT NOT NULL,
                status TEXT NOT NULL CHECK (status IN ('Pending', 'Streaming', 'Completed', 'Failed', 'Cancelled')),
                error TEXT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS ix_specfleet_chat_messages_conversation
                ON specfleet_chat_messages (conversation_id, created_at, message_id);
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

    public async Task<ServiceResult<Conversation>> StartConversationAsync(
        string ownerId,
        string title,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return ServiceResult<Conversation>.FromError(
                new ServiceError(ServiceErrorCode.Unauthorized, "An authenticated user is required to start a conversation."));
        }

        var conversation = new Conversation(
            Guid.NewGuid(),
            ownerId,
            string.IsNullOrWhiteSpace(title) ? "New conversation" : title,
            DateTimeOffset.UtcNow);

        const string sql = """
            INSERT INTO specfleet_conversations (conversation_id, owner_id, title, created_at)
            VALUES (@conversation_id, @owner_id, @title, @created_at);
            """;

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("conversation_id", conversation.ConversationId);
            command.Parameters.AddWithValue("owner_id", conversation.OwnerId);
            command.Parameters.AddWithValue("title", conversation.Title);
            command.Parameters.AddWithValue("created_at", conversation.CreatedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return ServiceResult<Conversation>.FromValue(conversation);
        }
        catch (NpgsqlException)
        {
            return Unavailable<Conversation>();
        }
    }

    public async Task<ServiceResult<Conversation>> GetConversationAsync(
        Guid conversationId,
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT conversation_id, owner_id, title, created_at
            FROM specfleet_conversations
            WHERE conversation_id = @conversation_id;
            """;

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("conversation_id", conversationId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return NotFound<Conversation>("The conversation was not found.");
            }

            var owner = reader.GetString(1);
            if (!string.Equals(owner, ownerId, StringComparison.Ordinal))
            {
                return ServiceResult<Conversation>.FromError(
                    new ServiceError(ServiceErrorCode.Unauthorized, "The conversation does not belong to the authenticated user."));
            }

            return ServiceResult<Conversation>.FromValue(
                new Conversation(
                    reader.GetGuid(0),
                    owner,
                    reader.GetString(2),
                    reader.GetFieldValue<DateTimeOffset>(3)));
        }
        catch (NpgsqlException)
        {
            return Unavailable<Conversation>();
        }
    }

    public async Task<ServiceResult<ChatMessage>> AppendUserMessageAsync(
        Guid conversationId,
        string ownerId,
        string content,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return ServiceResult<ChatMessage>.FromError(
                new ServiceError(ServiceErrorCode.Validation, "A non-empty prompt is required."));
        }

        var conversation = await GetConversationAsync(conversationId, ownerId, cancellationToken);
        if (conversation is ServiceResult<Conversation>.Failure failure)
        {
            return ServiceResult<ChatMessage>.FromError(failure.Error);
        }

        return await InsertMessageAsync(
            conversationId,
            ChatRole.User,
            content,
            ChatMessageStatus.Completed,
            cancellationToken);
    }

    public async Task<ServiceResult<ChatMessage>> BeginAssistantMessageAsync(
        Guid conversationId,
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await GetConversationAsync(conversationId, ownerId, cancellationToken);
        if (conversation is ServiceResult<Conversation>.Failure failure)
        {
            return ServiceResult<ChatMessage>.FromError(failure.Error);
        }

        return await InsertMessageAsync(
            conversationId,
            ChatRole.Assistant,
            string.Empty,
            ChatMessageStatus.Streaming,
            cancellationToken);
    }

    public async Task<ServiceResult<ChatMessage>> CompleteAssistantMessageAsync(
        Guid messageId,
        string content,
        CancellationToken cancellationToken = default) =>
        await UpdateMessageAsync(
            messageId,
            content,
            ChatMessageStatus.Completed,
            null,
            cancellationToken);

    public async Task<ServiceResult<ChatMessage>> FailAssistantMessageAsync(
        Guid messageId,
        string content,
        string error,
        CancellationToken cancellationToken = default) =>
        await UpdateMessageAsync(
            messageId,
            content,
            ChatMessageStatus.Failed,
            error,
            cancellationToken);

    public async Task<ServiceResult<ChatMessage>> CancelAssistantMessageAsync(
        Guid messageId,
        string content,
        CancellationToken cancellationToken = default) =>
        await UpdateMessageAsync(
            messageId,
            content,
            ChatMessageStatus.Cancelled,
            "The response was cancelled by the user.",
            cancellationToken);

    public async Task<ServiceResult<IReadOnlyList<ChatMessage>>> ListMessagesAsync(
        Guid conversationId,
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await GetConversationAsync(conversationId, ownerId, cancellationToken);
        if (conversation is ServiceResult<Conversation>.Failure failure)
        {
            return ServiceResult<IReadOnlyList<ChatMessage>>.FromError(failure.Error);
        }

        const string sql = """
            SELECT message_id, conversation_id, role, content, status, created_at, updated_at, error
            FROM specfleet_chat_messages
            WHERE conversation_id = @conversation_id
            ORDER BY created_at, message_id;
            """;

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("conversation_id", conversationId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var messages = new List<ChatMessage>();
            while (await reader.ReadAsync(cancellationToken))
            {
                messages.Add(ReadMessage(reader));
            }

            return ServiceResult<IReadOnlyList<ChatMessage>>.FromValue(messages);
        }
        catch (NpgsqlException)
        {
            return Unavailable<IReadOnlyList<ChatMessage>>();
        }
    }

    private async Task<ServiceResult<ChatMessage>> InsertMessageAsync(
        Guid conversationId,
        ChatRole role,
        string content,
        ChatMessageStatus status,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var message = new ChatMessage(
            Guid.NewGuid(),
            conversationId,
            role,
            content,
            status,
            now,
            now,
            null);

        const string sql = """
            INSERT INTO specfleet_chat_messages (
                message_id, conversation_id, role, content, status, created_at, updated_at)
            VALUES (
                @message_id, @conversation_id, @role, @content, @status, @created_at, @updated_at);
            """;

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("message_id", message.MessageId);
            command.Parameters.AddWithValue("conversation_id", message.ConversationId);
            command.Parameters.AddWithValue("role", role.ToString());
            command.Parameters.AddWithValue("content", message.Content);
            command.Parameters.AddWithValue("status", status.ToString());
            command.Parameters.AddWithValue("created_at", message.CreatedAt);
            command.Parameters.AddWithValue("updated_at", message.UpdatedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return ServiceResult<ChatMessage>.FromValue(message);
        }
        catch (NpgsqlException)
        {
            return Unavailable<ChatMessage>();
        }
    }

    private async Task<ServiceResult<ChatMessage>> UpdateMessageAsync(
        Guid messageId,
        string content,
        ChatMessageStatus status,
        string? error,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE specfleet_chat_messages
            SET content = @content, status = @status, error = @error, updated_at = @updated_at
            WHERE message_id = @message_id
            RETURNING message_id, conversation_id, role, content, status, created_at, updated_at, error;
            """;

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("message_id", messageId);
            command.Parameters.AddWithValue("content", content);
            command.Parameters.AddWithValue("status", status.ToString());
            command.Parameters.AddWithValue("error", (object?)error ?? DBNull.Value);
            command.Parameters.AddWithValue("updated_at", DateTimeOffset.UtcNow);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            return await reader.ReadAsync(cancellationToken)
                ? ServiceResult<ChatMessage>.FromValue(ReadMessage(reader))
                : NotFound<ChatMessage>("The message was not found.");
        }
        catch (NpgsqlException)
        {
            return Unavailable<ChatMessage>();
        }
    }

    private static ChatMessage ReadMessage(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            Enum.Parse<ChatRole>(reader.GetString(2)),
            reader.GetString(3),
            Enum.Parse<ChatMessageStatus>(reader.GetString(4)),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.IsDBNull(7) ? null : reader.GetString(7));

    private static ServiceResult<T> Unavailable<T>() where T : notnull =>
        ServiceResult<T>.FromError(
            new ServiceError(ServiceErrorCode.ProviderUnavailable, "The conversation store is unavailable.", IsRetryable: true));

    private static ServiceResult<T> NotFound<T>(string message) where T : notnull =>
        ServiceResult<T>.FromError(new ServiceError(ServiceErrorCode.NotFound, message));
}
