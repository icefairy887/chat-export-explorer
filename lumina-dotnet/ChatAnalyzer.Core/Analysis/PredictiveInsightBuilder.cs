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

            var early = finding.EarlyFunctions
                .Take(2)
                .Select(x => x.Function)
                .ToList();

            var momentum = rising
                .Average(x => trendMap[x.Function].Slope);

            var predictiveScore =
                item.FinalScore *
                (1.0 + momentum);

            var hiddenChange =
                $"Across independent conversations, use shifted from {Join(early)} toward {Join(rising.Take(3).Select(x => x.Function).ToList())}.";

            var convergenceSignals = rising
                .Select(x => $"{x.Function} (+{x.Score:F3})")
                .ToList();

            var momentumSignals = rising
                .Select(x =>
                {
                    var trend = trendMap[x.Function];
                    return $"{x.Function}: change {trend.Change:+0.000;-0.000;0.000}, slope {trend.Slope:+0.000;-0.000;0.000}";
                })
                .ToList();

            var consequence =
                $"Because these functions are still rising across successive time windows, upcoming conversations are most likely to increasingly combine {Join(rising.Take(3).Select(x => x.Function).ToList())}.";

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

    private static string Join(IReadOnlyList<string> items)
    {
        if (items.Count == 0)
            return "an unidentified pattern";

        if (items.Count == 1)
            return items[0];

        if (items.Count == 2)
            return $"{items[0]} and {items[1]}";

        return $"{string.Join(", ", items.Take(items.Count - 1))}, and {items[^1]}";
    }
}
