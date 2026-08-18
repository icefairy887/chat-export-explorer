using ChatAnalyzer.Core.Embeddings;

namespace ChatAnalyzer.Core.Analysis;

public sealed record FunctionScore(
    string Function,
    double Score
);

public sealed record FunctionalFinding(
    double Score,
    IReadOnlyList<FunctionScore> EarlyFunctions,
    IReadOnlyList<FunctionScore> LateFunctions,
    IReadOnlyList<FunctionScore> RisingFunctions,
    IReadOnlyList<FunctionScore> FallingFunctions,
    FindingSignals Source
);

public sealed class ConversationFunctionProfiler
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

    public ConversationFunctionProfiler(
        IEmbeddingService embeddingService)
    {
        _embeddingService = embeddingService;
    }

    public async Task<IReadOnlyList<FunctionalFinding>> ProfileAsync(
        IReadOnlyList<FindingSignals> findings,
        CancellationToken cancellationToken = default)
    {
        var functionVectors = await _embeddingService.EmbedBatchAsync(
            Functions,
            cancellationToken);

        var results = new List<FunctionalFinding>();

        foreach (var finding in findings)
        {
            if (finding.EarlySignals.Count == 0 ||
                finding.LateSignals.Count == 0)
                continue;

            var earlyVectors = await _embeddingService.EmbedBatchAsync(
                finding.EarlySignals,
                cancellationToken);

            var lateVectors = await _embeddingService.EmbedBatchAsync(
                finding.LateSignals,
                cancellationToken);

            var earlyCentroid = Centroid(earlyVectors);
            var lateCentroid = Centroid(lateVectors);

            var earlyScores = ScoreFunctions(
                earlyCentroid,
                functionVectors);

            var lateScores = ScoreFunctions(
                lateCentroid,
                functionVectors);

            var deltas = Functions
                .Select((name, i) => new
                {
                    Name = name,
                    Delta = lateScores[i].Score - earlyScores[i].Score
                })
                .ToList();

            var rising = deltas
                .Where(x => x.Delta > 0.02)
                .OrderByDescending(x => x.Delta)
                .Take(4)
                .Select(x => new FunctionScore(x.Name, x.Delta))
                .ToList();

            var falling = deltas
                .Where(x => x.Delta < -0.02)
                .OrderBy(x => x.Delta)
                .Take(4)
                .Select(x => new FunctionScore(x.Name, Math.Abs(x.Delta)))
                .ToList();

            results.Add(new FunctionalFinding(
                finding.Candidate.Score,
                earlyScores
                    .OrderByDescending(x => x.Score)
                    .Take(4)
                    .ToList(),
                lateScores
                    .OrderByDescending(x => x.Score)
                    .Take(4)
                    .ToList(),
                rising,
                falling,
                finding));
        }

        return results
            .OrderByDescending(x => x.Score)
            .ToList();
    }

    private static List<FunctionScore> ScoreFunctions(
        float[] centroid,
        IReadOnlyList<float[]> functionVectors)
    {
        return Functions
            .Select((name, i) =>
                new FunctionScore(
                    name,
                    Cosine(centroid, functionVectors[i])))
            .ToList();
    }

    private static float[] Centroid(
        IReadOnlyList<float[]> vectors)
    {
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

