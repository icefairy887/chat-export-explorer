namespace ChatAnalyzer.Core.Analysis;

public sealed record FindingReport(
    double Score,
    string HiddenChange,
    string NextObservableConsequence,
    IReadOnlyList<string> EarlyEvidence,
    IReadOnlyList<string> LateEvidence
);

public sealed class FindingReportBuilder
{
    public IReadOnlyList<FindingReport> Build(
        IReadOnlyList<FindingSignals> findings)
    {
        var reports = new List<FindingReport>();

        foreach (var finding in findings)
        {
            if (finding.EarlySignals.Count == 0 ||
                finding.LateSignals.Count == 0)
                continue;

            var early = Condense(finding.EarlySignals[0]);
            var late = Condense(finding.LateSignals[0]);

            var hiddenChange =
                $"The dominant conversation pattern shifted from \"{early}\" toward \"{late}\".";

            var consequence =
                finding.LateSignals.Count > 1
                    ? $"The next observable behavior is more likely to resemble: \"{Condense(finding.LateSignals[1])}\"."
                    : $"The newer pattern is more likely to recur than the older one.";

            reports.Add(new FindingReport(
                finding.Candidate.Score,
                hiddenChange,
                consequence,
                finding.EarlySignals,
                finding.LateSignals
            ));
        }

        return reports
            .OrderByDescending(x => x.Score)
            .ToList();
    }

    private static string Condense(string text)
    {
        text = text.Trim();

        if (text.Length <= 150)
            return text;

        return text[..147] + "...";
    }
}
