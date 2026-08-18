using ChatAnalyzer.Core.Models;

namespace ChatAnalyzer.Core.Importing;

public interface IChatImporter
{
    Task<IReadOnlyList<Conversation>> ImportAsync(
        Stream source,
        CancellationToken cancellationToken = default);
}
