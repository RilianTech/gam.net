using Gam.Core.Abstractions;
using Gam.Core.Agents;
using Gam.Core.Configuration;
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
        services.AddSingleton<IResearchAgent, ResearchAgent>();
        services.AddSingleton<IGamService, GamService>();
        
        // Configure TTL if enabled
        var options = section.Get<GamOptions>();
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
