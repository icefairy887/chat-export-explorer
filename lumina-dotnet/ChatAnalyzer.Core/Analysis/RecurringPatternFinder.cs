using ChatAnalyzer.Core.Models;

namespace ChatAnalyzer.Core.Analysis;

public sealed class RecurringPatternFinder
{
    public IReadOnlyList<RecurringPattern> Find(
        IReadOnlyList<Exchange> exchanges,
        IReadOnlyDictionary<string, float[]> embeddings,
        int maxAnchors = 500,
        int maxMatches = 8,
        double minimumSimilarity = 0.60,
        int minimumDaysApart = 2)
    {
        var eligible = exchanges
            .Where(e =>
                embeddings.ContainsKey(e.Id) &&
                !string.IsNullOrWhiteSpace(e.UserText) &&
                e.UserText.Length >= 40 &&
                e.StartedAt is not null)
            .OrderBy(e => e.StartedAt)
            .ToList();

        if (eligible.Count == 0)
            return [];

        var anchors = SelectAnchors(eligible, maxAnchors);
        var patterns = new List<RecurringPattern>();

        foreach (var anchor in anchors)
        {
            var anchorVector = embeddings[anchor.Id];

            var matches = eligible
                .Where(candidate =>
                    candidate.Id != anchor.Id &&
                    candidate.ConversationId != anchor.ConversationId &&
                    candidate.StartedAt is not null &&
                    Math.Abs(
                        (candidate.StartedAt.Value -
                         anchor.StartedAt!.Value).TotalDays)
                        >= minimumDaysApart)
                .Select(candidate => new PatternMatch(
                    candidate,
                    Cosine(
                        anchorVector,
                        embeddings[candidate.Id])))
                .Where(m => m.Similarity >= minimumSimilarity)
                .OrderByDescending(m => m.Similarity)
                .Take(maxMatches)
                .ToList();

            if (matches.Count < 2)
                continue;

            var allExchanges = matches
                .Select(m => m.Exchange)
                .Append(anchor)
                .ToList();

            var distinctConversations = allExchanges
                .Select(e => e.ConversationId)
                .Distinct()
                .Count();

            var dates = allExchanges
                .Where(e => e.StartedAt is not null)
                .Select(e => e.StartedAt!.Value)
                .OrderBy(d => d)
                .ToList();

            var spanDays = dates.Count > 1
                ? (int)(dates[^1] - dates[0]).TotalDays
                : 0;

            var averageSimilarity =
                matches.Average(m => m.Similarity);

            var score =
                averageSimilarity *
                Math.Log2(distinctConversations + 1) *
                Math.Log2(Math.Max(spanDays, 1) + 1);

            patterns.Add(new RecurringPattern(
                anchor,
                matches,
                score,
                distinctConversations,
                spanDays));
        }

        return patterns
            .OrderByDescending(p => p.Score)
            .Take(50)
            .ToList();
    }

    private static IReadOnlyList<Exchange> SelectAnchors(
        IReadOnlyList<Exchange> exchanges,
        int maxAnchors)
    {
        if (exchanges.Count <= maxAnchors)
            return exchanges;

        var result = new List<Exchange>(maxAnchors);
        var step = (double)exchanges.Count / maxAnchors;

        for (var i = 0; i < maxAnchors; i++)
        {
            var index = Math.Min(
                (int)Math.Floor(i * step),
                exchanges.Count - 1);

            result.Add(exchanges[index]);
        }

        return result;
    }

    private static double Cosine(float[] a, float[] b)
    {
        var length = Math.Min(a.Length, b.Length);

        double dot = 0;
        double normA = 0;
        double normB = 0;

        for (var i = 0; i < length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0)
            return 0;

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
