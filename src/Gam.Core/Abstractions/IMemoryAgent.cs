namespace Gam.Core.Abstractions;

using Gam.Core.Models;

/// <summary>
/// Processes conversation turns into storable memory pages.
/// Runs offline (not in the critical path of user requests).
/// </summary>
public interface IMemoryAgent
{
    /// <summary>
    /// Generate an abstract (summary + headers + type + tags) for a conversation turn.
    /// Also returns importance score for the memory page.
    /// </summary>
    Task<AbstractGenerationResult> GenerateAbstractAsync(
        ConversationTurn turn, 
        CancellationToken ct = default);
    
    /// <summary>
    /// Create a complete memory page from a conversation turn.
    /// Includes generating abstract and preparing for storage.
    /// </summary>
    Task<MemoryPage> CreatePageAsync(
        ConversationTurn turn, 
        CancellationToken ct = default);
}

/// <summary>
/// Result of abstract generation, including importance score for the page.
/// </summary>
public record AbstractGenerationResult
{
    /// <summary>The generated abstract with summary, headers, type, and tags.</summary>
    public required MemoryAbstract Abstract { get; init; }
    
    /// <summary>
    /// Importance score (0.0-1.0) determined during abstract generation.
    /// Should be stored on the MemoryPage.
    /// </summary>
    public float Importance { get; init; } = 0.5f;
}
