using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ChatAnalyzer.Desktop.Services;

// Minimal Ollama client stub: attempts to call a local Ollama HTTP endpoint if configured via OLLAMA_URL.
public sealed class OllamaClient : ILLMProvider
{
    private readonly HttpClient _http = new();
    private readonly string _baseUrl;

    public OllamaClient()
    {
        _baseUrl = Environment.GetEnvironmentVariable("OLLAMA_URL") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_baseUrl))
            throw new InvalidOperationException("OLLAMA_URL environment variable is not set. Set it to e.g. http://localhost:11434");
    }

    public async Task<string> GenerateSpiritBoxAsync(IEnumerable<DesktopFinding> findings, CancellationToken cancellationToken = default)
    {
        // Build a compact prompt similar to OpenAI client
        var sb = new StringBuilder();
        sb.AppendLine("Produce short 2-4 word lines, evocative, one per line, no explanation. For each finding, emit 6-12 lines and separate findings with a blank line.");
        sb.AppendLine();
        var idx = 1;
        foreach (var f in findings)
        {
            sb.AppendLine($"Finding {idx}: {f.HiddenChange}");
            sb.AppendLine($"Consequence: {f.Consequence}");
            idx++;
        }

        var payload = new { model = "llama", prompt = sb.ToString(), max_tokens = 400 };
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _http.PostAsync(new System.Uri(new System.Uri(_baseUrl), "/api/generate"), content, cancellationToken);
        var respText = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Ollama request failed: {resp.StatusCode} - {respText}");

        // This is a best-effort parse; Ollama's HTTP API may differ depending on version.
        using var doc = JsonDocument.Parse(respText);
        if (doc.RootElement.TryGetProperty("text", out var textEl))
            return textEl.GetString() ?? string.Empty;

        // fallback: return raw response
        return respText;
    }
}
