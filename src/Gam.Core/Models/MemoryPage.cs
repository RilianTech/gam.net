namespace Gam.Core.Models;

/// <summary>
/// A stored memory page containing raw conversation content.
/// </summary>
public record MemoryPage
{
    /// <summary>Unique identifier for this page.</summary>
    public required Guid Id { get; init; }
    
    /// <summary>Owner identifier (user ID, agent ID, or session ID).</summary>
    public required string OwnerId { get; init; }
    
    /// <summary>Raw conversation content.</summary>
    public required string Content { get; init; }
    
    /// <summary>Token count of the content.</summary>
    public required int TokenCount { get; init; }
    
    /// <summary>When this memory was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
    
    /// <summary>Optional metadata.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
    
    /// <summary>Embedding vector for semantic search.</summary>
    public float[]? Embedding { get; init; }
}
