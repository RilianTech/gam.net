using Gam.Core.Abstractions;
using Gam.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gam.Core.Services;

/// <summary>
/// Service for managing memory relationships.
/// Handles relationship creation and discovery based on tag/entity overlap and semantic similarity.
/// </summary>
public class RelationshipService
{
    private readonly IMemoryStore _store;
    private readonly ILogger<RelationshipService> _logger;
    
    public RelationshipService(IMemoryStore store, ILogger<RelationshipService> logger)
    {
        _store = store;
        _logger = logger;
    }
    
    /// <summary>
    /// Find and create RELATES_TO relationships for a new memory based on tag overlap.
    /// Called after memorization to link new memories to existing ones.
    /// Uses absolute overlap count (not Jaccard) for more relationships.
    /// </summary>
    public async Task CreateTagBasedRelationshipsAsync(
        MemoryAbstract newAbstract,
        string ownerId,
        int minOverlap = 2,
        float minConfidence = 0.3f,  // Used as floor, not filter
        CancellationToken ct = default)
    {
        if (newAbstract.Tags.Count == 0)
        {
            _logger.LogDebug("No tags on new memory, skipping relationship creation");
            return;
        }
        
        // Get all existing abstracts for this owner
        var existingAbstracts = await _store.GetAbstractsAsync(ownerId, ct);
        
        var relationships = new List<MemoryRelationship>();
        
        foreach (var existing in existingAbstracts)
        {
            // Skip self
            if (existing.PageId == newAbstract.PageId)
                continue;
            
            // Calculate tag overlap (case-insensitive)
            var sharedTags = newAbstract.Tags
                .Intersect(existing.Tags, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var overlap = sharedTags.Count;
            
            // Use absolute overlap threshold (≥2 shared tags creates relationship)
            if (overlap >= minOverlap)
            {
                // Confidence is based on overlap count, scaled by tag counts
                // More shared tags = higher confidence, but cap at 1.0
                var avgTagCount = (newAbstract.Tags.Count + existing.Tags.Count) / 2.0f;
                var confidence = Math.Min(1.0f, Math.Max(minConfidence, overlap / avgTagCount));
                
                // Boost confidence for entity tags (they're more meaningful)
                var entityOverlap = sharedTags.Count(t => t.StartsWith("entity:", StringComparison.OrdinalIgnoreCase));
                if (entityOverlap > 0)
                {
                    confidence = Math.Min(1.0f, confidence + (entityOverlap * 0.1f));
                }
                
                // Create bidirectional relationships
                relationships.Add(new MemoryRelationship
                {
                    SourcePageId = newAbstract.PageId,
                    TargetPageId = existing.PageId,
                    Type = RelationshipType.RelatesTo,
                    Confidence = confidence,
                    CreatedBy = RelationshipCreator.System
                });
                
                relationships.Add(new MemoryRelationship
                {
                    SourcePageId = existing.PageId,
                    TargetPageId = newAbstract.PageId,
                    Type = RelationshipType.RelatesTo,
                    Confidence = confidence,
                    CreatedBy = RelationshipCreator.System
                });
                
                _logger.LogTrace("Tag overlap: {Tags} between {Page1} and {Page2}",
                    string.Join(", ", sharedTags), newAbstract.PageId, existing.PageId);
            }
        }
        
        if (relationships.Count > 0)
        {
            await _store.StoreRelationshipsAsync(relationships, ct);
            _logger.LogDebug("Created {Count} tag-based relationships for page {PageId}", 
                relationships.Count, newAbstract.PageId);
        }
    }
    
    /// <summary>
    /// Create PRECEDED_BY relationships for temporal linking.
    /// Links a new memory to recent memories from the same owner.
    /// </summary>
    public async Task CreateTemporalRelationshipsAsync(
        Guid newPageId,
        string ownerId,
        int maxPrecedingMemories = 3,
        CancellationToken ct = default)
    {
        var recentPages = await _store.GetRecentPagesAsync(ownerId, maxPrecedingMemories + 1, ct);
        
        // Skip the first one if it's the new page itself
        var precedingPages = recentPages
            .Where(p => p.PageId != newPageId)
            .Take(maxPrecedingMemories)
            .ToList();
        
        if (precedingPages.Count == 0) return;
        
        var relationships = new List<MemoryRelationship>();
        
        foreach (var (precedingPageId, _) in precedingPages)
        {
            // newPage PRECEDED_BY precedingPage (the new memory came after the preceding one)
            relationships.Add(new MemoryRelationship
            {
                SourcePageId = newPageId,
                TargetPageId = precedingPageId,
                Type = RelationshipType.PrecededBy,
                Confidence = 1.0f,  // Temporal relationships are certain
                CreatedBy = RelationshipCreator.System
            });
        }
        
        await _store.StoreRelationshipsAsync(relationships, ct);
        _logger.LogDebug("Created {Count} temporal relationships for page {PageId}",
            relationships.Count, newPageId);
    }
    
    /// <summary>
    /// Create SIMILAR_TO relationships based on embedding similarity.
    /// Called by background service to discover semantic relationships.
    /// </summary>
    public async Task<int> CreateSemanticRelationshipsAsync(
        string ownerId,
        float minSimilarity = 0.8f,
        int batchSize = 100,
        CancellationToken ct = default)
    {
        var similarPairs = await _store.FindSimilarPairsAsync(ownerId, minSimilarity, batchSize, ct);
        
        if (similarPairs.Count == 0) return 0;
        
        var relationships = new List<MemoryRelationship>();
        
        foreach (var (pageId1, pageId2, similarity) in similarPairs)
        {
            // Create bidirectional SIMILAR_TO relationships
            relationships.Add(new MemoryRelationship
            {
                SourcePageId = pageId1,
                TargetPageId = pageId2,
                Type = RelationshipType.SimilarTo,
                Confidence = similarity,
                CreatedBy = RelationshipCreator.System
            });
            
            relationships.Add(new MemoryRelationship
            {
                SourcePageId = pageId2,
                TargetPageId = pageId1,
                Type = RelationshipType.SimilarTo,
                Confidence = similarity,
                CreatedBy = RelationshipCreator.System
            });
        }
        
        await _store.StoreRelationshipsAsync(relationships, ct);
        _logger.LogInformation("Created {Count} semantic similarity relationships for owner {OwnerId}",
            relationships.Count, ownerId);
        
        return relationships.Count;
    }
    
    /// <summary>
    /// Expand a set of page IDs with related pages.
    /// Used during Deep Research to find additional relevant context.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> ExpandWithRelatedAsync(
        IEnumerable<Guid> pageIds,
        RelationshipType[]? types = null,
        int maxPerSource = 2,
        CancellationToken ct = default)
    {
        // Include new relationship types in expansion
        types ??= [
            RelationshipType.RelatesTo, 
            RelationshipType.Reinforces,
            RelationshipType.SimilarTo,
            RelationshipType.PrecededBy
        ];
        
        var sourceIds = pageIds.ToList();
        var relatedIds = await _store.GetRelatedPageIdsAsync(sourceIds, types, maxPerSource, ct);
        
        // Return only IDs not already in the source set
        var newIds = relatedIds.Except(sourceIds).ToList();
        
        if (newIds.Count > 0)
        {
            _logger.LogDebug("Expanded {SourceCount} pages with {RelatedCount} related pages",
                sourceIds.Count, newIds.Count);
        }
        
        return newIds;
    }
}
