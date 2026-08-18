namespace ChatAnalyzer.Core.Observations;

public sealed record ObservationCandidate(
    string Subject,
    string Behavior,
    string? StatedBelief,
    string? Action,
    string? Direction,
    string? Contradiction,
    string? LikelyConsequence,
    double Confidence,
    IReadOnlyList<string> EvidenceExchangeIds
);
