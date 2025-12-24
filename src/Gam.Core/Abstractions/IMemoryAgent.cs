namespace Gam.Core.Abstractions;

using Gam.Core.Models;

/// <summary>
/// Processes conversation turns into storable memory pages.
/// Runs offline (not in the critical path of user requests).
/// </summary>
public interface IMemoryAgent
{
    /// <summary>
    /// Generate an abstract (summary + headers) for a conversation turn.
    /// </summary>
    Task<MemoryAbstract> GenerateAbstractAsync(
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
