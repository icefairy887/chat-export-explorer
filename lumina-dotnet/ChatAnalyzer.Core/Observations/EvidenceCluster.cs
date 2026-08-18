using ChatAnalyzer.Core.Models;

namespace ChatAnalyzer.Core.Observations;

public sealed record EvidenceCluster(
    string Id,
    Exchange Anchor,
    IReadOnlyList<Exchange> RelatedExchanges,
    IReadOnlyList<double> Similarities,
    DateTimeOffset? FirstSeen,
    DateTimeOffset? LastSeen
)
{
    public int Occurrences => RelatedExchanges.Count + 1;

    public int SpanDays =>
        FirstSeen is not null && LastSeen is not null
            ? (int)(LastSeen.Value - FirstSeen.Value).TotalDays
            : 0;
}
