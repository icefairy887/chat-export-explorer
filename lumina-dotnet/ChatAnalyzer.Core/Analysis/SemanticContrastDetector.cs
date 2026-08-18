using System.Text.RegularExpressions;
using ChatAnalyzer.Core.Embeddings;

namespace ChatAnalyzer.Core.Analysis;

public sealed record SemanticContrast(
    IReadOnlyList<string> EarlySignals,
    IReadOnlyList<string> LateSignals
);

public sealed class SemanticContrastDetector
{
    private readonly IEmbeddingService _embeddings;

    public SemanticContrastDetector(IEmbeddingService embeddings)
    {
        _embeddings = embeddings;
    }

    public async Task<SemanticContrast> CompareAsync(
        IEnumerable<string> earlyTexts,
        IEnumerable<string> lateTexts,
        float[] earlyCentroid,
        float[] lateCentroid)
    {
        var earlyCandidates = ExtractPhrases(earlyTexts)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToList();

        var lateCandidates = ExtractPhrases(lateTexts)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToList();

        if (earlyCandidates.Count == 0 || lateCandidates.Count == 0)
            return new SemanticContrast([], []);

        var all = earlyCandidates
            .Concat(lateCandidates)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var vectors = await _embeddings.EmbedBatchAsync(all);

        var map = all
            .Select((phrase, i) => new { phrase, vector = vectors[i] })
            .ToDictionary(
                x => x.phrase,
                x => x.vector,
                StringComparer.OrdinalIgnoreCase);

        var early = earlyCandidates
            .Where(p => !HasEquivalent(p, lateCandidates, map))
            .Select(p => new
            {
                Phrase = p,
                Score =
                    Cosine(map[p], earlyCentroid) -
                    Cosine(map[p], lateCentroid)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(5)
            .Select(x => x.Phrase)
            .ToList();

        var late = lateCandidates
            .Where(p => !HasEquivalent(p, earlyCandidates, map))
            .Select(p => new
            {
                Phrase = p,
                Score =
                    Cosine(map[p], lateCentroid) -
                    Cosine(map[p], earlyCentroid)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(5)
            .Select(x => x.Phrase)
            .ToList();

        return new SemanticContrast(early, late);
    }

    private static bool HasEquivalent(
        string phrase,
        IReadOnlyList<string> opposite,
        IReadOnlyDictionary<string, float[]> vectors)
    {
        foreach (var other in opposite)
        {
            if (string.Equals(
                phrase,
                other,
                StringComparison.OrdinalIgnoreCase))
                return true;

            if (Cosine(vectors[phrase], vectors[other]) >= 0.82)
                return true;
        }

        return false;
    }

    private static IEnumerable<string> ExtractPhrases(
        IEnumerable<string> texts)
    {
        foreach (var text in texts)
        {
            foreach (var sentence in Regex.Split(
                text,
                @"(?<=[.!?])\s+"))
            {
                var cleaned = Regex
                    .Replace(sentence, @"\s+", " ")
                    .Trim();

                if (cleaned.Length >= 25 &&
                    cleaned.Length <= 180)
                {
                    yield return cleaned;
                }
            }
        }
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
