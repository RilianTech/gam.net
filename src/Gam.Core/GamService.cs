using Gam.Core.Abstractions;
using Gam.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gam.Core;

/// <summary>
/// Default implementation of IGamService.
/// </summary>
public class GamService : IGamService
{
    private readonly IMemoryAgent _memoryAgent;
    private readonly IResearchAgent _researchAgent;
    private readonly IMemoryStore _store;
    private readonly ILogger<GamService> _logger;

    public GamService(
        IMemoryAgent memoryAgent,
        IResearchAgent researchAgent,
        IMemoryStore store,
        ILogger<GamService> logger)
    {
        _memoryAgent = memoryAgent;
        _researchAgent = researchAgent;
        _store = store;
        _logger = logger;
    }

    public async Task MemorizeAsync(MemorizeRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Memorizing conversation turn for {OwnerId}", request.Turn.OwnerId);

        // Create the memory page
        var page = await _memoryAgent.CreatePageAsync(request.Turn, ct);
        
        // Generate abstract
        var abstractData = await _memoryAgent.GenerateAbstractAsync(request.Turn, ct);
        
        // Update abstract with correct page ID
        abstractData = abstractData with { PageId = page.Id };
        
        // Store both
        await _store.StorePageWithAbstractAsync(page, abstractData, ct);
        
        _logger.LogInformation("Stored memory page {PageId} for {OwnerId}", page.Id, request.Turn.OwnerId);
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
