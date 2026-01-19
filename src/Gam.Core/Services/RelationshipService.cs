using Gam.Core.Abstractions;
using Gam.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gam.Core.Services;

/// <summary>
/// Service for managing memory relationships.
/// Handles relationship creation and discovery based on tag/entity overlap.
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
    /// </summary>
    public async Task CreateTagBasedRelationshipsAsync(
        MemoryAbstract newAbstract,
        string ownerId,
        int minOverlap = 2,
        float minConfidence = 0.5f,
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
            
            // Calculate tag overlap
            var overlap = newAbstract.Tags.Intersect(existing.Tags, StringComparer.OrdinalIgnoreCase).Count();
            
            if (overlap >= minOverlap)
            {
                // Calculate confidence based on Jaccard similarity
                var union = newAbstract.Tags.Union(existing.Tags, StringComparer.OrdinalIgnoreCase).Count();
                var confidence = (float)overlap / union;
                
                if (confidence >= minConfidence)
                {
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
                }
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
    /// Expand a set of page IDs with related pages.
    /// Used during Deep Research to find additional relevant context.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> ExpandWithRelatedAsync(
        IEnumerable<Guid> pageIds,
        RelationshipType[]? types = null,
        int maxPerSource = 2,
        CancellationToken ct = default)
    {
        types ??= [RelationshipType.RelatesTo, RelationshipType.Reinforces];
        
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
