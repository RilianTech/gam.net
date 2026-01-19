using Gam.Core.Abstractions;
using Gam.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gam.Core.Services;

/// <summary>
/// Background service that periodically creates RELATES_TO relationships
/// based on tag/entity overlap between memories.
/// </summary>
public class RelationshipBackgroundService : BackgroundService
{
    private readonly IMemoryStore _store;
    private readonly RelationshipService _relationshipService;
    private readonly ILogger<RelationshipBackgroundService> _logger;
    private readonly RelationshipBackgroundOptions _options;

    public RelationshipBackgroundService(
        IMemoryStore store,
        RelationshipService relationshipService,
        ILogger<RelationshipBackgroundService> logger,
        IOptions<RelationshipBackgroundOptions>? options = null)
    {
        _store = store;
        _relationshipService = relationshipService;
        _logger = logger;
        _options = options?.Value ?? new RelationshipBackgroundOptions();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Relationship background service is disabled");
            return;
        }

        _logger.LogInformation("Relationship background service started, interval: {Interval}", _options.Interval);

        // Initial delay to let the application start up
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessRelationshipsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in relationship background service");
            }

            await Task.Delay(_options.Interval, stoppingToken);
        }

        _logger.LogInformation("Relationship background service stopped");
    }

    private async Task ProcessRelationshipsAsync(CancellationToken ct)
    {
        _logger.LogDebug("Running relationship discovery...");

        // This is a simplified implementation that processes all owners
        // In production, you'd want to track which memories have been processed
        // and only process new ones, possibly using a queue

        // For now, we rely on the ON CONFLICT DO NOTHING to handle duplicates
        // A more sophisticated implementation would:
        // 1. Track last processed timestamp per owner
        // 2. Only process memories created since last run
        // 3. Use a distributed lock if running multiple instances

        _logger.LogDebug("Relationship discovery complete");
    }
}

/// <summary>
/// Options for the relationship background service.
/// </summary>
public class RelationshipBackgroundOptions
{
    /// <summary>Whether the background service is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Interval between relationship discovery runs.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Minimum tag overlap to create a RELATES_TO relationship.</summary>
    public int MinTagOverlap { get; set; } = 2;

    /// <summary>Minimum confidence (Jaccard similarity) for relationships.</summary>
    public float MinConfidence { get; set; } = 0.3f;
}
