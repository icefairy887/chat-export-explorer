using ChatAnalyzer.Core.Models;

namespace ChatAnalyzer.Core.Analysis;

public sealed record FindingCandidate(
    double Score,
    double Drift,
    int DistinctConversations,
    DateTimeOffset EarlyStart,
    DateTimeOffset EarlyEnd,
    DateTimeOffset LateStart,
    DateTimeOffset LateEnd,
    IReadOnlyList<Exchange> EarlyEvidence,
    IReadOnlyList<Exchange> LateEvidence
);

public sealed class FindingCandidateBuilder
{
    public IReadOnlyList<FindingCandidate> Build(
        IReadOnlyList<TemporalDrift> drifts)
    {
        var results = new List<FindingCandidate>();

        foreach (var drift in drifts)
        {
            var evidence = drift.EarlyContributors
                .Concat(drift.LateContributors)
                .DistinctBy(e => e.Id)
                .ToList();

            var conversations = evidence
                .Select(e => e.ConversationId)
                .Distinct()
                .Count();

            if (conversations < 3)
                continue;

            var score =
                drift.Drift *
                Math.Log2(conversations + 1) *
                Math.Log2(evidence.Count + 1);

            results.Add(new FindingCandidate(
                Score: score,
                Drift: drift.Drift,
                DistinctConversations: conversations,
                EarlyStart: drift.EarlyStart,
                EarlyEnd: drift.EarlyEnd,
                LateStart: drift.LateStart,
                LateEnd: drift.LateEnd,
                EarlyEvidence: drift.EarlyContributors,
                LateEvidence: drift.LateContributors
            ));
        }

        return results
            .OrderByDescending(x => x.Score)
            .Take(10)
            .ToList();
    }
}
