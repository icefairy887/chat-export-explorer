using ChatAnalyzer.Core.Models;

namespace ChatAnalyzer.Core.Analysis;

public sealed record PredictiveInsight(
    double Score,
    double Independence,
    double Momentum,
    string HiddenChange,
    IReadOnlyList<string> ConvergingSignals,
    IReadOnlyList<string> MomentumSignals,
    string NextObservableConsequence,
    IReadOnlyList<Exchange> SupportingEvidence
);

public sealed class PredictiveInsightBuilder
{
    public IReadOnlyList<PredictiveInsight> Build(
        IReadOnlyList<IndependentFinding> findings,
        IReadOnlyList<FunctionTrend> trends)
    {
        var trendMap = trends.ToDictionary(
            x => x.Function,
            StringComparer.OrdinalIgnoreCase);

        var results = new List<PredictiveInsight>();

        foreach (var item in findings)
        {
            if (item.IndependenceScore < 0.65 ||
                item.EarlyConversations < 3 ||
                item.LateConversations < 3)
                continue;

            var finding = item.Finding;

            var rising = finding.RisingFunctions
                .Where(x =>
                    trendMap.TryGetValue(x.Function, out var trend) &&
                    trend.Slope > 0.01 &&
                    trend.Change > 0.02)
                .Take(4)
                .ToList();

            if (rising.Count < 2)
                continue;

            var momentum = rising
                .Average(x => trendMap[x.Function].Slope);

            var predictiveScore =
                item.FinalScore *
                (1.0 + momentum);

            var earlySignal = finding.Source.EarlySignals.FirstOrDefault();
            var lateSignal = finding.Source.LateSignals.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(earlySignal) ||
                string.IsNullOrWhiteSpace(lateSignal))
                continue;

            var hiddenChange =
                $"The evidence moved from “{Condense(earlySignal, 135)}” to “{Condense(lateSignal, 135)}” across {item.EarlyConversations + item.LateConversations} independent conversations.";

            var convergenceSignals = finding.Source.LateSignals
                .Take(4)
                .Select(x => Condense(x, 92))
                .ToList();

            var momentumSignals = rising
                .Select(x =>
                {
                    var trend = trendMap[x.Function];
                    return $"{x.Function}: change {trend.Change:+0.000;-0.000;0.000}, slope {trend.Slope:+0.000;-0.000;0.000}";
                })
                .ToList();

            var consequence =
                $"The next test is observable repetition: another dated action or outcome consistent with “{Condense(lateSignal, 110)}”. If that does not recur, Lumina should downgrade this claim rather than turn it into a personality label.";

            var evidence = item.Finding.Source.Candidate.EarlyEvidence
                .Concat(item.Finding.Source.Candidate.LateEvidence)
                .DistinctBy(e => e.Id)
                .ToList();

            results.Add(new PredictiveInsight(
                predictiveScore,
                item.IndependenceScore,
                momentum,
                hiddenChange,
                convergenceSignals,
                momentumSignals,
                consequence,
                evidence));
        }

        return results
            .OrderByDescending(x => x.Score)
            .ToList();
    }

    private static string Condense(string text, int length)
    {
        var cleaned = text
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();

        return cleaned.Length <= length
            ? cleaned
            : cleaned[..length].TrimEnd() + "…";
    }
}
