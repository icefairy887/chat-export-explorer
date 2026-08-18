using ChatAnalyzer.Core.Embeddings;

var model = Path.Combine(
    Environment.CurrentDirectory,
    "Models",
    "all-MiniLM-L6-v2",
    "model.onnx");

var vocab = Path.Combine(
    Environment.CurrentDirectory,
    "Models",
    "all-MiniLM-L6-v2",
    "vocab.txt");

using var embeddings = new MiniLmEmbeddingService(model, vocab);

var a = await embeddings.EmbedAsync("I applied for a systems administrator job.");
var b = await embeddings.EmbedAsync("I submitted an application for an IT infrastructure position.");
var c = await embeddings.EmbedAsync("My cat is sleeping on the couch.");

Console.WriteLine($"Dimensions: {a.Length}");
Console.WriteLine($"Related similarity:   {Cosine(a, b):F4}");
Console.WriteLine($"Unrelated similarity: {Cosine(a, c):F4}");

static double Cosine(float[] a, float[] b)
{
    double dot = 0;
    double aa = 0;
    double bb = 0;

    for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
    {
        dot += a[i] * b[i];
        aa += a[i] * a[i];
        bb += b[i] * b[i];
    }

    return dot / (Math.Sqrt(aa) * Math.Sqrt(bb));
}
