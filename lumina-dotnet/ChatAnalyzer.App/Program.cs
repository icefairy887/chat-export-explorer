using ChatAnalyzer.Core.Observations;
using ChatAnalyzer.Core.Embeddings;
using ChatAnalyzer.Core.Analysis;
using ChatAnalyzer.Core.Importing;
using ChatAnalyzer.Core.Models;
using ChatAnalyzer.Core.Processing;
using ChatAnalyzer.Core.Storage;

if (args.Length == 0)
{
    Console.WriteLine("Usage:");
    Console.WriteLine(@"dotnet run --project .\ChatAnalyzer.App -- <export1.json> <export2.json> ...");
    return;
}

var importer = new ChatGptExportImporter();
var imported = new List<Conversation>();

foreach (var path in args)
{
    if (!File.Exists(path))
    {
        Console.WriteLine($"File not found: {path}");
        continue;
    }

    Console.WriteLine($"Importing: {Path.GetFileName(path)}");

    await using var stream = File.OpenRead(path);
    var importedConversations = await importer.ImportAsync(stream);

    Console.WriteLine($"  Found {importedConversations.Count:N0} conversations");
    imported.AddRange(importedConversations);
}

if (imported.Count == 0)
{
    Console.WriteLine("No conversations were imported.");
    return;
}

var conversations = MergeConversations(imported);

var exchangeBuilder = new ExchangeBuilder();
var exchanges = exchangeBuilder.Build(conversations);

var messages = conversations.SelectMany(c => c.Messages).ToList();

Console.WriteLine();
Console.WriteLine("MERGED CORPUS");
Console.WriteLine("-------------");
Console.WriteLine($"Unique conversations: {conversations.Count:N0}");
Console.WriteLine($"Unique messages:      {messages.Count:N0}");
Console.WriteLine($"Exchanges:            {exchanges.Count:N0}");

Console.WriteLine();
Console.WriteLine("EMBEDDING TEST");
Console.WriteLine("--------------");

var modelPath = Path.Combine(
    Environment.CurrentDirectory,
    "Models",
    "all-MiniLM-L6-v2",
    "model.onnx");

var vocabPath = Path.Combine(
    Environment.CurrentDirectory,
    "Models",
    "all-MiniLM-L6-v2",
    "vocab.txt");

var databasePath = Path.Combine(
    Environment.CurrentDirectory,
    "analyzer.db");

using var embeddingService =
    new MiniLmEmbeddingService(modelPath, vocabPath);

var store = new EmbeddingStore(databasePath);

var processor = new ExchangeEmbeddingProcessor(
    embeddingService,
    store);

var cachedEmbeddings = await store.GetAllAsync();

var timelineSample = TimelineSampler.Sample(exchanges, targetCount: 1200);

Console.WriteLine($"Timeline sample: {timelineSample.Count:N0} exchanges");
Console.WriteLine($"Sample coverage: {timelineSample.First().StartedAt:yyyy-MM-dd} -> {timelineSample.Last().StartedAt:yyyy-MM-dd}");

var missingSampleVectors = timelineSample
    .Where(e => !cachedEmbeddings.ContainsKey(e.Id))
    .ToList();

Console.WriteLine($"Missing sampled vectors: {missingSampleVectors.Count:N0}");

if (missingSampleVectors.Count > 0)
{
    Console.WriteLine($"Missing coverage: {missingSampleVectors.First().StartedAt:yyyy-MM-dd} -> {missingSampleVectors.Last().StartedAt:yyyy-MM-dd}");
    await processor.ProcessAsync(missingSampleVectors);
    cachedEmbeddings = await store.GetAllAsync();
}


Console.WriteLine();
Console.WriteLine("RECURRING PATTERNS");
Console.WriteLine("------------------");
Console.WriteLine($"Vectors available: {cachedEmbeddings.Count:N0}");

var vectorDates = exchanges
    .Where(e => e.StartedAt is not null && cachedEmbeddings.ContainsKey(e.Id))
    .Select(e => e.StartedAt!.Value)
    .OrderBy(d => d)
    .ToList();

