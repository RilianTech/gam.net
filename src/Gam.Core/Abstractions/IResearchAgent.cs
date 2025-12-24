namespace Gam.Core.Abstractions;

using Gam.Core.Models;

/// <summary>
/// Researches memories relevant to a query using iterative retrieval.
/// Runs online (in the critical path of user requests).
/// </summary>
public interface IResearchAgent
{
    /// <summary>
    /// Execute research loop and return relevant memory context.
    /// </summary>
    Task<MemoryContext> ResearchAsync(
        ResearchQuery query, 
        ResearchOptions? options = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Execute research with step-by-step callbacks for monitoring.
    /// </summary>
    IAsyncEnumerable<ResearchStep> ResearchStreamAsync(
        ResearchQuery query, 
        ResearchOptions? options = null,
        CancellationToken ct = default);
}

/// <summary>
/// Options for controlling the research process.
/// </summary>
public record ResearchOptions
{
    /// <summary>Maximum research iterations (default: 5)</summary>
    public int MaxIterations { get; init; } = 5;
    
    /// <summary>Maximum pages to retrieve per iteration (default: 10)</summary>
    public int MaxPagesPerIteration { get; init; } = 10;
    
    /// <summary>Maximum total tokens in final context (default: 8000)</summary>
    public int MaxContextTokens { get; init; } = 8000;
    
    /// <summary>Minimum relevance score to include page (default: 0.3)</summary>
    public float MinRelevanceScore { get; init; } = 0.3f;
}
