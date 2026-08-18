using System.IO;
using ChatAnalyzer.Core.Analysis;
using ChatAnalyzer.Core.Embeddings;
using ChatAnalyzer.Core.Evidence;
using ChatAnalyzer.Core.Importing;
using ChatAnalyzer.Core.Models;
using ChatAnalyzer.Core.Processing;
using ChatAnalyzer.Core.Storage;

namespace ChatAnalyzer.Desktop;

public sealed record DesktopFinding(
    double Score,
    string HiddenChange,
    string Consequence,
    IReadOnlyList<string> Signals,
    IReadOnlyList<ChatAnalyzer.Core.Models.Exchange> Evidence
);

public sealed record DesktopTimelineEvent(
    string Id,
    DateTimeOffset? Timestamp,
    string Kind,
    string Text,
    double Confidence,
    ChatAnalyzer.Core.Models.Exchange Source
);

public sealed record DesktopAnalysisResult(
    int Conversations,
    int Messages,
    int Exchanges,
    IReadOnlyList<DesktopTimelineEvent> Events,
    IReadOnlyList<DesktopFinding> Findings
);

public sealed class DesktopAnalyzerService
{
    public async Task<DesktopAnalysisResult> AnalyzeAsync(
        IReadOnlyList<string> files,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("Importing conversations...");

        var importer = new ChatGptExportImporter();
        var allConversations = new List<Conversation>();

        foreach (var file in files)
        {
            await using var stream = File.OpenRead(file);

            var imported = await importer.ImportAsync(
                stream,
                cancellationToken);

            allConversations.AddRange(imported);
        }

        progress?.Report("Merging exports...");

        var conversations = allConversations
            .GroupBy(c => c.Id)
            .Select(group =>
            {
                var first = group.First();

                var messages = group
                    .SelectMany(c => c.Messages)
                    .GroupBy(m => m.Id)
                    .Select(g => g.First())
                    .OrderBy(m => m.CreatedAt)
                    .ToList();

                return new Conversation(
                    first.Id,
                    first.Title,
                    first.CreatedAt,
                    messages);
            })
            .OrderBy(c => c.CreatedAt)
            .ToList();

        var messageCount = conversations.Sum(c => c.Messages.Count);

        progress?.Report("Building conversation timeline...");

        var exchangeBuilder = new ExchangeBuilder();
        var exchanges = exchangeBuilder.Build(conversations);

        if (exchanges.Count == 0)
        {
            return new DesktopAnalysisResult(
                conversations.Count,
                messageCount,
                0,
                [],
                []);
        }

        var repoRoot = FindRepoRoot();

        var modelPath = Path.Combine(
            repoRoot,
            "Models",
            "all-MiniLM-L6-v2",
            "model.onnx");

        var vocabPath = Path.Combine(
            repoRoot,
            "Models",
            "all-MiniLM-L6-v2",
            "vocab.txt");

        if (!File.Exists(modelPath) || !File.Exists(vocabPath))
            throw new FileNotFoundException(
                "MiniLM model files were not found.");

        progress?.Report("Preparing semantic index...");

        using var embeddingService = new MiniLmEmbeddingService(
            modelPath,
            vocabPath);

        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Lumina");
        Directory.CreateDirectory(dataDirectory);
        var databasePath = Path.Combine(dataDirectory, "analyzer.db");
        var store = new EmbeddingStore(databasePath);

        await store.InitializeAsync(cancellationToken);

        var cachedEmbeddings =
            await store.GetAllAsync(cancellationToken);

        var timelineSample = TimelineSampler.Sample(
            exchanges,
            targetCount: 1200);

        var missing = timelineSample
            .Where(e => !cachedEmbeddings.ContainsKey(e.Id))
            .ToList();

        if (missing.Count > 0)
        {
            progress?.Report(
                $"Indexing {missing.Count:N0} new timeline samples...");

            var processor = new ExchangeEmbeddingProcessor(
                embeddingService,
                store);

            await processor.ProcessAsync(
                missing,
                cancellationToken: cancellationToken);

            cachedEmbeddings =
                await store.GetAllAsync(cancellationToken);
        }

        progress?.Report("Extracting persistent timeline events...");

        var evidenceExtractor = new EvidenceEventExtractor(embeddingService);
        var evidenceEvents = await evidenceExtractor.ExtractAsync(exchanges, cancellationToken);
        var eventStore = new EvidenceEventStore(databasePath);
        await eventStore.InitializeAsync(cancellationToken);
        await eventStore.SaveAsync(evidenceEvents, cancellationToken);

        var timelineEvents = evidenceEvents
            .Select(x => new DesktopTimelineEvent(
                x.Id,
                x.Timestamp,
                x.Kind.ToString(),
                x.Text,
                x.Confidence,
                x.Source))
            .ToList();

        progress?.Report("Finding longitudinal changes...");

        var driftDetector = new TemporalDriftDetector();

        var drifts = driftDetector.Detect(
            exchanges,
            cachedEmbeddings,
            windowDays: 14);

        var candidateBuilder = new FindingCandidateBuilder();
        var candidates = candidateBuilder.Build(drifts);

        if (candidates.Count == 0)
        {
            return new DesktopAnalysisResult(
                conversations.Count,
                messageCount,
                exchanges.Count,
                timelineEvents,
                []);
        }

        progress?.Report("Comparing behavioral signals...");

        var signalExtractor =
            new FindingSignalExtractor(embeddingService);

        var findingSignals =
            await signalExtractor.ExtractAsync(
                candidates,
                cancellationToken);

        var functionProfiler =
            new ConversationFunctionProfiler(embeddingService);

        var functionalFindings =
            await functionProfiler.ProfileAsync(
                findingSignals,
                cancellationToken);

        var independenceScorer =
            new EvidenceIndependenceScorer();

        var independentFindings =
            independenceScorer.Score(
                functionalFindings,
                cachedEmbeddings);

        progress?.Report("Checking momentum...");

        var trendDetector =
            new FunctionTrendDetector(embeddingService);

        var trends = await trendDetector.DetectAsync(
            exchanges,
            cachedEmbeddings,
            windowDays: 5,
            cancellationToken);

        var predictiveBuilder =
            new PredictiveInsightBuilder();

        var predictive =
            predictiveBuilder.Build(
                independentFindings,
                trends);

        var findings = predictive
            .Take(7)
            .Select(x => new DesktopFinding(
                x.Score,
                x.HiddenChange,
                x.NextObservableConsequence,
                x.ConvergingSignals,
                x.SupportingEvidence))
            .ToList();

        progress?.Report("Done.");

        return new DesktopAnalysisResult(
            conversations.Count,
            messageCount,
            exchanges.Count,
            timelineEvents,
            findings);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(
            AppContext.BaseDirectory);

        while (current is not null)
        {
            var model = Path.Combine(
                current.FullName,
                "Models",
                "all-MiniLM-L6-v2",
                "model.onnx");

            if (File.Exists(model))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the ChatAnalyzerDotNet repository root.");
    }
}