if (vectorDates.Count > 0)
{
    Console.WriteLine($"Vector coverage: {vectorDates.First():yyyy-MM-dd} -> {vectorDates.Last():yyyy-MM-dd}");
}
var finder = new RecurringPatternFinder();
var patterns = finder.Find(exchanges, cachedEmbeddings, maxAnchors: 150, maxMatches: 6, minimumSimilarity: 0.60, minimumDaysApart: 2);

foreach (var pattern in patterns.Take(10))
{
    Console.WriteLine();
    Console.WriteLine($"Score: {pattern.Score:F2} | Span: {pattern.SpanDays} days | Conversations: {pattern.DistinctConversations}");
    Console.WriteLine($"ANCHOR: {Clip(pattern.Anchor.UserText)}");
    foreach (var match in pattern.Matches.Take(4))
        Console.WriteLine($"MATCH {match.Similarity:F3} | {match.Exchange.StartedAt:yyyy-MM-dd} | {Clip(match.Exchange.UserText)}");
    Console.WriteLine(new string('=',70));
}

Console.WriteLine();
Console.WriteLine("PATTERN CHANGES");
Console.WriteLine("---------------");

var changeDetector = new PatternChangeDetector();
var changes = changeDetector.Detect(patterns, cachedEmbeddings);

foreach (var change in changes.Take(10))
{
    Console.WriteLine();
    Console.WriteLine($"Shift: {change.SemanticShift:F3} | Span: {change.DaysBetween} days");
    Console.WriteLine($"EARLY: {Clip(change.EarliestText)}");
    Console.WriteLine($"LATE:  {Clip(change.LatestText)}");
    Console.WriteLine(new string('=', 70));
}


Console.WriteLine();
Console.WriteLine("PATTERN LIFECYCLES");
Console.WriteLine("------------------");

var deduplicator = new SemanticPatternDeduplicator();
var deduplicatedPatterns = deduplicator.Deduplicate(patterns, cachedEmbeddings);
Console.WriteLine($"Deduplicated patterns: {deduplicatedPatterns.Count:N0}/{patterns.Count:N0}");

var qualityFilter = new PatternQualityFilter();
var filteredPatterns = qualityFilter.Filter(deduplicatedPatterns);
Console.WriteLine($"Quality patterns: {filteredPatterns.Count:N0}/{patterns.Count:N0}");

var lifecycleDetector = new PatternLifecycleDetector();
var lifecycles = lifecycleDetector.Detect(filteredPatterns);

foreach (var life in lifecycles.Take(15))
{
    Console.WriteLine(
        $"{life.State,-10} | early {life.EarlyCount} -> middle {life.MiddleCount} -> late {life.LateCount} | span {life.Pattern.SpanDays} days");

    Console.WriteLine($"  {Clip(life.Pattern.Anchor.UserText, 180)}");
}

Console.WriteLine();
Console.WriteLine("WITHIN-PATTERN SHIFTS");
Console.WriteLine("---------------------");

var shiftDetector = new WithinPatternShiftDetector();
var shifts = shiftDetector.Detect(filteredPatterns, cachedEmbeddings);

foreach (var shift in shifts.Take(10))
{
    Console.WriteLine();
    Console.WriteLine($"SHIFT: {shift.Shift:F3} | SPAN: {shift.SpanDays} days");
    Console.WriteLine($"EARLY: {Clip(shift.EarlyRepresentative.UserText, 300)}");
    Console.WriteLine($"LATE:  {Clip(shift.LateRepresentative.UserText, 300)}");
    Console.WriteLine(new string('=', 70));
}

Console.WriteLine();
Console.WriteLine("PHRASE CONTRAST");
Console.WriteLine("---------------");

var contrastDetector = new PhraseContrastDetector();

foreach (var shift in shifts.Take(10))
{
    var members = shift.Pattern.Matches
        .Select(m => m.Exchange)
        .Append(shift.Pattern.Anchor)
        .Where(e => e.StartedAt is not null)
        .OrderBy(e => e.StartedAt)
        .ToList();

    var split = members.Count / 2;

    var contrast = contrastDetector.Compare(
        members.Take(split).Select(e => e.UserText),
        members.Skip(split).Select(e => e.UserText));

    Console.WriteLine();
    Console.WriteLine($"SHIFT: {shift.Shift:F3}");
    Console.WriteLine($"FADING:   {string.Join(", ", contrast.Fading)}");
    Console.WriteLine($"EMERGING: {string.Join(", ", contrast.Emerging)}");
    Console.WriteLine(new string('=', 70));
}

