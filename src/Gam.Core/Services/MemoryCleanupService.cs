using Gam.Core.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gam.Core.Services;

/// <summary>
/// Configuration for memory TTL (Time-To-Live) and automatic cleanup.
/// </summary>
public class MemoryTtlOptions
{
    /// <summary>
    /// Whether TTL cleanup is enabled. Default: false
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Maximum age of memories before they are deleted.
    /// Default: 30 days
    /// </summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// How often to run the cleanup job.
    /// Default: 1 hour
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Optional: Only cleanup memories for specific owner IDs.
    /// If null, cleans up all owners.
    /// </summary>
    public string[]? OwnerIds { get; set; }
}

/// <summary>
/// Background service that periodically cleans up expired memories.
/// </summary>
public class MemoryCleanupService : BackgroundService
{
    private readonly IMemoryStore _store;
    private readonly IOptions<MemoryTtlOptions> _options;
    private readonly ILogger<MemoryCleanupService> _logger;

    public MemoryCleanupService(
        IMemoryStore store,
        IOptions<MemoryTtlOptions> options,
        ILogger<MemoryCleanupService> logger)
    {
        _store = store;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.Value;

        if (!opts.Enabled)
        {
            _logger.LogInformation("Memory TTL cleanup is disabled");
            return;
        }

        _logger.LogInformation(
            "Memory TTL cleanup enabled: MaxAge={MaxAge}, Interval={Interval}",
            opts.MaxAge, opts.CleanupInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCleanupAsync(opts, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during memory cleanup");
            }

            await Task.Delay(opts.CleanupInterval, stoppingToken);
        }
    }

    private async Task RunCleanupAsync(MemoryTtlOptions opts, CancellationToken ct)
    {
        var totalDeleted = 0;

        if (opts.OwnerIds?.Length > 0)
        {
            // Cleanup specific owners
            foreach (var ownerId in opts.OwnerIds)
            {
                var deleted = await _store.CleanupExpiredAsync(opts.MaxAge, ownerId, ct);
                totalDeleted += deleted;
            }
        }
        else
        {
            // Cleanup all owners
            totalDeleted = await _store.CleanupExpiredAsync(opts.MaxAge, null, ct);
        }

        if (totalDeleted > 0)
        {
            _logger.LogInformation("TTL cleanup completed: deleted {Count} expired memories", totalDeleted);
        }
        else
        {
            _logger.LogDebug("TTL cleanup completed: no expired memories found");
        }
    }
}
