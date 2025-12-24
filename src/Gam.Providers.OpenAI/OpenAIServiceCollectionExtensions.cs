using System.ClientModel;
using Azure.AI.OpenAI;
using Gam.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

namespace Gam.Providers.OpenAI;

/// <summary>
/// Extension methods for registering OpenAI provider services.
/// </summary>
public static class OpenAIServiceCollectionExtensions
{
    /// <summary>
    /// Add OpenAI LLM and embedding providers.
    /// </summary>
    public static IServiceCollection AddGamOpenAI(
        this IServiceCollection services,
        string apiKey,
        string? model = null,
        string? embeddingModel = null,
        int embeddingDimensions = 1536)
    {
        var client = new OpenAIClient(apiKey);
        
        services.AddSingleton<ILlmProvider>(_ => 
            new OpenAILlmProvider(client, model ?? "gpt-4o"));
        services.AddSingleton<IEmbeddingProvider>(_ => 
            new OpenAIEmbeddingProvider(client, embeddingModel ?? "text-embedding-3-small", embeddingDimensions));
        
        return services;
    }
    
    /// <summary>
    /// Add Azure OpenAI LLM and embedding providers.
    /// </summary>
    public static IServiceCollection AddGamAzureOpenAI(
        this IServiceCollection services,
        string endpoint,
        string apiKey,
        string chatDeployment,
        string embeddingDeployment,
        int embeddingDimensions = 1536)
    {
        var client = new AzureOpenAIClient(
            new Uri(endpoint), 
            new ApiKeyCredential(apiKey));
        
        services.AddSingleton<ILlmProvider>(_ => 
            new AzureOpenAILlmProvider(client, chatDeployment));
        services.AddSingleton<IEmbeddingProvider>(_ => 
            new AzureOpenAIEmbeddingProvider(client, embeddingDeployment, embeddingDimensions));
        
        return services;
    }
}
