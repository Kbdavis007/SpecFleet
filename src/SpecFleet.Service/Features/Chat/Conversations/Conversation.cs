namespace SpecFleet.Service.Features.Chat.Conversations;

public sealed record Conversation(
    Guid ConversationId,
    string OwnerId,
    string Title,
    DateTimeOffset CreatedAt);
