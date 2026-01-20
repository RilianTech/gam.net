using System.Threading.Channels;
using Gam.Core.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gam.Core.Services;

/// <summary>
/// Background service that periodically discovers and creates relationships
/// between memories based on semantic similarity and other patterns.
/// 
/// Uses System.Threading.Channels for:
/// - Bounded memory usage (backpressure when consumers are slow)
/// - Concurrent processing of multiple owners
/// - Graceful cancellation
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
            "Relationship background service started (interval: {Interval}, parallelism: {Parallelism}, similarity: {Threshold})", 
            _options.Interval, 
            _options.MaxParallelism,
            _options.MinSemanticSimilarity);

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
        
        // Bounded channel provides backpressure - if consumers are slow, 
        // producer will wait rather than buffering unlimited owner IDs
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(_options.ChannelCapacity)
        {
            SingleWriter = true,
            SingleReader = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        var totalRelationships = 0;
        var ownersProcessed = 0;

        // Start producer - streams owner IDs into the channel
        var producerTask = ProduceOwnerIdsAsync(channel.Writer, ct);

        // Start consumers - process owners in parallel
        var consumerTasks = Enumerable
            .Range(0, _options.MaxParallelism)
            .Select(_ => ConsumeOwnerIdsAsync(channel.Reader, ct, (created, processed) =>
            {
                Interlocked.Add(ref totalRelationships, created);
                Interlocked.Add(ref ownersProcessed, processed);
            }))
            .ToArray();

        // Wait for producer to finish (will complete the channel)
        await producerTask;

        // Wait for all consumers to drain the channel
        await Task.WhenAll(consumerTasks);

        if (totalRelationships > 0)
        {
            _logger.LogInformation(
                "Relationship discovery complete: created {Count} relationships across {Owners} owners", 
                totalRelationships, ownersProcessed);
        }
        else
        {
            _logger.LogDebug("Relationship discovery complete: no new relationships found ({Owners} owners checked)", 
                ownersProcessed);
        }
    }

    private async Task ProduceOwnerIdsAsync(ChannelWriter<string> writer, CancellationToken ct)
    {
        try
        {
            await foreach (var ownerId in _store.StreamOwnerIdsAsync(ct))
            {
                await writer.WriteAsync(ownerId, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error producing owner IDs");
        }
        finally
        {
            writer.Complete();
        }
    }

    private async Task ConsumeOwnerIdsAsync(
        ChannelReader<string> reader, 
        CancellationToken ct,
        Action<int, int> reportProgress)
    {
        try
        {
            await foreach (var ownerId in reader.ReadAllAsync(ct))
            {
                try
                {
                    var created = await _relationshipService.CreateSemanticRelationshipsAsync(
                        ownerId,
                        _options.MinSemanticSimilarity,
                        _options.BatchSize,
                        ct);
                    
                    reportProgress(created, 1);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process relationships for owner {OwnerId}", ownerId);
                    reportProgress(0, 1); // Still count as processed
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected on shutdown
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
    
    /// <summary>
    /// Maximum number of owners to process concurrently.
    /// Higher values increase throughput but use more connections/memory.
    /// </summary>
    public int MaxParallelism { get; set; } = 4;
    
    /// <summary>
    /// Bounded channel capacity for owner ID queue.
    /// Provides backpressure when consumers are slower than the producer.
    /// </summary>
    public int ChannelCapacity { get; set; } = 100;
}
