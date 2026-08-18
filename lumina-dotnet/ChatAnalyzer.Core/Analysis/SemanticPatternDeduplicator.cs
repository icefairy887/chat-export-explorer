namespace ChatAnalyzer.Core.Analysis;

public sealed class SemanticPatternDeduplicator
{
    public IReadOnlyList<RecurringPattern> Deduplicate(
        IReadOnlyList<RecurringPattern> patterns,
        IReadOnlyDictionary<string, float[]> embeddings,
        double similarityThreshold = 0.78)
    {
        var kept = new List<(RecurringPattern Pattern, float[] Centroid)>();

        foreach (var pattern in patterns.OrderByDescending(p => p.Score))
        {
            var centroid = GetCentroid(pattern, embeddings);

            if (centroid.Length == 0)
                continue;

            var duplicate = kept.Any(existing =>
                Cosine(centroid, existing.Centroid) >= similarityThreshold);

            if (!duplicate)
                kept.Add((pattern, centroid));
        }

        return kept
            .Select(x => x.Pattern)
            .ToList();
    }

    private static float[] GetCentroid(
        RecurringPattern pattern,
        IReadOnlyDictionary<string, float[]> embeddings)
    {
        var ids = pattern.Matches
            .Select(m => m.Exchange.Id)
            .Append(pattern.Anchor.Id)
            .Distinct();

        var vectors = ids
            .Where(embeddings.ContainsKey)
            .Select(id => embeddings[id])
            .ToList();

        if (vectors.Count == 0)
            return [];

        var result = new float[vectors[0].Length];

        foreach (var vector in vectors)
            for (var i = 0; i < result.Length; i++)
                result[i] += vector[i];

        for (var i = 0; i < result.Length; i++)
            result[i] /= vectors.Count;

        return result;
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

        return aa == 0 || bb == 0
            ? 0
            : dot / (Math.Sqrt(aa) * Math.Sqrt(bb));
    }
}