Console.WriteLine();
Console.WriteLine("SEMANTIC CONTRAST");
Console.WriteLine("-----------------");

var semanticContrast = new SemanticContrastDetector(embeddingService);

foreach (var shift in shifts.Take(5))
{
    var members = shift.Pattern.Matches
        .Select(m => m.Exchange)
        .Append(shift.Pattern.Anchor)
        .Where(e => e.StartedAt is not null && cachedEmbeddings.ContainsKey(e.Id))
        .OrderBy(e => e.StartedAt)
        .ToList();

    var split = members.Count / 2;

    if (split < 1 || members.Count - split < 1)
        continue;

    var earlyMembers = members.Take(split).ToList();
    var lateMembers = members.Skip(split).ToList();

    var earlyCentroid = MakeCentroid(
        earlyMembers.Select(e => cachedEmbeddings[e.Id]));

    var lateCentroid = MakeCentroid(
        lateMembers.Select(e => cachedEmbeddings[e.Id]));

    var contrast = await semanticContrast.CompareAsync(
        earlyMembers.Select(e => e.UserText),
        lateMembers.Select(e => e.UserText),
        earlyCentroid,
        lateCentroid);

    Console.WriteLine();
    Console.WriteLine($"SHIFT: {shift.Shift:F3}");
    Console.WriteLine("EARLY:");
    foreach (var signal in contrast.EarlySignals)
        Console.WriteLine($"  - {signal}");

    Console.WriteLine("LATE:");
    foreach (var signal in contrast.LateSignals)
        Console.WriteLine($"  - {signal}");

    Console.WriteLine(new string('=', 70));
}

Console.WriteLine();
Console.WriteLine("CROSS-PATTERN CONVERGENCE");
Console.WriteLine("-------------------------");

var convergenceDetector = new CrossPatternConvergenceDetector();
var convergenceGroups = convergenceDetector.Detect(shifts, cachedEmbeddings);

foreach (var group in convergenceGroups.Take(10))
{
    Console.WriteLine();
    Console.WriteLine($"GROUP SCORE: {group.Score:F3} | AVG DIRECTION SIM: {group.AverageSimilarity:F3} | MEMBERS: {group.Members.Count}");

    foreach (var member in group.Members)
    {
        Console.WriteLine();
        Console.WriteLine($"SHIFT {member.Shift.Shift:F3} | {member.Shift.SpanDays} days");
        Console.WriteLine($"EARLY: {Clip(member.Shift.EarlyRepresentative.UserText, 220)}");
        Console.WriteLine($"LATE:  {Clip(member.Shift.LateRepresentative.UserText, 220)}");
    }

    Console.WriteLine(new string('=', 70));
}

Console.WriteLine();
Console.WriteLine("TEMPORAL DRIFT");
Console.WriteLine("--------------");

var temporalDriftDetector = new TemporalDriftDetector();
var drifts = temporalDriftDetector.Detect(exchanges, cachedEmbeddings, windowDays: 14);

foreach (var drift in drifts.Take(5))
{
    Console.WriteLine();
    Console.WriteLine($"DRIFT: {drift.Drift:F3}");
    Console.WriteLine($"EARLY WINDOW: {drift.EarlyStart:yyyy-MM-dd} -> {drift.EarlyEnd:yyyy-MM-dd}");

    foreach (var exchange in drift.EarlyContributors)
        Console.WriteLine($"  EARLY: {Clip(exchange.UserText, 220)}");

    Console.WriteLine($"LATE WINDOW:  {drift.LateStart:yyyy-MM-dd} -> {drift.LateEnd:yyyy-MM-dd}");

    foreach (var exchange in drift.LateContributors)
        Console.WriteLine($"  LATE:  {Clip(exchange.UserText, 220)}");

    Console.WriteLine(new string('=', 70));
}

Console.WriteLine();
Console.WriteLine("FINDING CANDIDATES");
Console.WriteLine("------------------");

var findingBuilder = new FindingCandidateBuilder();
var findingCandidates = findingBuilder.Build(drifts);

