using ChatAnalyzer.Core.Models;

namespace ChatAnalyzer.Core.Processing;

public sealed class ExchangeBuilder
{
    public IReadOnlyList<Exchange> Build(IEnumerable<Conversation> conversations)
    {
        var exchanges = new List<Exchange>();

        foreach (var conversation in conversations)
        {
            var messages = conversation.Messages
                .Where(m =>
                    string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.CreatedAt)
                .ToList();

            var userMessages = new List<ChatMessage>();
            var assistantMessages = new List<ChatMessage>();

            foreach (var message in messages)
            {
                if (string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
                {
                    if (userMessages.Count > 0 && assistantMessages.Count > 0)
                    {
                        exchanges.Add(CreateExchange(
                            conversation.Id,
                            userMessages,
                            assistantMessages));

                        userMessages = [];
                        assistantMessages = [];
                    }

                    userMessages.Add(message);
                }
                else if (userMessages.Count > 0)
                {
                    assistantMessages.Add(message);
                }
            }

            if (userMessages.Count > 0)
            {
                exchanges.Add(CreateExchange(
                    conversation.Id,
                    userMessages,
                    assistantMessages));
            }
        }

        return exchanges;
    }

    private static Exchange CreateExchange(
        string conversationId,
        IReadOnlyList<ChatMessage> userMessages,
        IReadOnlyList<ChatMessage> assistantMessages)
    {
        var all = userMessages.Concat(assistantMessages).ToList();
        var dated = all.Where(m => m.CreatedAt is not null).ToList();

        return new Exchange(
            userMessages[0].Id,
            conversationId,
            dated.MinBy(m => m.CreatedAt)?.CreatedAt,
            dated.MaxBy(m => m.CreatedAt)?.CreatedAt,
            userMessages.ToList(),
            assistantMessages.ToList());
    }
}
