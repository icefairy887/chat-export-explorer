using System.Text.RegularExpressions;
using ChatAnalyzer.Core.Embeddings;

namespace ChatAnalyzer.Core.Analysis;

public sealed record FindingSignals(
    FindingCandidate Candidate,
    IReadOnlyList<string> EarlySignals,
    IReadOnlyList<string> LateSignals
);

public sealed class FindingSignalExtractor
{
    private readonly IEmbeddingService _embeddingService;

    public FindingSignalExtractor(IEmbeddingService embeddingService)
    {
        _embeddingService = embeddingService;
    }

    public async Task<IReadOnlyList<FindingSignals>> ExtractAsync(
        IReadOnlyList<FindingCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        var results = new List<FindingSignals>();

        foreach (var candidate in candidates)
        {
            var early = ExtractSentences(
                candidate.EarlyEvidence.Select(x => x.UserText));

            var late = ExtractSentences(
                candidate.LateEvidence.Select(x => x.UserText));

            if (early.Count == 0 || late.Count == 0)
                continue;

            var all = early.Concat(late).Distinct().ToList();

            var vectors = await _embeddingService.EmbedBatchAsync(
                all,
                cancellationToken);

            var map = all
                .Select((text, index) => new { text, vector = vectors[index] })
                .ToDictionary(x => x.text, x => x.vector);

            var earlyCentroid = Centroid(early.Select(x => map[x]));
            var lateCentroid = Centroid(late.Select(x => map[x]));

            var earlySignals = early
                .Select(text => new
                {
                    Text = text,
                    Score =
                        Cosine(map[text], earlyCentroid) -
                        Cosine(map[text], lateCentroid)
                })
                .OrderByDescending(x => x.Score)
                .Select(x => x.Text)
                .Distinct()
                .Take(4)
                .ToList();

            var lateSignals = late
                .Select(text => new
                {
                    Text = text,
                    Score =
                        Cosine(map[text], lateCentroid) -
                        Cosine(map[text], earlyCentroid)
                })
                .OrderByDescending(x => x.Score)
                .Select(x => x.Text)
                .Distinct()
                .Take(4)
                .ToList();

            results.Add(new FindingSignals(
                candidate,
                earlySignals,
                lateSignals));
        }

        return results;
    }

    private static List<string> ExtractSentences(
        IEnumerable<string> texts)
    {
        return texts
            .SelectMany(text =>
                Regex.Split(
                    text,
                    @"(?<=[.!?])\s+|\r?\n+"))
            .Select(x => x.Trim())
            .Where(x => x.Length >= 25 && x.Length <= 240)
            .Distinct()
            .Take(80)
            .ToList();
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