foreach (var finding in findingCandidates.Take(5))
{
    Console.WriteLine();
    Console.WriteLine(
        $"CANDIDATE SCORE: {finding.Score:F3} | DRIFT: {finding.Drift:F3} | CONVERSATIONS: {finding.DistinctConversations}");

    Console.WriteLine(
        $"EARLY: {finding.EarlyStart:yyyy-MM-dd} -> {finding.EarlyEnd:yyyy-MM-dd}");

    foreach (var exchange in finding.EarlyEvidence.Take(3))
        Console.WriteLine($"  - {Clip(exchange.UserText, 240)}");

    Console.WriteLine(
        $"LATE:  {finding.LateStart:yyyy-MM-dd} -> {finding.LateEnd:yyyy-MM-dd}");

    foreach (var exchange in finding.LateEvidence.Take(3))
        Console.WriteLine($"  + {Clip(exchange.UserText, 240)}");

    Console.WriteLine(new string('=', 70));
}

Console.WriteLine();
Console.WriteLine("FINDING SIGNALS");
Console.WriteLine("---------------");

var findingSignalExtractor = new FindingSignalExtractor(embeddingService);
var findingSignals = await findingSignalExtractor.ExtractAsync(findingCandidates);

foreach (var finding in findingSignals.Take(5))
{
    Console.WriteLine();
    Console.WriteLine($"SCORE: {finding.Candidate.Score:F3}");

    Console.WriteLine("EARLY SIGNALS:");
    foreach (var signal in finding.EarlySignals)
        Console.WriteLine($"  - {signal}");

    Console.WriteLine("LATE SIGNALS:");
    foreach (var signal in finding.LateSignals)
        Console.WriteLine($"  + {signal}");

    Console.WriteLine(new string('=', 70));
}


Console.WriteLine();
Console.WriteLine("FINAL FINDINGS");
Console.WriteLine("--------------");

var reportBuilder = new FindingReportBuilder();
var reports = reportBuilder.Build(findingSignals);

foreach (var report in reports.Take(5))
{
    Console.WriteLine();
    Console.WriteLine($"SCORE: {report.Score:F3}");
    Console.WriteLine($"HIDDEN CHANGE: {report.HiddenChange}");
    Console.WriteLine($"NEXT CONSEQUENCE: {report.NextObservableConsequence}");

    Console.WriteLine("EVIDENCE:");
    foreach (var evidence in report.EarlyEvidence.Take(2))
        Console.WriteLine($"  EARLY - {evidence}");

    foreach (var evidence in report.LateEvidence.Take(2))
        Console.WriteLine($"  LATE  + {evidence}");

    Console.WriteLine(new string('=', 70));
}

Console.WriteLine();
Console.WriteLine("ABSTRACT FINDINGS");
Console.WriteLine("-----------------");

var abstractionBuilder = new FindingAbstractionBuilder();
var abstractFindings = abstractionBuilder.Build(findingSignals);

foreach (var finding in abstractFindings.Take(5))
{
    Console.WriteLine();
    Console.WriteLine($"SCORE: {finding.Score:F3}");
    Console.WriteLine($"EARLY MODE: {finding.EarlyMode}");
    Console.WriteLine($"LATE MODE:  {finding.LateMode}");
    Console.WriteLine($"CHANGE:     {finding.HiddenChange}");

    Console.WriteLine("SUPPORT:");
    foreach (var evidence in finding.EarlyEvidence.Take(2))
        Console.WriteLine($"  < {evidence}");

    foreach (var evidence in finding.LateEvidence.Take(2))
        Console.WriteLine($"  > {evidence}");

    Console.WriteLine(new string('=', 70));
}


Console.WriteLine();
Console.WriteLine("CONVERSATION FUNCTION SHIFTS");
Console.WriteLine("----------------------------");

var functionProfiler = new ConversationFunctionProfiler(embeddingService);
var functionalFindings = await functionProfiler.ProfileAsync(findingSignals);

