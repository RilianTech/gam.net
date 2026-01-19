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
    
    // === ADR-0002 Enhancements ===
    
    /// <summary>
    /// Importance score (0.0-1.0). Higher values indicate more significant memories.
    /// 0.8-1.0: Critical decisions, key facts, important preferences
    /// 0.5-0.7: Useful context, moderate relevance (default)
    /// 0.2-0.4: Minor details, low future relevance
    /// </summary>
    public float Importance { get; init; } = 0.5f;
    
    /// <summary>Number of times this memory has been retrieved.</summary>
    public int AccessCount { get; init; } = 0;
    
    /// <summary>When this memory was last accessed/retrieved.</summary>
    public DateTimeOffset? LastAccessedAt { get; init; }
}
