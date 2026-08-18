using ChatAnalyzer.Core.Models;

namespace ChatAnalyzer.Core.Analysis;

public sealed record IndependentFinding(
    FunctionalFinding Finding,
    double OriginalScore,
    double IndependenceScore,
    double FinalScore,
    int EarlyConversations,
    int LateConversations,
    double EarlyDiversity,
    double LateDiversity
);

public sealed class EvidenceIndependenceScorer
{
    public IReadOnlyList<IndependentFinding> Score(
        IReadOnlyList<FunctionalFinding> findings,
        IReadOnlyDictionary<string, float[]> embeddings)
    {
        var results = new List<IndependentFinding>();

        foreach (var finding in findings)
        {
            var early = finding.Source.Candidate.EarlyEvidence
                .DistinctBy(e => e.Id)
                .Where(e => embeddings.ContainsKey(e.Id))
                .ToList();

            var late = finding.Source.Candidate.LateEvidence
                .DistinctBy(e => e.Id)
                .Where(e => embeddings.ContainsKey(e.Id))
                .ToList();

            if (early.Count < 2 || late.Count < 2)
                continue;

            var earlyConversations = early
                .Select(e => e.ConversationId)
                .Distinct()
                .Count();

            var lateConversations = late
                .Select(e => e.ConversationId)
                .Distinct()
                .Count();

            var earlyConversationRatio =
                (double)earlyConversations / early.Count;

            var lateConversationRatio =
                (double)lateConversations / late.Count;

            var conversationIndependence =
                (earlyConversationRatio + lateConversationRatio) / 2.0;

            var earlyDiversity = SemanticDiversity(early, embeddings);
            var lateDiversity = SemanticDiversity(late, embeddings);

            var diversity =
                (earlyDiversity + lateDiversity) / 2.0;

            var independence =
                (conversationIndependence * 0.60) +
                (diversity * 0.40);

            var finalScore =
                finding.Score *
                (0.50 + independence);

            results.Add(new IndependentFinding(
                finding,
                finding.Score,
                independence,
                finalScore,
                earlyConversations,
                lateConversations,
                earlyDiversity,
                lateDiversity));
        }

        return results
            .OrderByDescending(x => x.FinalScore)
            .ToList();
    }

    private static double SemanticDiversity(
        IReadOnlyList<Exchange> evidence,
        IReadOnlyDictionary<string, float[]> embeddings)
    {
        var distances = new List<double>();

        for (var i = 0; i < evidence.Count; i++)
        {
            for (var j = i + 1; j < evidence.Count; j++)
            {
                var similarity = Cosine(
                    embeddings[evidence[i].Id],
                    embeddings[evidence[j].Id]);

                distances.Add(
                    Math.Clamp(1.0 - similarity, 0.0, 1.0));
            }
        }

        return distances.Count == 0
            ? 0
            : distances.Average();
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
