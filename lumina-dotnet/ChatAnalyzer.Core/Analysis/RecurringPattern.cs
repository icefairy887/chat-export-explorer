using ChatAnalyzer.Core.Models;

namespace ChatAnalyzer.Core.Analysis;

public sealed record PatternMatch(
    Exchange Exchange,
    double Similarity
);

public sealed record RecurringPattern(
    Exchange Anchor,
    IReadOnlyList<PatternMatch> Matches,
    double Score,
    int DistinctConversations,
    int SpanDays
);