foreach (var finding in functionalFindings.Take(5))
{
    Console.WriteLine();
    Console.WriteLine($"SCORE: {finding.Score:F3}");

    Console.WriteLine("EARLY FUNCTIONS:");
    foreach (var item in finding.EarlyFunctions)
        Console.WriteLine($"  - {item.Function} [{item.Score:F3}]");

    Console.WriteLine("LATE FUNCTIONS:");
    foreach (var item in finding.LateFunctions)
        Console.WriteLine($"  + {item.Function} [{item.Score:F3}]");

    Console.WriteLine("RISING:");
    foreach (var item in finding.RisingFunctions)
        Console.WriteLine($"  ? {item.Function} [+{item.Score:F3}]");

    Console.WriteLine("FALLING:");
    foreach (var item in finding.FallingFunctions)
        Console.WriteLine($"  ? {item.Function} [-{item.Score:F3}]");

    Console.WriteLine(new string('=', 70));
}

Console.WriteLine();
Console.WriteLine("LONGITUDINAL FINDINGS");
Console.WriteLine("---------------------");

var longitudinalBuilder = new LongitudinalFindingBuilder();
var longitudinalFindings = longitudinalBuilder.Build(functionalFindings);

foreach (var finding in longitudinalFindings.Take(5))
{
    Console.WriteLine();
    Console.WriteLine($"SCORE: {finding.Score:F3}");
    Console.WriteLine($"HIDDEN CHANGE: {finding.HiddenChange}");

    Console.WriteLine("SIGNALS:");
    foreach (var signal in finding.Signals)
        Console.WriteLine($"  + {signal}");

    Console.WriteLine($"NEXT OBSERVABLE CONSEQUENCE: {finding.NextObservableConsequence}");
    Console.WriteLine(new string('=', 70));
}

Console.WriteLine();
Console.WriteLine("EVIDENCE INDEPENDENCE");
Console.WriteLine("---------------------");

var independenceScorer = new EvidenceIndependenceScorer();
var independentFindings = independenceScorer.Score(
    functionalFindings,
    cachedEmbeddings);

foreach (var item in independentFindings.Take(5))
{
    Console.WriteLine();
    Console.WriteLine(
        $"FINAL SCORE: {item.FinalScore:F3} | ORIGINAL: {item.OriginalScore:F3}");

    Console.WriteLine(
        $"INDEPENDENCE: {item.IndependenceScore:F3} | EARLY CONVS: {item.EarlyConversations} | LATE CONVS: {item.LateConversations} | EARLY DIV: {item.EarlyDiversity:F3} | LATE DIV: {item.LateDiversity:F3}");

    foreach (var rising in item.Finding.RisingFunctions.Take(4))
        Console.WriteLine($"  + {rising.Function} [{rising.Score:+0.000;-0.000;0.000}]");

    Console.WriteLine(new string('=', 70));
}

Console.WriteLine();
Console.WriteLine("STRONGEST FINDINGS");
Console.WriteLine("------------------");

var finalInsightBuilder = new FinalInsightBuilder();
var finalInsights = finalInsightBuilder.Build(independentFindings);

foreach (var insight in finalInsights.Take(5))
{
    Console.WriteLine();
    Console.WriteLine($"CONFIDENCE SCORE: {insight.Score:F3} | INDEPENDENCE: {insight.Independence:F3}");
    Console.WriteLine($"HIDDEN CHANGE: {insight.HiddenChange}");

    Console.WriteLine("CONVERGING SIGNALS:");
    foreach (var signal in insight.Signals)
        Console.WriteLine($"  + {signal}");

    Console.WriteLine($"NEXT OBSERVABLE CONSEQUENCE: {insight.NextObservableConsequence}");
    Console.WriteLine(new string('=', 70));
}

Console.WriteLine();
Console.WriteLine("FUNCTION MOMENTUM");
Console.WriteLine("-----------------");

var trendDetector = new FunctionTrendDetector(embeddingService);
var functionTrends = await trendDetector.DetectAsync(
    exchanges,
    cachedEmbeddings,
    windowDays: 5);

foreach (var trend in functionTrends.Take(10))
{
    Console.WriteLine(
        $"{trend.Function} | CHANGE: {trend.Change:+0.000;-0.000;0.000} | SLOPE: {trend.Slope:+0.000;-0.000;0.000}");
}

Console.WriteLine();
Console.WriteLine("PREDICTIVE FINDINGS");
Console.WriteLine("-------------------");

