using System.Text.Json;
using ChatAnalyzer.Core.Models;

namespace ChatAnalyzer.Core.Importing;

public sealed class ChatGptExportImporter : IChatImporter
{
    public async Task<IReadOnlyList<Conversation>> ImportAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        using var document = await JsonDocument.ParseAsync(
            source,
            cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Expected conversations.json to contain a JSON array.");

        var conversations = new List<Conversation>();

        foreach (var item in document.RootElement.EnumerateArray())
        {
            var id = GetString(item, "id") ?? Guid.NewGuid().ToString();
            var title = GetString(item, "title") ?? "Untitled";

            var createdAt = ParseUnixTimestamp(item, "create_time");

            var messages = new List<ChatMessage>();

            if (item.TryGetProperty("mapping", out var mapping) &&
                mapping.ValueKind == JsonValueKind.Object)
            {
                foreach (var node in mapping.EnumerateObject())
                {
                    if (!node.Value.TryGetProperty("message", out var message) ||
                        message.ValueKind == JsonValueKind.Null)
                        continue;

                    var parsed = ParseMessage(message, id);

                    if (parsed is not null)
                        messages.Add(parsed);
                }
            }

            messages = messages
                .OrderBy(m => m.CreatedAt)
                .ToList();

            conversations.Add(new Conversation(
                id,
                title,
                createdAt,
                messages));
        }

        return conversations;
    }

    private static ChatMessage? ParseMessage(
        JsonElement message,
        string conversationId)
    {
        var id = GetString(message, "id") ?? Guid.NewGuid().ToString();

        var role = "";

        if (message.TryGetProperty("author", out var author))
            role = GetString(author, "role") ?? "";

        if (string.IsNullOrWhiteSpace(role))
            return null;

        var text = ExtractText(message);

        if (string.IsNullOrWhiteSpace(text))
            return null;

        var createdAt = ParseUnixTimestamp(message, "create_time");

        return new ChatMessage(
            id,
            conversationId,
            role,
            text,
            createdAt);
    }

    private static string ExtractText(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
            return "";

        if (!content.TryGetProperty("parts", out var parts) ||
            parts.ValueKind != JsonValueKind.Array)
            return "";

        var values = new List<string>();

        foreach (var part in parts.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.String)
            {
                var value = part.GetString();

                if (!string.IsNullOrWhiteSpace(value))
                    values.Add(value);
            }
        }

        return string.Join(Environment.NewLine, values);
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return null;

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

   private static DateTimeOffset? ParseUnixTimestamp(
    JsonElement element,
    string property)
{
    if (!element.TryGetProperty(property, out var value))
        return null;

    if (value.ValueKind != JsonValueKind.Number)
        return null;

    if (!value.TryGetDouble(out var timestamp))
        return null;

    try
    {
        // Seconds:      ~1,700,000,000
        // Milliseconds: ~1,700,000,000,000
        // Microseconds: ~1,700,000,000,000,000
        if (timestamp > 100_000_000_000_000)
            timestamp /= 1000;

        if (timestamp > 100_000_000_000)
            return DateTimeOffset.FromUnixTimeMilliseconds((long)timestamp);

        return DateTimeOffset.FromUnixTimeSeconds((long)timestamp);
    }
    catch (ArgumentOutOfRangeException)
    {
        return null;
    }
}
}
