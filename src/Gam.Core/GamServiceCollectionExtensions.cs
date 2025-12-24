using Gam.Core.Abstractions;
using Gam.Core.Agents;
using Microsoft.Extensions.DependencyInjection;

namespace Gam.Core;

/// <summary>
/// Extension methods for registering GAM core services.
/// </summary>
public static class GamServiceCollectionExtensions
{
    /// <summary>
    /// Add GAM core services to the service collection.
    /// Requires ILlmProvider, IEmbeddingProvider, IMemoryStore, and retrievers to be registered.
    /// </summary>
    public static IServiceCollection AddGamCore(this IServiceCollection services)
    {
        services.AddSingleton<IMemoryAgent, MemoryAgent>();
        services.AddSingleton<IResearchAgent, ResearchAgent>();
        services.AddSingleton<IGamService, GamService>();
        return services;
    }
}
