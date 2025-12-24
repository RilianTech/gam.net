namespace Gam.Core.Abstractions;

using Gam.Core.Models;

/// <summary>
/// Main entry point for GAM operations.
/// </summary>
public interface IGamService
{
    /// <summary>
    /// Process a conversation turn and store it in memory (offline operation).
    /// </summary>
    Task MemorizeAsync(MemorizeRequest request, CancellationToken ct = default);
    
    /// <summary>
    /// Research memories relevant to a query (online operation).
    /// Returns context to be included in LLM prompts.
    /// </summary>
    Task<MemoryContext> ResearchAsync(ResearchRequest request, CancellationToken ct = default);
    
    /// <summary>
    /// Delete memories matching the filter.
    /// </summary>
    Task ForgetAsync(ForgetRequest request, CancellationToken ct = default);
}
