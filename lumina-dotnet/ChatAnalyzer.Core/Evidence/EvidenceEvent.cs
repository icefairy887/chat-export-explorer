using ChatAnalyzer.Core.Models;

namespace ChatAnalyzer.Core.Evidence;

public enum EvidenceKind
{
    Claim,
    Action,
    Decision,
    Preference,
    Outcome
}

public sealed record EvidenceEvent(
    string Id,
    string ConversationId,
    string ExchangeId,
    DateTimeOffset? Timestamp,
    EvidenceKind Kind,
    string Text,
    double Confidence,
    Exchange Source
);
