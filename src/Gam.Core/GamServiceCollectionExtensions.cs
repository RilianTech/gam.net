using Gam.Core.Abstractions;
using Gam.Core.Agents;
using Gam.Core.Configuration;
using Gam.Core.Models;
using Gam.Core.Prompts;
using Gam.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gam.Core;

/// <summary>
/// Extension methods for registering GAM core services.
/// </summary>
public static class GamServiceCollectionExtensions
{
    /// <summary>
    /// Add GAM core services to the service collection.
    /// Uses simple research by default. Call AddGamDeepResearch() for the full GAM research loop.
    /// Requires ILlmProvider, IEmbeddingProvider, IMemoryStore, and retrievers to be registered.
    /// </summary>
    public static IServiceCollection AddGamCore(this IServiceCollection services)
    {
        services.AddSingleton<IPromptProvider, DefaultPromptProvider>();
        services.AddSingleton<IMemoryAgent, MemoryAgent>();
        services.AddSingleton<IResearchAgent, ResearchAgent>();
        services.AddSingleton<IGamService, GamService>();
        return services;
    }

    /// <summary>
    /// Add GAM core services with Deep Research enabled (ADR-1).
    /// This is the full GAM implementation with Plan→Search→Integrate→Reflect loop.
    /// Enhanced with ADR-0002 relationship support.
    /// </summary>
    public static IServiceCollection AddGamCoreWithDeepResearch(
        this IServiceCollection services,
        Action<DeepResearchOptions>? configureOptions = null,
        bool enableRelationships = true)
    {
        var options = new DeepResearchOptions();
        configureOptions?.Invoke(options);
        
        services.AddSingleton(options);
        services.AddSingleton<IPromptProvider, DefaultPromptProvider>();
        services.AddSingleton<IMemoryAgent, MemoryAgent>();
        
        // ADR-0002: Add relationship service for tag-based relationships
        if (enableRelationships)
        {
            services.AddSingleton<RelationshipService>();
        }
        
        services.AddSingleton<IResearchAgent, DeepResearchAgent>();
        services.AddSingleton<IGamService, GamService>();
        return services;
    }
    
    /// <summary>
    /// Add the relationship background service for automatic relationship discovery.
    /// </summary>
    public static IServiceCollection AddGamRelationshipBackgroundService(
        this IServiceCollection services,
        Action<RelationshipBackgroundOptions>? configure = null)
    {
        if (configure != null)
        {
            services.Configure(configure);
        }
        services.AddHostedService<RelationshipBackgroundService>();
        return services;
    }

    /// <summary>
    /// Add GAM core services with research options from configuration.
    /// </summary>
    public static IServiceCollection AddGamCore(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = GamOptions.SectionName)
    {
        var section = configuration.GetSection(sectionName);
        services.Configure<GamOptions>(section);
        
        // Configure prompt options from the Prompts subsection
        var promptSection = section.GetSection("Prompts");
        services.Configure<PromptOptions>(promptSection);
        
        services.AddSingleton<IPromptProvider, DefaultPromptProvider>();
        services.AddSingleton<IMemoryAgent, MemoryAgent>();
        
        // Check if Deep Research is enabled in config
        var options = section.Get<GamOptions>();
        if (options?.Research.UseDeepResearch == true)
        {
            var deepOptions = new DeepResearchOptions
            {
                MaxIterations = options.Research.MaxIterations,
                MaxContextTokens = options.Research.MaxContextTokens,
                MinRelevanceScore = options.Research.MinRelevanceScore,
                MaxHitsPerIteration = options.Research.MaxPagesPerIteration
            };
            services.AddSingleton(deepOptions);
            services.AddSingleton<IResearchAgent, DeepResearchAgent>();
        }
        else
        {
            services.AddSingleton<IResearchAgent, ResearchAgent>();
        }
        
        services.AddSingleton<IGamService, GamService>();
        
        // Configure TTL if enabled
        if (options?.Ttl.Enabled == true)
        {
            services.AddGamMemoryTtl(opts =>
            {
                opts.Enabled = true;
                opts.MaxAge = TimeSpan.FromDays(options.Ttl.MaxAgeDays);
                opts.CleanupInterval = TimeSpan.FromHours(options.Ttl.CleanupIntervalHours);
            });
        }
        
        return services;
    }

    /// <summary>
    /// Add memory TTL (Time-To-Live) cleanup as a background service.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configure">Configuration action for TTL options</param>
    public static IServiceCollection AddGamMemoryTtl(
        this IServiceCollection services,
        Action<MemoryTtlOptions> configure)
    {
        services.Configure(configure);
        services.AddHostedService<MemoryCleanupService>();
        return services;
    }

    /// <summary>
    /// Add memory TTL with default 30-day expiration.
    /// </summary>
    public static IServiceCollection AddGamMemoryTtl(
        this IServiceCollection services,
        TimeSpan maxAge)
    {
        return services.AddGamMemoryTtl(opts =>
        {
            opts.Enabled = true;
            opts.MaxAge = maxAge;
        });
    }
}
