using ChatAnalyzer.Core.Models;

namespace ChatAnalyzer.Core.Processing;

public static class TimelineSampler
{
    public static IReadOnlyList<Exchange> Sample(
        IReadOnlyList<Exchange> exchanges,
        int targetCount = 1200)
    {
        var eligible = exchanges
            .Where(e =>
                e.StartedAt is not null &&
                !string.IsNullOrWhiteSpace(e.UserText) &&
                e.UserText.Length >= 40)
            .OrderBy(e => e.StartedAt)
            .ToList();

        if (eligible.Count <= targetCount)
            return eligible;

        var sampled = new List<Exchange>(targetCount);
        var step = (double)eligible.Count / targetCount;

        for (var i = 0; i < targetCount; i++)
        {
            var index = Math.Min(
                (int)Math.Floor(i * step),
                eligible.Count - 1);

            sampled.Add(eligible[index]);
        }

        return sampled
            .DistinctBy(e => e.Id)
            .ToList();
    }
}
