using ChatAnalyzer.Core.Models;

namespace ChatAnalyzer.Core.Analysis;

public sealed record PatternChange(
    RecurringPattern Pattern,
    Exchange Earliest,
    Exchange Latest,
    double SemanticShift,
    int DaysBetween,
    string EarliestText,
    string LatestText
);

public sealed class PatternChangeDetector
{
    public IReadOnlyList<PatternChange> Detect(
        IReadOnlyList<RecurringPattern> patterns,
        IReadOnlyDictionary<string, float[]> embeddings)
    {
        var changes = new List<PatternChange>();

        foreach (var pattern in patterns)
        {
            var members = pattern.Matches
                .Select(m => m.Exchange)
                .Append(pattern.Anchor)
                .Where(e => e.StartedAt is not null && embeddings.ContainsKey(e.Id))
                .OrderBy(e => e.StartedAt)
                .ToList();

            if (members.Count < 3)
                continue;

            var earliest = members.First();
            var latest = members.Last();

            var days = (int)(latest.StartedAt!.Value - earliest.StartedAt!.Value).TotalDays;

            if (days < 7)
                continue;

            var similarity = Cosine(
                embeddings[earliest.Id],
                embeddings[latest.Id]);

            var shift = 1.0 - similarity;

            changes.Add(new PatternChange(
                pattern,
                earliest,
                latest,
                shift,
                days,
                earliest.UserText,
                latest.UserText));
        }

        return changes
            .OrderByDescending(c => c.SemanticShift)
            .ThenByDescending(c => c.DaysBetween)
            .Take(25)
            .ToList();
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0;
        double aa = 0;
        double bb = 0;

        var length = Math.Min(a.Length, b.Length);

        for (var i = 0; i < length; i++)
        {
            dot += a[i] * b[i];
            aa += a[i] * a[i];
            bb += b[i] * b[i];
        }

        if (aa == 0 || bb == 0)
            return 0;

        return dot / (Math.Sqrt(aa) * Math.Sqrt(bb));
    }
}
