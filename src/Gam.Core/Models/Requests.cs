using Gam.Core.Abstractions;

namespace Gam.Core.Models;

/// <summary>
/// Request to memorize a conversation turn.
/// </summary>
public record MemorizeRequest
{
    public required ConversationTurn Turn { get; init; }
    
    /// <summary>If true, process synchronously. If false, queue for background processing.</summary>
    public bool Synchronous { get; init; } = true;
}

/// <summary>
/// Request to research memories.
/// </summary>
public record ResearchRequest
{
    /// <summary>Owner whose memories to search.</summary>
    public required string OwnerId { get; init; }
    
    /// <summary>The query to research.</summary>
    public required string Query { get; init; }
    
    /// <summary>Optional recent conversation context to inform research.</summary>
    public IReadOnlyList<ConversationTurn>? RecentContext { get; init; }
    
    /// <summary>Research options.</summary>
    public ResearchOptions? Options { get; init; }
}

/// <summary>
/// Request to forget/delete memories.
/// </summary>
public record ForgetRequest
{
    public required string OwnerId { get; init; }
    
    /// <summary>If specified, only delete these pages.</summary>
    public IReadOnlyList<Guid>? PageIds { get; init; }
    
    /// <summary>If specified, delete pages before this date.</summary>
    public DateTimeOffset? Before { get; init; }
    
    /// <summary>If true, delete all memories for this owner.</summary>
    public bool All { get; init; } = false;
}

/// <summary>
/// Query for research operations.
/// </summary>
public record ResearchQuery
{
    public required string OwnerId { get; init; }
    public required string Query { get; init; }
    public IReadOnlyList<ConversationTurn>? RecentContext { get; init; }
}
