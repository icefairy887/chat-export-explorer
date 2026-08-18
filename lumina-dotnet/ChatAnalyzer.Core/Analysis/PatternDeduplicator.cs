namespace ChatAnalyzer.Core.Analysis;

public sealed class PatternDeduplicator
{
    public IReadOnlyList<RecurringPattern> Deduplicate(
        IReadOnlyList<RecurringPattern> patterns,
        double overlapThreshold = 0.50)
    {
        var kept = new List<RecurringPattern>();

        foreach (var candidate in patterns
            .OrderByDescending(p => p.Score))
        {
            var candidateIds = MemberIds(candidate);

            var duplicate = kept.Any(existing =>
            {
                var existingIds = MemberIds(existing);

                var intersection = candidateIds
                    .Intersect(existingIds)
                    .Count();

                var smaller = Math.Min(
                    candidateIds.Count,
                    existingIds.Count);

                if (smaller == 0)
                    return false;

                var overlap =
                    (double)intersection / smaller;

                return overlap >= overlapThreshold;
            });

            if (!duplicate)
                kept.Add(candidate);
        }

        return kept;
    }

    private static HashSet<string> MemberIds(
        RecurringPattern pattern)
    {
        return pattern.Matches
            .Select(m => m.Exchange.Id)
            .Append(pattern.Anchor.Id)
            .ToHashSet();
    }
}
