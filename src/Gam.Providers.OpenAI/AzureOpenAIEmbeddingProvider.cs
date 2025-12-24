using System.ClientModel;
using Azure.AI.OpenAI;
using Gam.Core.Abstractions;
using OpenAI.Embeddings;

namespace Gam.Providers.OpenAI;

/// <summary>
/// Azure OpenAI embedding provider implementation.
/// </summary>
public class AzureOpenAIEmbeddingProvider : IEmbeddingProvider
{
    private readonly AzureOpenAIClient _client;
    private readonly string _deploymentName;
    
    public int Dimensions { get; }
    public string Model => _deploymentName;

    public AzureOpenAIEmbeddingProvider(
        AzureOpenAIClient client,
        string deploymentName,
        int dimensions = 1536)
    {
        _client = client;
        _deploymentName = deploymentName;
        Dimensions = dimensions;
    }

    public AzureOpenAIEmbeddingProvider(
        string endpoint,
        string apiKey,
        string deploymentName,
        int dimensions = 1536)
        : this(new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey)), deploymentName, dimensions)
    {
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var embeddingClient = _client.GetEmbeddingClient(_deploymentName);
        var options = new EmbeddingGenerationOptions { Dimensions = Dimensions };
        var response = await embeddingClient.GenerateEmbeddingAsync(text, options, ct);
        return response.Value.ToFloats().ToArray();
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IEnumerable<string> texts,
        CancellationToken ct = default)
    {
        var embeddingClient = _client.GetEmbeddingClient(_deploymentName);
        var textList = texts.ToList();
        var options = new EmbeddingGenerationOptions { Dimensions = Dimensions };
        var response = await embeddingClient.GenerateEmbeddingsAsync(textList, options, ct);
        return response.Value.Select(e => e.ToFloats().ToArray()).ToList();
    }
}
