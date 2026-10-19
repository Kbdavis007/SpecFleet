namespace SpecFleet.Service.Features.Chat.Conversations;

public sealed record ChatMessage(
    Guid MessageId,
    Guid ConversationId,
    ChatRole Role,
    string Content,
    ChatMessageStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Error);
