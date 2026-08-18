using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace ChatAnalyzer.Desktop.Services;

public sealed class OpenAiClient : ILLMProvider
{
    private readonly HttpClient _http = new();
    private readonly string _apiKey;

    public OpenAiClient()
    {
        _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set.");

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    // Allow passing API key directly from UI for convenience (does not persist)
    public OpenAiClient(string apiKey)
    {
        _apiKey = apiKey ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("OpenAI API key was not provided.");

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    public async Task<string> GenerateSpiritBoxAsync(IEnumerable<DesktopFinding> findings, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a concise extractor that converts analytic findings into 'spirit box' transmissions: very short lines of 2-4 words, evocative, each on its own line. Do not include any explanation or metadata. For each finding, create 6-12 short lines capturing the essence and a likely next observable action. Separate findings with a blank line.");
        sb.AppendLine();

        var idx = 1;
        foreach (var f in findings)
        {
            sb.AppendLine($"Finding {idx}: {f.HiddenChange}");
            sb.AppendLine($"Consequence: {f.Consequence}");
            if (f.Signals != null && f.Signals.Count > 0)
            {
                sb.AppendLine("Signals:");
                foreach (var s in f.Signals.Take(4)) sb.AppendLine("- " + s);
            }

            if (f.Evidence != null && f.Evidence.Count > 0)
            {
                sb.AppendLine("Evidence:");
                foreach (var e in f.Evidence.Take(6))
                {
                    var when = e.StartedAt ?? e.EndedAt;
                    var whenText = when.HasValue ? when.Value.ToString("yyyy-MM-dd") : "unknown";
                    var snippet = (e.UserText ?? e.AssistantText ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ');
                    if (snippet.Length > 120) snippet = snippet.Substring(0, 120) + "…";
                    sb.AppendLine($"* {whenText} | {e.ConversationId} | {snippet}");
                }
            }

            sb.AppendLine();
            idx++;
        }

        var systemMessage = "You will produce only short lines: 2-4 words per line, evocative, no punctuation beyond basic, no numbering, no explanations. Output should be plain text.";

        var request = new
        {
            model = "gpt-3.5-turbo",
            messages = new[]
            {
                new { role = "system", content = systemMessage },
                new { role = "user", content = sb.ToString() }
            },
            max_tokens = 400,
            temperature = 0.9
        };

        var json = JsonSerializer.Serialize(request);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _http.PostAsync("https://api.openai.com/v1/chat/completions", content, cancellationToken);
        var respText = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI request failed: {resp.StatusCode} - {respText}");

        using var doc = JsonDocument.Parse(respText);
        var root = doc.RootElement;
        var choice = root.GetProperty("choices")[0];
        var message = choice.GetProperty("message").GetProperty("content").GetString();
        return message ?? string.Empty;
    }
}
