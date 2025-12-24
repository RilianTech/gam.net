using Gam.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Gam.Providers.Ollama;

/// <summary>
/// Extension methods for registering Ollama provider services.
/// </summary>
public static class OllamaServiceCollectionExtensions
{
    /// <summary>
    /// Add Ollama LLM and embedding providers for local inference.
    /// </summary>
    public static IServiceCollection AddGamOllama(
        this IServiceCollection services,
        string baseUrl = "http://localhost:11434",
        string? llmModel = null,
        string? embeddingModel = null,
        int embeddingDimensions = 768)
    {
        services.AddHttpClient();
        
        services.AddSingleton<ILlmProvider>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
            return new OllamaLlmProvider(httpClient, llmModel ?? "llama3.2", baseUrl);
        });
        
        services.AddSingleton<IEmbeddingProvider>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
            return new OllamaEmbeddingProvider(httpClient, embeddingModel ?? "nomic-embed-text", baseUrl, embeddingDimensions);
        });
        
        return services;
    }
}
