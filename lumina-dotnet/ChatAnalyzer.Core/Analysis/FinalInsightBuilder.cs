namespace ChatAnalyzer.Core.Analysis;

public sealed record FinalInsight(
    double Score,
    double Independence,
    string HiddenChange,
    IReadOnlyList<string> Signals,
    string NextObservableConsequence
);

public sealed class FinalInsightBuilder
{
    public IReadOnlyList<FinalInsight> Build(
        IReadOnlyList<IndependentFinding> findings)
    {
        var results = new List<FinalInsight>();

        foreach (var item in findings)
        {
            if (item.IndependenceScore < 0.65 ||
                item.EarlyConversations < 3 ||
                item.LateConversations < 3)
                continue;

            var finding = item.Finding;

            var early = finding.EarlyFunctions
                .Take(2)
                .Select(x => x.Function)
                .ToList();

            var rising = finding.RisingFunctions
                .Take(4)
                .Select(x => x.Function)
                .ToList();

            if (rising.Count < 2)
                continue;

            var hiddenChange =
                $"Across independent conversations, use shifted from {Join(early)} toward {Join(rising.Take(3).ToList())}.";

            var signals = finding.RisingFunctions
                .Take(4)
                .Select(x => $"{x.Function} (+{x.Score:F3})")
                .ToList();

            var consequence =
                $"The next observable consequence is more conversations combining {Join(rising.Take(3).ToList())} in the same request, rather than staying focused on interpreting one event at a time.";

            results.Add(new FinalInsight(
                item.FinalScore,
                item.IndependenceScore,
                hiddenChange,
                signals,
                consequence));
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
