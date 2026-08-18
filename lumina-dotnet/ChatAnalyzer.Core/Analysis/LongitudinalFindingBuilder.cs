namespace ChatAnalyzer.Core.Analysis;

public sealed record LongitudinalFinding(
    double Score,
    string HiddenChange,
    IReadOnlyList<string> Signals,
    string NextObservableConsequence
);

public sealed class LongitudinalFindingBuilder
{
    public IReadOnlyList<LongitudinalFinding> Build(
        IReadOnlyList<FunctionalFinding> findings)
    {
        var results = new List<LongitudinalFinding>();

        foreach (var finding in findings)
        {
            if (finding.EarlyFunctions.Count == 0 ||
                finding.LateFunctions.Count == 0 ||
                finding.RisingFunctions.Count == 0)
                continue;

            var early = finding.EarlyFunctions
                .Take(2)
                .Select(x => x.Function)
                .ToList();

            var rising = finding.RisingFunctions
                .Take(3)
                .Select(x => x.Function)
                .ToList();

            var late = finding.LateFunctions
                .Take(3)
                .Select(x => x.Function)
                .ToList();

            var hiddenChange =
                $"Conversation use shifted from {Join(early)} toward {Join(rising)}.";

            var signals = finding.RisingFunctions
                .Take(4)
                .Select(x => $"{x.Function} (+{x.Score:F3})")
                .ToList();

            var consequence =
                $"Newer conversations are increasingly likely to involve {Join(late)}, rather than remaining centered on the earlier functions.";

            results.Add(new LongitudinalFinding(
                finding.Score,
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
