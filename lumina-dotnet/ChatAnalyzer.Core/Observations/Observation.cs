using ChatAnalyzer.Core.Models;

namespace ChatAnalyzer.Core.Observations;

public sealed record Observation(
    string Id,
    string Subject,
    string Behavior,
    string? StatedBelief,
    string? Action,
    string? Direction,
    DateTimeOffset? ObservedAt,
    IReadOnlyList<string> EvidenceExchangeIds,
    double Confidence
);
