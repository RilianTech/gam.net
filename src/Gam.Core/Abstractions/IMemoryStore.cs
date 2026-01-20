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
    
    /// <summary>
    /// Get all abstracts for an owner. Used by Deep Research to build memory index.
    /// </summary>
    Task<IReadOnlyList<MemoryAbstract>> GetAbstractsAsync(string ownerId, CancellationToken ct = default);
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
    
    // Access tracking (ADR-0002)
    /// <summary>
    /// Update access tracking for retrieved pages (increments access_count, updates last_accessed_at).
    /// </summary>
    Task UpdateAccessAsync(IEnumerable<Guid> pageIds, CancellationToken ct = default);
    
    // Relationship operations (ADR-0002 Phase 4)
    /// <summary>
    /// Store a relationship between two memory pages.
    /// </summary>
    Task StoreRelationshipAsync(MemoryRelationship relationship, CancellationToken ct = default);
    
    /// <summary>
    /// Store multiple relationships in a batch.
    /// </summary>
    Task StoreRelationshipsAsync(IEnumerable<MemoryRelationship> relationships, CancellationToken ct = default);
    
    /// <summary>
    /// Get all relationships from the given source pages.
    /// </summary>
    Task<IReadOnlyList<MemoryRelationship>> GetRelationshipsFromAsync(
        IEnumerable<Guid> sourcePageIds,
        RelationshipType[]? types = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Get page IDs related to the given source pages (for expansion during retrieval).
    /// </summary>
    /// <param name="sourcePageIds">Pages to find relationships from</param>
    /// <param name="types">Filter by relationship types (null = all types)</param>
    /// <param name="maxPerSource">Maximum related pages per source page</param>
    Task<IReadOnlyList<Guid>> GetRelatedPageIdsAsync(
        IEnumerable<Guid> sourcePageIds,
        RelationshipType[]? types = null,
        int maxPerSource = 3,
        CancellationToken ct = default);
    
    // Relationship discovery methods (for background service)
    
    /// <summary>
    /// Find pairs of memories with high embedding similarity that don't already have SIMILAR_TO relationships.
    /// </summary>
    /// <param name="ownerId">Owner to search within</param>
    /// <param name="minSimilarity">Minimum cosine similarity threshold (0.0-1.0)</param>
    /// <param name="limit">Maximum pairs to return</param>
    Task<IReadOnlyList<(Guid PageId1, Guid PageId2, float Similarity)>> FindSimilarPairsAsync(
        string ownerId,
        float minSimilarity = 0.8f,
        int limit = 100,
        CancellationToken ct = default);
    
    /// <summary>
    /// Get recent memories for an owner, ordered by creation time descending.
    /// Used for temporal relationship creation.
    /// </summary>
    Task<IReadOnlyList<(Guid PageId, DateTimeOffset CreatedAt)>> GetRecentPagesAsync(
        string ownerId,
        int limit = 10,
        CancellationToken ct = default);
    
    /// <summary>
    /// Get all unique owner IDs in the store.
    /// Used by background service to process all owners.
    /// </summary>
    [Obsolete("Use StreamOwnerIdsAsync for better memory efficiency")]
    Task<IReadOnlyList<string>> GetAllOwnerIdsAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Stream unique owner IDs from the store.
    /// More memory-efficient than GetAllOwnerIdsAsync for large datasets.
    /// </summary>
    IAsyncEnumerable<string> StreamOwnerIdsAsync(CancellationToken ct = default);
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
