namespace ChatAnalyzer.Core.Analysis;

public sealed record PatternLifecycle(
    RecurringPattern Pattern,
    string State,
    int EarlyCount,
    int MiddleCount,
    int LateCount,
    double ChangeScore
);

public sealed class PatternLifecycleDetector
{
    public IReadOnlyList<PatternLifecycle> Detect(
        IReadOnlyList<RecurringPattern> patterns)
    {
        var results = new List<PatternLifecycle>();

        foreach (var pattern in patterns)
        {
            var exchanges = pattern.Matches
                .Select(m => m.Exchange)
                .Append(pattern.Anchor)
                .Where(e => e.StartedAt is not null)
                .OrderBy(e => e.StartedAt)
                .ToList();

            if (exchanges.Count < 3)
                continue;

            var first = exchanges.First().StartedAt!.Value;
            var last = exchanges.Last().StartedAt!.Value;

            var span = last - first;

            if (span.TotalDays < 7)
                continue;

            var third = span.TotalSeconds / 3.0;

            var early = 0;
            var middle = 0;
            var late = 0;

            foreach (var exchange in exchanges)
            {
                var offset =
                    (exchange.StartedAt!.Value - first).TotalSeconds;

                if (offset < third)
                    early++;
                else if (offset < third * 2)
                    middle++;
                else
                    late++;
            }

            var change = late - early;

            string state;

            if (late >= early + 2)
                state = "EMERGING";
            else if (early >= late + 2)
                state = "FADING";
            else if (middle == 0 && early > 0 && late > 0)
                state = "RESURGING";
            else
                state = "PERSISTENT";

            results.Add(new PatternLifecycle(
                pattern,
                state,
                early,
                middle,
                late,
                change));
        }

        return results
            .OrderByDescending(x => Math.Abs(x.ChangeScore))
            .ThenByDescending(x => x.Pattern.SpanDays)
            .Take(50)
            .ToList();
    }
}
