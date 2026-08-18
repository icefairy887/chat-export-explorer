using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ChatAnalyzer.Desktop.Services;

/// <summary>
/// Azure OpenAI refinement layer. The desktop app sends only the locally
/// selected findings and bounded evidence snippets, not the full archive.
/// </summary>
public sealed class AzureOpenAiClient : ILLMProvider, ICloudLongitudinalAnalyzer
{
    private readonly HttpClient _http = new();
    private readonly string _endpoint;
    private readonly string _deployment;
    private readonly string _apiVersion;

    public AzureOpenAiClient(string? apiKey = null)
    {
        _endpoint = (Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? string.Empty).TrimEnd('/');
        _deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? string.Empty;
        _apiVersion = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_VERSION") ?? "2024-10-21";
        var key = string.IsNullOrWhiteSpace(apiKey)
            ? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
            : apiKey;

        if (string.IsNullOrWhiteSpace(_endpoint))
            throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
        if (string.IsNullOrWhiteSpace(_deployment))
            throw new InvalidOperationException("AZURE_OPENAI_DEPLOYMENT is not set.");
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("AZURE_OPENAI_API_KEY is not set or was not provided.");

        _http.DefaultRequestHeaders.Add("api-key", key);
    }

    public async Task<IReadOnlyList<DesktopFinding>> AnalyzeLongitudinalAsync(
        DesktopAnalysisResult analysis,
        CancellationToken cancellationToken = default)
    {
        var findings = analysis.Findings;
        if (findings.Count == 0 && analysis.Events.Count == 0)
            return findings;

        var packet = BuildEvidencePacket(analysis);
        var schema = new
        {
            type = "object",
            additionalProperties = false,
            properties = new
            {
                findings = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        additionalProperties = false,
                        properties = new
                        {
                            claim = new { type = "string" },
                            next_observable_consequence = new { type = "string" },
                            signals = new { type = "array", items = new { type = "string" } },
                            evidence_event_ids = new { type = "array", items = new { type = "string" } },
                            source_finding_number = new { type = "integer" }
                        },
                        required = new[] { "claim", "next_observable_consequence", "signals", "evidence_event_ids", "source_finding_number" }
                    }
                }
            },
            required = new[] { "findings" }
        };

        var payload = new
        {
            model = _deployment,
            temperature = 0.2,
            max_tokens = 1800,
            response_format = new
            {
                type = "json_schema",
                json_schema = new { name = "longitudinal_findings", strict = true, schema }
            },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You are the cloud longitudinal reasoning layer for a private chat-history analyzer. " +
                        "Rewrite only evidence-supported findings that become visible across time. Reject generic observations, " +
                        "single-date claims, assistant-authored evidence, and conclusions already explicitly stated by the user. " +
                        "Require at least two independent signals from different dates or conversations. " +
                        "Treat education events such as CS50 Cybersecurity as evidence only when the supplied text supports them. " +
                        "Do not invent dates, credentials, skills, or predictions. Return at most 7 strong findings."
                },
                new { role = "user", content = packet }
            }
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var uri = $"{_endpoint}/openai/deployments/{Uri.EscapeDataString(_deployment)}/chat/completions?api-version={Uri.EscapeDataString(_apiVersion)}";
        using var response = await _http.PostAsync(uri, content, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Azure OpenAI request failed: {response.StatusCode} - {responseText}");

        using var responseDocument = JsonDocument.Parse(responseText);
        var jsonText = responseDocument.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(jsonText))
            return findings;

        using var resultDocument = JsonDocument.Parse(jsonText);
        var refined = new List<DesktopFinding>();
        foreach (var item in resultDocument.RootElement.GetProperty("findings").EnumerateArray())
        {
            var sourceNumber = item.GetProperty("source_finding_number").GetInt32();
            var sourceIndex = sourceNumber - 1;
            var source = sourceIndex >= 0 && sourceIndex < findings.Count
                ? findings[sourceIndex]
                : new DesktopFinding(0.5, string.Empty, string.Empty, [], []);
            var signals = item.GetProperty("signals").EnumerateArray()
                .Select(x => x.GetString() ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(6)
                .ToList();

            if (signals.Count < 2)
                continue;

            var evidenceIds = item.GetProperty("evidence_event_ids").EnumerateArray()
                .Select(x => x.GetString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.Ordinal);
            var eventEvidence = analysis.Events
                .Where(x => evidenceIds.Contains(x.Id))
                .Select(x => x.Source)
                .DistinctBy(x => x.Id)
                .ToList();
            var combinedEvidence = eventEvidence.Count > 0 ? eventEvidence : source.Evidence;

            refined.Add(source with
            {
                HiddenChange = item.GetProperty("claim").GetString() ?? source.HiddenChange,
                Consequence = item.GetProperty("next_observable_consequence").GetString() ?? source.Consequence,
                Signals = signals,
                Evidence = combinedEvidence
            });
        }

        return refined.Count > 0 ? refined : findings;
    }

    public async Task<string> GenerateSpiritBoxAsync(
        IEnumerable<DesktopFinding> findings,
        CancellationToken cancellationToken = default)
    {
        var source = findings.ToList();
        var refined = await AnalyzeLongitudinalAsync(
            new DesktopAnalysisResult(0, 0, 0, [], source),
            cancellationToken);
        var lines = refined.SelectMany(f => new[]
        {
            f.HiddenChange,
            f.Consequence
        });
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildEvidencePacket(DesktopAnalysisResult analysis)
    {
        var builder = new StringBuilder();
        builder.AppendLine("PERSISTENT USER TIMELINE EVENTS");
        foreach (var item in SelectTimelineEvents(analysis.Events))
        {
            var date = item.Timestamp?.ToString("yyyy-MM-dd") ?? "unknown-date";
            builder.AppendLine($"EVENT {item.Id} | {date} | {item.Kind} | confidence {item.Confidence:F3} | {item.Text}");
        }
        builder.AppendLine();

        var findings = analysis.Findings;
        for (var i = 0; i < findings.Count; i++)
        {
            var finding = findings[i];
            builder.AppendLine($"FINDING {i + 1}");
            builder.AppendLine($"Local candidate: {finding.HiddenChange}");
            builder.AppendLine($"Local consequence: {finding.Consequence}");
            foreach (var signal in finding.Signals.Take(6))
                builder.AppendLine($"Signal: {signal}");

            foreach (var evidence in finding.Evidence.Take(8))
            {
                var date = (evidence.StartedAt ?? evidence.EndedAt)?.ToString("yyyy-MM-dd") ?? "unknown-date";
                var text = !string.IsNullOrWhiteSpace(evidence.UserText) ? evidence.UserText : string.Empty;
                if (text.Length > 500) text = text[..500] + "…";
                builder.AppendLine($"User evidence | {date} | {text.Replace('\r', ' ').Replace('\n', ' ')}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static IReadOnlyList<DesktopTimelineEvent> SelectTimelineEvents(
        IReadOnlyList<DesktopTimelineEvent> events)
    {
        return events
            .Where(x => x.Timestamp is not null)
            .GroupBy(x => new
            {
                Month = new DateTime(x.Timestamp!.Value.Year, x.Timestamp.Value.Month, 1),
                x.Kind
            })
            .SelectMany(group => group
                .OrderByDescending(x => x.Confidence)
                .Take(5))
            .OrderBy(x => x.Timestamp)
            .Take(350)
            .ToList();
    }
}
