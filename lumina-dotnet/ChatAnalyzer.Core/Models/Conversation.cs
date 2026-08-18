namespace ChatAnalyzer.Core.Models;

public sealed record Conversation(
    string Id,
    string Title,
    DateTimeOffset? CreatedAt,
    IReadOnlyList<ChatMessage> Messages
);
