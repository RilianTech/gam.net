using Gam.Core.Abstractions;
using OpenAI;
using OpenAI.Embeddings;

namespace Gam.Providers.OpenAI;

/// <summary>
/// OpenAI embedding provider implementation.
/// </summary>
public class OpenAIEmbeddingProvider : IEmbeddingProvider
{
    private readonly OpenAIClient _client;
    private readonly string _model;
    
    public int Dimensions { get; }
    public string Model => _model;

    public OpenAIEmbeddingProvider(
        OpenAIClient client, 
        string model = "text-embedding-3-small",
        int dimensions = 1536)
    {
        _client = client;
        _model = model;
        Dimensions = dimensions;
    }

    public OpenAIEmbeddingProvider(
        string apiKey,
        string model = "text-embedding-3-small",
        int dimensions = 1536)
        : this(new OpenAIClient(apiKey), model, dimensions)
    {
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var embeddingClient = _client.GetEmbeddingClient(_model);
        var options = new EmbeddingGenerationOptions { Dimensions = Dimensions };
        var response = await embeddingClient.GenerateEmbeddingAsync(text, options, ct);
        return response.Value.ToFloats().ToArray();
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IEnumerable<string> texts, 
        CancellationToken ct = default)
    {
        var embeddingClient = _client.GetEmbeddingClient(_model);
        var textList = texts.ToList();
        var options = new EmbeddingGenerationOptions { Dimensions = Dimensions };
        var response = await embeddingClient.GenerateEmbeddingsAsync(textList, options, ct);
        return response.Value.Select(e => e.ToFloats().ToArray()).ToList();
    }
}
