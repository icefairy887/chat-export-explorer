using ChatAnalyzer.Core.Models;

namespace ChatAnalyzer.Core.Analysis;

public sealed record TemporalDrift(
    DateTimeOffset EarlyStart,
    DateTimeOffset EarlyEnd,
    DateTimeOffset LateStart,
    DateTimeOffset LateEnd,
    double Drift,
    IReadOnlyList<Exchange> EarlyContributors,
    IReadOnlyList<Exchange> LateContributors
);

public sealed class TemporalDriftDetector
{
    public IReadOnlyList<TemporalDrift> Detect(
        IReadOnlyList<Exchange> exchanges,
        IReadOnlyDictionary<string, float[]> embeddings,
        int windowDays = 14)
    {
        var eligible = exchanges
            .Where(e => e.StartedAt is not null &&
                        embeddings.ContainsKey(e.Id) &&
                        !string.IsNullOrWhiteSpace(e.UserText))
            .OrderBy(e => e.StartedAt)
            .ToList();

        if (eligible.Count == 0)
            return [];

        var first = eligible.First().StartedAt!.Value.Date;
        var last = eligible.Last().StartedAt!.Value.Date;

        var windows = new List<(DateTimeOffset Start, DateTimeOffset End, List<Exchange> Items, float[] Centroid)>();

        for (var start = first; start <= last; start = start.AddDays(windowDays))
        {
            var end = start.AddDays(windowDays);

            var items = eligible
                .Where(e => e.StartedAt!.Value >= start &&
                            e.StartedAt!.Value < end)
                .ToList();

            if (items.Count < 10)
                continue;

            var centroid = Centroid(items.Select(e => embeddings[e.Id]));

            windows.Add((start, end, items, centroid));
        }

        var results = new List<TemporalDrift>();

        for (var i = 1; i < windows.Count; i++)
        {
            var early = windows[i - 1];
            var late = windows[i];

            var drift = 1.0 - Cosine(early.Centroid, late.Centroid);

            var earlyRanked = early.Items
                .OrderByDescending(e =>
                    Cosine(embeddings[e.Id], early.Centroid) -
                    Cosine(embeddings[e.Id], late.Centroid))
                .ToList();

            var lateRanked = late.Items
                .OrderByDescending(e =>
                    Cosine(embeddings[e.Id], late.Centroid) -
                    Cosine(embeddings[e.Id], early.Centroid))
                .ToList();

            var earlyContributors = SelectIndependent(
                earlyRanked,
                5);

            var lateContributors = SelectIndependent(
                lateRanked,
                5);

            results.Add(new TemporalDrift(
                early.Start,
                early.End,
                late.Start,
                late.End,
                drift,
                earlyContributors,
                lateContributors));
        }

        return results
            .OrderByDescending(x => x.Drift)
            .Take(10)
            .ToList();
    }


    private static List<Exchange> SelectIndependent(
        IReadOnlyList<Exchange> ranked,
        int count)
    {
        var selected = ranked
            .GroupBy(e => e.ConversationId)
            .Select(g => g.First())
            .Take(count)
            .ToList();

        if (selected.Count >= count)
            return selected;

        foreach (var exchange in ranked)
        {
            if (selected.Count >= count)
                break;

            if (selected.Any(x => x.Id == exchange.Id))
                continue;

            selected.Add(exchange);
        }

        return selected;
    }
    private static float[] Centroid(IEnumerable<float[]> vectors)
    {
        var list = vectors.ToList();
        if (list.Count == 0) return [];

        var result = new float[list[0].Length];

        foreach (var vector in list)
            for (var i = 0; i < result.Length; i++)
                result[i] += vector[i];

        for (var i = 0; i < result.Length; i++)
            result[i] /= list.Count;

        return result;
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, aa = 0, bb = 0;

        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            dot += a[i] * b[i];
            aa += a[i] * a[i];
            bb += b[i] * b[i];
        }

        return aa == 0 || bb == 0
            ? 0
            : dot / (Math.Sqrt(aa) * Math.Sqrt(bb));
    }
}

