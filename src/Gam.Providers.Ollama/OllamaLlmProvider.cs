using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gam.Core.Abstractions;

namespace Gam.Providers.Ollama;

/// <summary>
/// Ollama LLM provider for local model inference.
/// </summary>
public class OllamaLlmProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _defaultModel;
    private readonly string _baseUrl;

    public OllamaLlmProvider(
        HttpClient httpClient, 
        string defaultModel = "llama3.2",
        string baseUrl = "http://localhost:11434")
    {
        _httpClient = httpClient;
        _defaultModel = defaultModel;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public OllamaLlmProvider(string defaultModel = "llama3.2", string baseUrl = "http://localhost:11434")
        : this(new HttpClient(), defaultModel, baseUrl)
    {
    }

    public async Task<LlmResponse> CompleteAsync(
        IReadOnlyList<LlmMessage> messages,
        LlmOptions? options = null,
        CancellationToken ct = default)
    {
        var model = options?.Model ?? _defaultModel;
        
        var request = new OllamaChatRequest
        {
            Model = model,
            Messages = messages.Select(m => new OllamaMessage
            {
                Role = m.Role.ToString().ToLowerInvariant(),
                Content = m.Content
            }).ToList(),
            Stream = false,
            Options = new OllamaOptions
            {
                Temperature = options?.Temperature ?? 0.7f,
                NumPredict = options?.MaxTokens
            }
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUrl}/api/chat", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(ct);
        
        return new LlmResponse
        {
            Content = result?.Message?.Content ?? "",
            PromptTokens = result?.PromptEvalCount ?? 0,
            CompletionTokens = result?.EvalCount ?? 0,
            Model = model
        };
    }

    public async IAsyncEnumerable<string> CompleteStreamAsync(
        IReadOnlyList<LlmMessage> messages,
        LlmOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = options?.Model ?? _defaultModel;
        
        var request = new OllamaChatRequest
        {
            Model = model,
            Messages = messages.Select(m => new OllamaMessage
            {
                Role = m.Role.ToString().ToLowerInvariant(),
                Content = m.Content
            }).ToList(),
            Stream = true,
            Options = new OllamaOptions
            {
                Temperature = options?.Temperature ?? 0.7f,
                NumPredict = options?.MaxTokens
            }
        };

        var jsonContent = JsonSerializer.Serialize(request);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/chat")
        {
            Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
        };

        var response = await _httpClient.SendAsync(
            httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null && !ct.IsCancellationRequested)
        {
            if (string.IsNullOrEmpty(line)) continue;

            var chunk = JsonSerializer.Deserialize<OllamaChatResponse>(line);
            if (!string.IsNullOrEmpty(chunk?.Message?.Content))
            {
                yield return chunk.Message.Content;
            }
        }
    }

    private class OllamaChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";
        
        [JsonPropertyName("messages")]
        public List<OllamaMessage> Messages { get; set; } = new();
        
        [JsonPropertyName("stream")]
        public bool Stream { get; set; }
        
        [JsonPropertyName("options")]
        public OllamaOptions? Options { get; set; }
    }

    private class OllamaMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";
        
        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }

    private class OllamaOptions
    {
        [JsonPropertyName("temperature")]
        public float? Temperature { get; set; }
        
        [JsonPropertyName("num_predict")]
        public int? NumPredict { get; set; }
    }

    private class OllamaChatResponse
    {
        [JsonPropertyName("message")]
        public OllamaMessage? Message { get; set; }
        
        [JsonPropertyName("done")]
        public bool Done { get; set; }
        
        [JsonPropertyName("prompt_eval_count")]
        public int PromptEvalCount { get; set; }
        
        [JsonPropertyName("eval_count")]
        public int EvalCount { get; set; }
    }
}
