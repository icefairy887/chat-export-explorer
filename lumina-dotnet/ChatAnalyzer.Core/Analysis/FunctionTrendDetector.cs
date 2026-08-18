using ChatAnalyzer.Core.Embeddings;
using ChatAnalyzer.Core.Models;

namespace ChatAnalyzer.Core.Analysis;

public sealed record FunctionTrend(
    string Function,
    double FirstScore,
    double LastScore,
    double Change,
    double Slope
);

public sealed class FunctionTrendDetector
{
    private readonly IEmbeddingService _embeddingService;

    private static readonly string[] Functions =
    [
        "narrating something that happened",
        "describing a current situation",
        "expressing an emotional reaction",
        "expressing a preference or desire",
        "questioning another person's motives",
        "trying to understand why something happened",
        "trying to verify what is true",
        "asking for evidence or supporting data",
        "asking for a prediction about what will happen",
        "asking for hidden patterns or implications",
        "asking for a direct conclusion",
        "asking what action to take",
        "giving instructions for how the response should be formatted",
        "correcting or criticizing the response",
        "asking for greater specificity",
        "asking for novelty or non-obvious information",
        "asking for interpretation of past events",
        "reflecting on personal behavior",
        "working through a technical problem",
        "requesting executable steps or commands"
    ];

    public FunctionTrendDetector(IEmbeddingService embeddingService)
    {
        _embeddingService = embeddingService;
    }

    public async Task<IReadOnlyList<FunctionTrend>> DetectAsync(
        IReadOnlyList<Exchange> exchanges,
        IReadOnlyDictionary<string, float[]> embeddings,
        int windowDays = 5,
        CancellationToken cancellationToken = default)
    {
        var eligible = exchanges
            .Where(e =>
                e.StartedAt is not null &&
                embeddings.ContainsKey(e.Id))
            .OrderBy(e => e.StartedAt)
            .ToList();

        if (eligible.Count == 0)
            return [];

        var functionVectors =
            await _embeddingService.EmbedBatchAsync(
                Functions,
                cancellationToken);

        var firstDate = eligible.First().StartedAt!.Value.Date;
        var lastDate = eligible.Last().StartedAt!.Value.Date;

        var windows =
            new List<(DateTimeOffset Start, float[] Centroid)>();

        for (var start = firstDate;
             start <= lastDate;
             start = start.AddDays(windowDays))
        {
            var end = start.AddDays(windowDays);

            var items = eligible
                .Where(e =>
                    e.StartedAt!.Value >= start &&
                    e.StartedAt.Value < end)
                .ToList();

            if (items.Count < 10)
                continue;

            windows.Add((
                start,
                Centroid(items.Select(e => embeddings[e.Id]))));
        }

        if (windows.Count < 3)
            return [];

        var results = new List<FunctionTrend>();

        for (var f = 0; f < Functions.Length; f++)
        {
            var scores = windows
                .Select(w =>
                    Cosine(w.Centroid, functionVectors[f]))
                .ToList();

            var first = scores.First();
            var last = scores.Last();
            var change = last - first;
            var slope = LinearSlope(scores);

            results.Add(new FunctionTrend(
                Functions[f],
                first,
                last,
                change,
                slope));
        }

        return results
            .OrderByDescending(x => x.Slope)
            .ToList();
    }

    private static double LinearSlope(
        IReadOnlyList<double> values)
    {
        var n = values.Count;
        var meanX = (n - 1) / 2.0;
        var meanY = values.Average();

        double numerator = 0;
        double denominator = 0;

        for (var i = 0; i < n; i++)
        {
            var dx = i - meanX;
            numerator += dx * (values[i] - meanY);
            denominator += dx * dx;
        }

        return denominator == 0
            ? 0
            : numerator / denominator;
    }

    private static float[] Centroid(
        IEnumerable<float[]> vectors)
    {
        var list = vectors.ToList();

        if (list.Count == 0)
            return [];

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
        double dot = 0;
        double aa = 0;
        double bb = 0;

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
