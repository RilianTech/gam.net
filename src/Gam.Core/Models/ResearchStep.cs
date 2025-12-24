using Gam.Core.Abstractions;

namespace Gam.Core.Models;

/// <summary>
/// A single step in the research process, for monitoring/debugging.
/// </summary>
public record ResearchStep
{
    public required int Iteration { get; init; }
    public required ResearchPhase Phase { get; init; }
    public required string Summary { get; init; }
    public required TimeSpan Duration { get; init; }
    
    // Phase-specific data
    public string? Plan { get; init; }
    public IReadOnlyList<RetrievalResult>? RetrievalResults { get; init; }
    public int? PagesIntegrated { get; init; }
    public bool? ShouldContinue { get; init; }
    public MemoryContext? CurrentContext { get; init; }
}

/// <summary>
/// Phases of the research process.
/// </summary>
public enum ResearchPhase
{
    Plan,
    Search,
    Integrate,
    Reflect
}
