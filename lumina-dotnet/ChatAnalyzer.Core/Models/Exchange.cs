namespace ChatAnalyzer.Core.Models;

public sealed record Exchange(
    string Id,
    string ConversationId,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    IReadOnlyList<ChatMessage> UserMessages,
    IReadOnlyList<ChatMessage> AssistantMessages
)
{
    public string UserText =>
        string.Join(Environment.NewLine, UserMessages.Select(m => m.Text));

    public string AssistantText =>
        string.Join(Environment.NewLine, AssistantMessages.Select(m => m.Text));
}
