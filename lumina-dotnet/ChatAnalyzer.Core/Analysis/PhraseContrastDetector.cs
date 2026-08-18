using System.Text.RegularExpressions;

namespace ChatAnalyzer.Core.Analysis;

public sealed record PhraseContrast(
    IReadOnlyList<string> Fading,
    IReadOnlyList<string> Emerging
);

public sealed class PhraseContrastDetector
{
    private static readonly HashSet<string> StopWords =
    [
        "the","and","that","this","with","was","were","have","has","had",
        "but","for","you","your","from","they","them","then","than","what",
        "when","where","which","would","could","should","about","just","like",
        "really","know","dont","didnt","doesnt","into","out","its","im","ive",
        "me","my","i","a","an","to","of","in","on","at","is","it","he","she"
    ];

    public PhraseContrast Compare(
        IEnumerable<string> earlyTexts,
        IEnumerable<string> lateTexts)
    {
        var early = CountTerms(earlyTexts);
        var late = CountTerms(lateTexts);

        var all = early.Keys
            .Union(late.Keys)
            .Distinct();

        var changes = all
            .Select(term => new
            {
                Term = term,
                Early = early.GetValueOrDefault(term),
                Late = late.GetValueOrDefault(term)
            })
            .Where(x => x.Early + x.Late >= 2)
            .ToList();

        var emerging = changes
            .OrderByDescending(x => x.Late - x.Early)
            .ThenByDescending(x => x.Late)
            .Where(x => x.Late > x.Early)
            .Take(8)
            .Select(x => x.Term)
            .ToList();

        var fading = changes
            .OrderByDescending(x => x.Early - x.Late)
            .ThenByDescending(x => x.Early)
            .Where(x => x.Early > x.Late)
            .Take(8)
            .Select(x => x.Term)
            .ToList();

        return new PhraseContrast(fading, emerging);
    }

    private static Dictionary<string, int> CountTerms(
        IEnumerable<string> texts)
    {
        var counts = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var text in texts)
        {
            var words = Regex.Matches(
                    text.ToLowerInvariant(),
                    @"[a-z']+")
                .Select(m => m.Value)
                .Where(w =>
                    w.Length >= 3 &&
                    !StopWords.Contains(w))
                .ToList();

            var terms = words
                .Concat(
                    words.Zip(
                        words.Skip(1),
                        (a, b) => $"{a} {b}"));

            foreach (var term in terms)
                counts[term] = counts.GetValueOrDefault(term) + 1;
        }

        return counts;
    }
}
