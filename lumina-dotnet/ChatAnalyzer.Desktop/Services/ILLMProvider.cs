using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ChatAnalyzer.Desktop.Services;

// Abstraction for LLM providers used by the desktop app.
public interface ILLMProvider
{
    /// <summary>
    /// Given the UI-friendly DesktopFinding collection, produce the spirit-box style plain text output.
    /// </summary>
    Task<string> GenerateSpiritBoxAsync(IEnumerable<DesktopFinding> findings, CancellationToken cancellationToken = default);
}

// Cloud providers can refine locally discovered findings using the original
// evidence packet. The local analyzer remains the source of candidate evidence.
public interface ICloudLongitudinalAnalyzer
{
    Task<IReadOnlyList<DesktopFinding>> AnalyzeLongitudinalAsync(
        DesktopAnalysisResult analysis,
        CancellationToken cancellationToken = default);
}
