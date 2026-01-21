namespace Gam.Core.Abstractions;

using Gam.Core.Models;

/// <summary>
/// Processes content into storable memory pages.
/// Runs offline (not in the critical path of user requests).
/// </summary>
public interface IMemoryAgent
{
    /// <summary>
    /// Generate an abstract (summary + headers) for content.
    /// </summary>
    Task<MemoryAbstract> GenerateAbstractAsync(
        MemoryInput input, 
        CancellationToken ct = default);
    
    /// <summary>
    /// Create a complete memory page from content.
    /// Includes generating abstract and preparing for storage.
    /// </summary>
    Task<MemoryPage> CreatePageAsync(
        MemoryInput input, 
        CancellationToken ct = default);
}
