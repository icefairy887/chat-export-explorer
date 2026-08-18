namespace ChatAnalyzer.Core.Models;

public sealed record ChatMessage(
    string Id,
    string ConversationId,
    string Role,
    string Text,
    DateTimeOffset? CreatedAt
);
