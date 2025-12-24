using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Gam.Core.Abstractions;

namespace Gam.Providers.Ollama;

/// <summary>
/// Ollama embedding provider for local embedding generation.
/// </summary>
public class OllamaEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly string _baseUrl;
    
    public int Dimensions { get; }
    public string Model => _model;

    public OllamaEmbeddingProvider(
        HttpClient httpClient,
        string model = "nomic-embed-text",
        string baseUrl = "http://localhost:11434",
        int dimensions = 768)
    {
        _httpClient = httpClient;
        _model = model;
        _baseUrl = baseUrl.TrimEnd('/');
        Dimensions = dimensions;
    }

    public OllamaEmbeddingProvider(
        string model = "nomic-embed-text",
        string baseUrl = "http://localhost:11434",
        int dimensions = 768)
        : this(new HttpClient(), model, baseUrl, dimensions)
    {
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var request = new OllamaEmbedRequest
        {
            Model = _model,
            Input = text
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUrl}/api/embed", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(ct);
        
        // Ollama returns embeddings as array of arrays, we take the first one
        return result?.Embeddings?.FirstOrDefault() ?? Array.Empty<float>();
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IEnumerable<string> texts,
        CancellationToken ct = default)
    {
        var textList = texts.ToList();
        var results = new List<float[]>();

        // Ollama's embed endpoint can handle multiple inputs
        var request = new OllamaEmbedBatchRequest
        {
            Model = _model,
            Input = textList
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUrl}/api/embed", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(ct);
        
        if (result?.Embeddings != null)
        {
            results.AddRange(result.Embeddings);
        }

        return results;
    }

    private class OllamaEmbedRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";
        
        [JsonPropertyName("input")]
        public string Input { get; set; } = "";
    }

    private class OllamaEmbedBatchRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";
        
        [JsonPropertyName("input")]
        public List<string> Input { get; set; } = new();
    }

    private class OllamaEmbedResponse
    {
        [JsonPropertyName("embeddings")]
        public List<float[]>? Embeddings { get; set; }
    }
}
