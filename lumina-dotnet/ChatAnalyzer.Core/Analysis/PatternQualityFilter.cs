namespace ChatAnalyzer.Core.Analysis;

public sealed class PatternQualityFilter
{
    private static readonly string[] GenericPhrases =
    [
        "what are you talking about",
        "i don't know",
        "i dont know",
        "okay",
        "ok",
        "what do you mean",
        "tell me",
        "try again"
    ];

    public IReadOnlyList<RecurringPattern> Filter(
        IReadOnlyList<RecurringPattern> patterns)
    {
        return patterns
            .Where(IsUseful)
            .ToList();
    }

    private static bool IsUseful(RecurringPattern pattern)
    {
        var texts = pattern.Matches
            .Select(m => m.Exchange.UserText)
            .Append(pattern.Anchor.UserText)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        if (texts.Count < 3)
            return false;

        var averageLength = texts.Average(t => t.Length);

        if (averageLength < 100)
            return false;

        var genericCount = texts.Count(IsGeneric);

        if (genericCount >= Math.Ceiling(texts.Count * 0.5))
            return false;

        var distinctWords = texts
            .SelectMany(Tokenize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (distinctWords < 35)
            return false;

        return true;
    }

    private static bool IsGeneric(string text)
    {
        var normalized = text.Trim().ToLowerInvariant();

        if (normalized.Length < 40)
            return true;

        return GenericPhrases.Any(p =>
            normalized == p ||
            normalized.StartsWith(p + "?") ||
            normalized.StartsWith(p + "."));
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        return text
            .Split(
                [' ', '\r', '\n', '\t', '.', ',', '!', '?', ';', ':', '"', '(', ')'],
                StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.Length >= 3);
    }
}
