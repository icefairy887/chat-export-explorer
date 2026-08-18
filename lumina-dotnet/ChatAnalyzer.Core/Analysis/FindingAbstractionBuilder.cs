using System.Text.RegularExpressions;

namespace ChatAnalyzer.Core.Analysis;

public sealed record AbstractFinding(
    double Score,
    string EarlyMode,
    string LateMode,
    string HiddenChange,
    IReadOnlyList<string> EarlyEvidence,
    IReadOnlyList<string> LateEvidence
);

public sealed class FindingAbstractionBuilder
{
    private static readonly HashSet<string> StopWords = new(
        new[]
        {
            "i","im","i'm","me","my","mine","we","our","you","your",
            "he","him","his","she","her","they","them","their",
            "it","its","this","that","these","those",
            "a","an","the","and","or","but","so","if","then",
            "to","of","in","on","at","for","from","with","as",
            "is","are","was","were","be","been","being",
            "do","does","did","have","has","had",
            "would","could","should","can","cant","can't",
            "just","really","like","yeah","okay","ok","dude",
            "fuck","fucking","shit","idk","dont","don't",
            "what","why","how","when","where","who",
            "about","something","anything","everything",
            "know","think","mean","want"
        },
        StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<AbstractFinding> Build(
        IReadOnlyList<FindingSignals> findings)
    {
        return findings
            .Select(BuildOne)
            .Where(x => x is not null)
            .Select(x => x!)
            .OrderByDescending(x => x.Score)
            .ToList();
    }

    private static AbstractFinding? BuildOne(FindingSignals finding)
    {
        if (finding.EarlySignals.Count == 0 ||
            finding.LateSignals.Count == 0)
            return null;

        var earlyMode = BuildModeLabel(
            finding.EarlySignals,
            finding.LateSignals);

        var lateMode = BuildModeLabel(
            finding.LateSignals,
            finding.EarlySignals);

        if (string.IsNullOrWhiteSpace(earlyMode) ||
            string.IsNullOrWhiteSpace(lateMode))
            return null;

        return new AbstractFinding(
            finding.Candidate.Score,
            earlyMode,
            lateMode,
            $"Shifted from [{earlyMode}] toward [{lateMode}].",
            finding.EarlySignals,
            finding.LateSignals
        );
    }

    private static string BuildModeLabel(
        IReadOnlyList<string> side,
        IReadOnlyList<string> opposite)
    {
        var sideTokens = side
            .SelectMany(Tokenize)
            .ToList();

        var oppositeTokens = opposite
            .SelectMany(Tokenize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unigrams = sideTokens
            .Where(x => !oppositeTokens.Contains(x))
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Phrase = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .ThenByDescending(x => x.Phrase.Length)
            .ToList();

        var sideBigrams = side
            .SelectMany(GetBigrams)
            .ToList();

        var oppositeBigrams = opposite
            .SelectMany(GetBigrams)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var bigrams = sideBigrams
            .Where(x => !oppositeBigrams.Contains(x))
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Phrase = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .ThenByDescending(x => x.Phrase.Length)
            .ToList();

        var chosen = new List<string>();

        foreach (var item in bigrams)
        {
            if (chosen.Count >= 2)
                break;

            if (!chosen.Any(x =>
                x.Contains(item.Phrase, StringComparison.OrdinalIgnoreCase) ||
                item.Phrase.Contains(x, StringComparison.OrdinalIgnoreCase)))
            {
                chosen.Add(item.Phrase);
            }
        }

        foreach (var item in unigrams)
        {
            if (chosen.Count >= 3)
                break;

            if (!chosen.Any(x =>
                x.Contains(item.Phrase, StringComparison.OrdinalIgnoreCase)))
            {
                chosen.Add(item.Phrase);
            }
        }

        return string.Join(" / ", chosen);
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        return Regex.Matches(
                text.ToLowerInvariant(),
                @"[a-z][a-z']+")
            .Select(m => m.Value.Trim('\''))
            .Where(x =>
                x.Length >= 3 &&
                !StopWords.Contains(x));
    }

    private static IEnumerable<string> GetBigrams(string text)
    {
        var tokens = Tokenize(text).ToList();

        for (var i = 0; i < tokens.Count - 1; i++)
            yield return $"{tokens[i]} {tokens[i + 1]}";
    }
}
