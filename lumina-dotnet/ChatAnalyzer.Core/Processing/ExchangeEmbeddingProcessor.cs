using ChatAnalyzer.Core.Embeddings;
using ChatAnalyzer.Core.Models;
using ChatAnalyzer.Core.Storage;

namespace ChatAnalyzer.Core.Processing;

public sealed class ExchangeEmbeddingProcessor
{
    private readonly IEmbeddingService _embeddingService;
    private readonly EmbeddingStore _store;

    public ExchangeEmbeddingProcessor(
        IEmbeddingService embeddingService,
        EmbeddingStore store)
    {
        _embeddingService = embeddingService;
        _store = store;
    }

    public async Task ProcessAsync(
        IReadOnlyList<Exchange> exchanges,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _store.InitializeAsync(cancellationToken);

        var selected = limit is null
            ? exchanges
            : exchanges.Take(limit.Value).ToList();

        var created = 0;
        var cached = 0;

        for (var i = 0; i < selected.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var exchange = selected[i];

            var existing = await _store.GetAsync(
                exchange.Id,
                cancellationToken);

            if (existing is not null)
            {
                cached++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(exchange.UserText))
                continue;

            var embedding = await _embeddingService.EmbedAsync(
                exchange.UserText,
                cancellationToken);

            await _store.SaveAsync(
                exchange.Id,
                embedding,
                cancellationToken);

            created++;

            if ((i + 1) % 100 == 0 || i + 1 == selected.Count)
            {
                Console.WriteLine(
                    $"Embeddings: {i + 1:N0}/{selected.Count:N0} | new {created:N0} | cached {cached:N0}");
            }
        }

        var total = await _store.CountAsync(cancellationToken);

        Console.WriteLine($"Stored embeddings: {total:N0}");
    }
}
