using Gam.Core.Abstractions;
using Gam.Core.Models;
using Gam.Core.Services;
using Microsoft.Extensions.Logging;

namespace Gam.Core;

/// <summary>
/// Default implementation of IGamService.
/// Enhanced with ADR-0002 relationship creation during memorization.
/// </summary>
public class GamService : IGamService
{
    private readonly IMemoryAgent _memoryAgent;
    private readonly IResearchAgent _researchAgent;
    private readonly IMemoryStore _store;
    private readonly RelationshipService? _relationshipService;
    private readonly ILogger<GamService> _logger;

    public GamService(
        IMemoryAgent memoryAgent,
        IResearchAgent researchAgent,
        IMemoryStore store,
        ILogger<GamService> logger,
        RelationshipService? relationshipService = null)
    {
        _memoryAgent = memoryAgent;
        _researchAgent = researchAgent;
        _store = store;
        _relationshipService = relationshipService;
        _logger = logger;
    }

    public async Task MemorizeAsync(MemorizeRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Memorizing conversation turn for {OwnerId}", request.Turn.OwnerId);

        // Create the memory page
        var page = await _memoryAgent.CreatePageAsync(request.Turn, ct);
        
        // Generate abstract (includes type, importance, tags from ADR-0002)
        var result = await _memoryAgent.GenerateAbstractAsync(request.Turn, ct);
        
        // Update abstract with correct page ID
        var abstractData = result.Abstract with { PageId = page.Id };
        
        // Copy importance from abstract generation to page (ADR-0002)
        page = page with { Importance = result.Importance };
        
        // Store both
        await _store.StorePageWithAbstractAsync(page, abstractData, ct);
        
        _logger.LogInformation("Stored memory page {PageId} for {OwnerId} (type={Type}, importance={Importance:F2})", 
            page.Id, request.Turn.OwnerId, abstractData.Type, page.Importance);
        
        // === ADR-0002: Create relationships ===
        if (_relationshipService != null)
        {
            try
            {
                // Create tag-based relationships (RELATES_TO)
                if (abstractData.Tags.Count > 0)
                {
                    await _relationshipService.CreateTagBasedRelationshipsAsync(
                        abstractData, 
                        request.Turn.OwnerId, 
                        minOverlap: 2,
                        minConfidence: 0.3f,
                        ct);
                }
                
                // Create temporal relationships (PRECEDED_BY)
                await _relationshipService.CreateTemporalRelationshipsAsync(
                    page.Id,
                    request.Turn.OwnerId,
                    maxPrecedingMemories: 3,
                    ct);
            }
            catch (Exception ex)
            {
                // Don't fail memorization if relationship creation fails
                _logger.LogWarning(ex, "Failed to create relationships for page {PageId}", page.Id);
            }
        }
    }

    public async Task<MemoryContext> ResearchAsync(ResearchRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Researching memories for {OwnerId}: {Query}", 
            request.OwnerId, request.Query);

        var query = new ResearchQuery
        {
            OwnerId = request.OwnerId,
            Query = request.Query,
            RecentContext = request.RecentContext
        };

        var context = await _researchAgent.ResearchAsync(query, request.Options, ct);
        
        _logger.LogInformation("Research complete: {PageCount} pages, {TokenCount} tokens in {Duration}ms",
            context.Pages.Count, context.TotalTokens, context.Duration.TotalMilliseconds);

        return context;
    }

    public async Task ForgetAsync(ForgetRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Forgetting memories for {OwnerId}", request.OwnerId);

        if (request.All)
        {
            await _store.DeleteByOwnerAsync(request.OwnerId, ct);
            _logger.LogInformation("Deleted all memories for {OwnerId}", request.OwnerId);
            return;
        }

        if (request.PageIds is { Count: > 0 })
        {
            foreach (var pageId in request.PageIds)
            {
                await _store.DeletePageAsync(pageId, ct);
            }
            _logger.LogInformation("Deleted {Count} specific pages for {OwnerId}", 
                request.PageIds.Count, request.OwnerId);
        }

        // Note: DeleteBefore date filtering would require additional IMemoryStore method
        // This is a simplified implementation
    }
}
