namespace Gam.Core.Models;

/// <summary>
/// A structured summary of a memory page with searchable headers.
/// </summary>
public record MemoryAbstract
{
    /// <summary>ID of the associated page.</summary>
    public required Guid PageId { get; init; }
    
    /// <summary>Owner identifier.</summary>
    public required string OwnerId { get; init; }
    
    /// <summary>Brief summary of the page content.</summary>
    public required string Summary { get; init; }
    
    /// <summary>Searchable headers/topics extracted from content.</summary>
    public required IReadOnlyList<string> Headers { get; init; }
    
    /// <summary>When this abstract was generated.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
    
    /// <summary>Embedding of the summary for semantic search.</summary>
    public float[]? SummaryEmbedding { get; init; }
}
