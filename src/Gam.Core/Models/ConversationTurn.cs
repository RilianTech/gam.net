namespace Gam.Core.Models;

/// <summary>
/// Input to the MemoryAgent - a conversation turn to be memorized.
/// </summary>
public record ConversationTurn
{
    /// <summary>Owner identifier.</summary>
    public required string OwnerId { get; init; }
    
    /// <summary>User's message.</summary>
    public required string UserMessage { get; init; }
    
    /// <summary>Assistant's response.</summary>
    public required string AssistantMessage { get; init; }
    
    /// <summary>When this turn occurred.</summary>
    public required DateTimeOffset Timestamp { get; init; }
    
    /// <summary>Optional conversation/session ID.</summary>
    public string? ConversationId { get; init; }
    
    /// <summary>Optional turn number within conversation.</summary>
    public int? TurnNumber { get; init; }
    
    /// <summary>Optional tool calls made during this turn.</summary>
    public IReadOnlyList<ToolCallRecord>? ToolCalls { get; init; }
    
    /// <summary>Optional metadata.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Record of a tool call made during a conversation turn.
/// </summary>
public record ToolCallRecord
{
    public required string ToolName { get; init; }
    public required string Arguments { get; init; }
    public required string Result { get; init; }
}
