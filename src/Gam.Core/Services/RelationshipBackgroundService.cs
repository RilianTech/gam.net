using Gam.Core.Abstractions;
using Gam.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gam.Core.Services;

/// <summary>
/// Background service that periodically discovers and creates relationships
/// between memories based on semantic similarity and other patterns.
/// 
/// Relationship types created:
/// - SIMILAR_TO: High embedding cosine similarity (≥0.8)
/// - RELATES_TO: Tag/entity overlap (created during memorization, not here)
/// - PRECEDED_BY: Temporal proximity (created during memorization, not here)
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

        _logger.LogInformation(
            "Relationship background service started (interval: {Interval}, similarity threshold: {Threshold})", 
            _options.Interval, 
            _options.MinSemanticSimilarity);

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
        var totalRelationships = 0;

        try
        {
            // Get all owners
            var ownerIds = await _store.GetAllOwnerIdsAsync(ct);
            _logger.LogDebug("Processing {OwnerCount} owners for relationship discovery", ownerIds.Count);

            foreach (var ownerId in ownerIds)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    // Discover semantic similarity relationships
                    var created = await _relationshipService.CreateSemanticRelationshipsAsync(
                        ownerId,
                        _options.MinSemanticSimilarity,
                        _options.BatchSize,
                        ct);
                    
                    totalRelationships += created;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process relationships for owner {OwnerId}", ownerId);
                }
            }

            if (totalRelationships > 0)
            {
                _logger.LogInformation("Relationship discovery complete: created {Count} new relationships", 
                    totalRelationships);
            }
            else
            {
                _logger.LogDebug("Relationship discovery complete: no new relationships found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete relationship discovery");
        }
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

    /// <summary>Minimum cosine similarity for SIMILAR_TO relationships.</summary>
    public float MinSemanticSimilarity { get; set; } = 0.8f;

    /// <summary>Maximum pairs to process per owner per run.</summary>
    public int BatchSize { get; set; } = 100;
    
    /// <summary>Minimum tag overlap to create a RELATES_TO relationship (used during memorization).</summary>
    public int MinTagOverlap { get; set; } = 2;
}