var predictiveBuilder = new PredictiveInsightBuilder();
var predictiveInsights = predictiveBuilder.Build(
    independentFindings,
    functionTrends);

foreach (var insight in predictiveInsights.Take(5))
{
    Console.WriteLine();
    Console.WriteLine(
        $"SCORE: {insight.Score:F3} | INDEPENDENCE: {insight.Independence:F3} | MOMENTUM: {insight.Momentum:F3}");

    Console.WriteLine($"HIDDEN CHANGE: {insight.HiddenChange}");

    Console.WriteLine("CONVERGING SIGNALS:");
    foreach (var signal in insight.ConvergingSignals)
        Console.WriteLine($"  + {signal}");

    Console.WriteLine("MOMENTUM:");
    foreach (var signal in insight.MomentumSignals)
        Console.WriteLine($"  ? {signal}");

    Console.WriteLine(
        $"NEXT OBSERVABLE CONSEQUENCE: {insight.NextObservableConsequence}");

    Console.WriteLine(new string('=', 70));
}

Console.WriteLine();
Console.WriteLine("FORECAST BACKTEST");
Console.WriteLine("-----------------");

var forecastBacktester = new FunctionForecastBacktester(embeddingService);
var forecastBacktests = await forecastBacktester.RunAsync(
    exchanges,
    cachedEmbeddings,
    windowDays: 5);

foreach (var result in forecastBacktests.Take(15))
{
    Console.WriteLine(
        $"{result.Function} | ACCURACY: {result.Accuracy:P0} | CORRECT: {result.Correct}/{result.Predictions} | AVG PRIOR SLOPE: {result.AveragePredictedSlope:+0.000;-0.000;0.000}");
}

Console.WriteLine();
Console.WriteLine("EVIDENCE EVENTS");
Console.WriteLine("---------------");

var evidenceExtractor = new ChatAnalyzer.Core.Evidence.EvidenceEventExtractor(embeddingService);
var evidenceEvents = await evidenceExtractor.ExtractAsync(
    exchanges,
    cancellationToken: CancellationToken.None);

Console.WriteLine($"Total evidence events: {evidenceEvents.Count:N0}");

foreach (var kind in Enum.GetValues<ChatAnalyzer.Core.Evidence.EvidenceKind>())
{
    var items = evidenceEvents
        .Where(x => x.Kind == kind)
        .OrderByDescending(x => x.Confidence)
        .Take(5)
        .ToList();

    Console.WriteLine();
    Console.WriteLine($"{kind.ToString().ToUpperInvariant()} ({evidenceEvents.Count(x => x.Kind == kind):N0})");

    foreach (var item in items)
    {
        Console.WriteLine(
            $"  [{item.Confidence:F3}] {item.Timestamp:yyyy-MM-dd} | {Clip(item.Text, 220)}");
    }
}
static float[] MakeCentroid(IEnumerable<float[]> vectors)
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
static string Clip(string text, int maxLength = 350)
{
    var cleaned = text.Replace("\r", " ").Replace("\n", " ").Trim();
    return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength] + "...";
}

static IReadOnlyList<Conversation> MergeConversations(
    IEnumerable<Conversation> source)
{
    return source
        .GroupBy(c => c.Id)
        .Select(group =>
        {
            var title = group
                .Select(c => c.Title)
                .FirstOrDefault(t =>
                    !string.IsNullOrWhiteSpace(t) &&
                    !string.Equals(
                        t,
                        "Untitled",
                        StringComparison.OrdinalIgnoreCase))
                ?? "Untitled";

            var createdAt = group
                .Where(c => c.CreatedAt is not null)
                .Select(c => c.CreatedAt)
                .Min();

            var messages = group
                .SelectMany(c => c.Messages)
                .GroupBy(m => m.Id)
                .Select(g => g.First())
                .OrderBy(m => m.CreatedAt ?? DateTimeOffset.MaxValue)
                .ThenBy(m => m.Id)
                .ToList();

            return new Conversation(
                group.Key,
                title,
                createdAt,
                messages);
        })
        .OrderBy(c => c.CreatedAt ?? DateTimeOffset.MaxValue)
        .ToList();
}













































