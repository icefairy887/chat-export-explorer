using ChatAnalyzer.Core.Analysis;

namespace ChatAnalyzer.Core.Observations;

public sealed class EvidenceClusterBuilder
{
    public IReadOnlyList<EvidenceCluster> Build(
        IReadOnlyList<RecurringPattern> patterns)
    {
        return patterns
            .Select(pattern =>
            {
                var exchanges = pattern.Matches
                    .Select(m => m.Exchange)
                    .Append(pattern.Anchor)
                    .Where(e => e.StartedAt is not null)
                    .OrderBy(e => e.StartedAt)
                    .ToList();

                return new EvidenceCluster(
                    pattern.Anchor.Id,
                    pattern.Anchor,
                    pattern.Matches.Select(m => m.Exchange).ToList(),
                    pattern.Matches.Select(m => m.Similarity).ToList(),
                    exchanges.FirstOrDefault()?.StartedAt,
                    exchanges.LastOrDefault()?.StartedAt);
            })
            .Where(cluster =>
                cluster.Occurrences >= 3 &&
                cluster.SpanDays >= 7)
            .OrderByDescending(cluster => cluster.SpanDays)
            .ToList();
    }
}
