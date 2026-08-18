using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.Tokenizers;

namespace ChatAnalyzer.Core.Embeddings;

public sealed class MiniLmEmbeddingService : IEmbeddingService, IDisposable
{
    private const int MaxTokens = 256;

    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;

    public int Dimensions => 384;

    public MiniLmEmbeddingService(string modelPath, string vocabPath)
    {
        _session = new InferenceSession(modelPath);

        _tokenizer = BertTokenizer.Create(
            vocabPath,
            new BertOptions
            {
                LowerCaseBeforeTokenization = true
            });
    }

    public Task<float[]> EmbedAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult(new float[Dimensions]);

        var tokenIds = _tokenizer.EncodeToIds(
            text,
            MaxTokens,
            true,
            out _,
            out _);

        var ids = tokenIds
            .Select(id => (long)id)
            .ToArray();

        var attention = Enumerable
            .Repeat(1L, ids.Length)
            .ToArray();

        var tokenTypes = new long[ids.Length];

        long[] shape = [1, ids.Length];

        using var inputIds =
            OrtValue.CreateTensorValueFromMemory(ids, shape);

        using var attentionMask =
            OrtValue.CreateTensorValueFromMemory(attention, shape);

        using var tokenTypeIds =
            OrtValue.CreateTensorValueFromMemory(tokenTypes, shape);

        var inputs = new Dictionary<string, OrtValue>
        {
            ["input_ids"] = inputIds,
            ["attention_mask"] = attentionMask
        };

        if (_session.InputNames.Contains("token_type_ids"))
            inputs["token_type_ids"] = tokenTypeIds;

        using var runOptions = new RunOptions();

        using var results = _session.Run(
            runOptions,
            inputs,
            new[] { "sentence_embedding" });

        var embedding = results[0]
            .GetTensorDataAsSpan<float>()
            .ToArray();

        return Task.FromResult(embedding);
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        var results = new List<float[]>(texts.Count);

        foreach (var text in texts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await EmbedAsync(text, cancellationToken));
        }

        return results;
    }

    public void Dispose()
    {
        _session.Dispose();
    }
}
