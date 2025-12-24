namespace Gam.Core.Abstractions;

using Gam.Core.Models;

/// <summary>
/// Persistent storage for memory pages and abstracts.
/// </summary>
public interface IMemoryStore
{
    // Page operations
    Task<MemoryPage?> GetPageAsync(Guid pageId, CancellationToken ct = default);
    Task<IReadOnlyList<MemoryPage>> GetPagesAsync(IEnumerable<Guid> pageIds, CancellationToken ct = default);
    Task StorePageAsync(MemoryPage page, CancellationToken ct = default);
    Task DeletePageAsync(Guid pageId, CancellationToken ct = default);
    
    // Abstract operations  
    Task<MemoryAbstract?> GetAbstractAsync(Guid pageId, CancellationToken ct = default);
    Task<IReadOnlyList<MemoryAbstract>> GetAbstractsAsync(IEnumerable<Guid> pageIds, CancellationToken ct = default);
    Task StoreAbstractAsync(MemoryAbstract memoryAbstract, CancellationToken ct = default);
    
    // Bulk operations
    Task StorePageWithAbstractAsync(MemoryPage page, MemoryAbstract memoryAbstract, CancellationToken ct = default);
    Task DeleteByOwnerAsync(string ownerId, CancellationToken ct = default);
    
    // TTL / Cleanup operations
    /// <summary>
    /// Delete memories older than the specified age.
    /// </summary>
    /// <param name="maxAge">Maximum age of memories to keep</param>
    /// <param name="ownerId">Optional: only cleanup for specific owner</param>
    /// <returns>Number of pages deleted</returns>
    Task<int> CleanupExpiredAsync(TimeSpan maxAge, string? ownerId = null, CancellationToken ct = default);
    
    /// <summary>
    /// Delete memories created before the specified date.
    /// </summary>
    Task<int> DeleteBeforeAsync(DateTimeOffset before, string? ownerId = null, CancellationToken ct = default);
    
    // Statistics
    Task<MemoryStats> GetStatsAsync(string ownerId, CancellationToken ct = default);
}

/// <summary>
/// Statistics about stored memories.
/// </summary>
public record MemoryStats
{
    public int TotalPages { get; init; }
    public int TotalTokens { get; init; }
    public DateTimeOffset? OldestPage { get; init; }
    public DateTimeOffset? NewestPage { get; init; }
    public int? ExpiredPages { get; init; }
}
