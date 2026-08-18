using ChatAnalyzer.Core.Models;

namespace ChatAnalyzer.Core.Analysis;

public sealed record WithinPatternShift(
    RecurringPattern Pattern,
    Exchange EarlyRepresentative,
    Exchange LateRepresentative,
    double Shift,
    int SpanDays
);

public sealed class WithinPatternShiftDetector
{
    public IReadOnlyList<WithinPatternShift> Detect(
        IReadOnlyList<RecurringPattern> patterns,
        IReadOnlyDictionary<string, float[]> embeddings)
    {
        var results = new List<WithinPatternShift>();

        foreach (var pattern in patterns)
        {
            var members = pattern.Matches
                .Select(m => m.Exchange)
                .Append(pattern.Anchor)
                .Where(e =>
                    e.StartedAt is not null &&
                    embeddings.ContainsKey(e.Id))
                .OrderBy(e => e.StartedAt)
                .ToList();

            if (members.Count < 4)
                continue;

            var split = members.Count / 2;

            var early = members.Take(split).ToList();
            var late = members.Skip(split).ToList();

            if (early.Count < 2 || late.Count < 2)
                continue;

            var earlyCentroid = Centroid(
                early.Select(e => embeddings[e.Id]));

            var lateCentroid = Centroid(
                late.Select(e => embeddings[e.Id]));

            var similarity = Cosine(
                earlyCentroid,
                lateCentroid);

            var shift = 1.0 - similarity;

            var earlyRep = Representative(
                early,
                earlyCentroid,
                embeddings);

            var lateRep = Representative(
                late,
                lateCentroid,
                embeddings);

            var spanDays = (int)(
                members.Last().StartedAt!.Value -
                members.First().StartedAt!.Value
            ).TotalDays;

            results.Add(new WithinPatternShift(
                pattern,
                earlyRep,
                lateRep,
                shift,
                spanDays));
        }

        return results
            .OrderByDescending(x => x.Shift)
            .ThenByDescending(x => x.SpanDays)
            .Take(25)
            .ToList();
    }

    private static float[] Centroid(
        IEnumerable<float[]> vectors)
    {
        var list = vectors.ToList();

        if (list.Count == 0)
            return [];

        var dimensions = list[0].Length;
        var result = new float[dimensions];

        foreach (var vector in list)
        {
            for (var i = 0; i < dimensions; i++)
                result[i] += vector[i];
        }

        for (var i = 0; i < dimensions; i++)
            result[i] /= list.Count;

        return result;
    }

    private static Exchange Representative(
        IReadOnlyList<Exchange> exchanges,
        float[] centroid,
        IReadOnlyDictionary<string, float[]> embeddings)
    {
        return exchanges
            .OrderByDescending(e =>
                Cosine(centroid, embeddings[e.Id]))
            .First();
    }

    private static double Cosine(
        float[] a,
        float[] b)
    {
        var length = Math.Min(a.Length, b.Length);

        double dot = 0;
        double aa = 0;
        double bb = 0;

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
