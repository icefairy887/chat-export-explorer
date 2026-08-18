using ChatAnalyzer.Core.Embeddings;
using ChatAnalyzer.Core.Models;

namespace ChatAnalyzer.Core.Analysis;

public sealed record ForecastBacktest(
    string Function,
    int Predictions,
    int Correct,
    double Accuracy,
    double AveragePredictedSlope
);

public sealed class FunctionForecastBacktester
{
    private readonly IEmbeddingService _embeddingService;

    private static readonly string[] Functions =
    [
        "narrating something that happened",
        "describing a current situation",
        "expressing an emotional reaction",
        "expressing a preference or desire",
        "questioning another person's motives",
        "trying to understand why something happened",
        "trying to verify what is true",
        "asking for evidence or supporting data",
        "asking for a prediction about what will happen",
        "asking for hidden patterns or implications",
        "asking for a direct conclusion",
        "asking what action to take",
        "giving instructions for how the response should be formatted",
        "correcting or criticizing the response",
        "asking for greater specificity",
        "asking for novelty or non-obvious information",
        "asking for interpretation of past events",
        "reflecting on personal behavior",
        "working through a technical problem",
        "requesting executable steps or commands"
    ];

    public FunctionForecastBacktester(
        IEmbeddingService embeddingService)
    {
        _embeddingService = embeddingService;
    }

    public async Task<IReadOnlyList<ForecastBacktest>> RunAsync(
        IReadOnlyList<Exchange> exchanges,
        IReadOnlyDictionary<string, float[]> embeddings,
        int windowDays = 5,
        CancellationToken cancellationToken = default)
    {
        var eligible = exchanges
            .Where(e =>
                e.StartedAt is not null &&
                embeddings.ContainsKey(e.Id))
            .OrderBy(e => e.StartedAt)
            .ToList();

        if (eligible.Count == 0)
            return [];

        var functionVectors =
            await _embeddingService.EmbedBatchAsync(
                Functions,
                cancellationToken);

        var first = eligible.First().StartedAt!.Value.Date;
        var last = eligible.Last().StartedAt!.Value.Date;

        var windows = new List<float[]>();

        for (var start = first;
             start <= last;
             start = start.AddDays(windowDays))
        {
            var end = start.AddDays(windowDays);

            var items = eligible
                .Where(e =>
                    e.StartedAt!.Value >= start &&
                    e.StartedAt.Value < end)
                .ToList();

            if (items.Count < 10)
                continue;

            windows.Add(
                Centroid(items.Select(e => embeddings[e.Id])));
        }

        if (windows.Count < 4)
            return [];

        var scores = Functions
            .Select((_, f) =>
                windows.Select(w =>
                    Cosine(w, functionVectors[f]))
                .ToList())
            .ToList();

        var results = new List<ForecastBacktest>();

        for (var f = 0; f < Functions.Length; f++)
        {
            var predictions = 0;
            var correct = 0;
            var predictedSlopes = new List<double>();

            var values = scores[f];

            for (var i = 2; i < values.Count - 1; i++)
            {
                var priorSlope =
                    ((values[i] - values[i - 1]) +
                     (values[i - 1] - values[i - 2])) / 2.0;

                if (Math.Abs(priorSlope) < 0.01)
                    continue;

                var actualNext =
                    values[i + 1] - values[i];

                predictions++;
                predictedSlopes.Add(priorSlope);

                if ((priorSlope > 0 && actualNext > 0) ||
                    (priorSlope < 0 && actualNext < 0))
                {
                    correct++;
                }
            }

            if (predictions == 0)
                continue;

            results.Add(new ForecastBacktest(
                Functions[f],
                predictions,
                correct,
                (double)correct / predictions,
                predictedSlopes.Average()));
        }

        return results
            .OrderByDescending(x => x.Accuracy)
            .ThenByDescending(x => x.Predictions)
            .ToList();
    }

    private static float[] Centroid(
        IEnumerable<float[]> vectors)
    {
        var list = vectors.ToList();

        if (list.Count == 0)
            return [];

        var result = new float[list[0].Length];

        foreach (var vector in list)
            for (var i = 0; i < result.Length; i++)
                result[i] += vector[i];

        for (var i = 0; i < result.Length; i++)
            result[i] /= list.Count;

        return result;
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0;
        double aa = 0;
        double bb = 0;

        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            dot += a[i] * b[i];
            aa += a[i] * a[i];
            bb += b[i] * b[i];
        }

        return aa == 0 || bb == 0
            ? 0
            : dot / (Math.Sqrt(aa) * Math.Sqrt(bb));
    }
}
