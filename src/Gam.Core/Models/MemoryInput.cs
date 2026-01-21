namespace Gam.Core.Models;

/// <summary>
/// Input to the MemoryAgent - content to be memorized.
/// </summary>
public record MemoryInput
{
    /// <summary>
    /// Scope identifier for multi-tenancy.
    /// All memories with the same OwnerId are queryable together.
    /// Examples: "user-123", "org-acme/user-bob", "locomo-conv-26"
    /// </summary>
    public required string OwnerId { get; init; }
    
    /// <summary>The content to memorize.</summary>
    public required string Content { get; init; }
    
    /// <summary>When this content was created/observed.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    
    /// <summary>Optional session/conversation ID within the owner's scope.</summary>
    public string? SessionId { get; init; }
    
    /// <summary>Optional sequence number within a session.</summary>
    public int? SequenceNumber { get; init; }
    
    /// <summary>Optional tool calls associated with this content.</summary>
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
