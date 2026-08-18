namespace ChatAnalyzer.Core.Embeddings;

public interface IEmbeddingService
{
    int Dimensions { get; }

    Task<float[]> EmbedAsync(
        string text,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
