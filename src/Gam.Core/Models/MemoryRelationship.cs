namespace Gam.Core.Models;

/// <summary>
/// A relationship between two memory pages.
/// Enables multi-hop reasoning and relationship-aware retrieval.
/// </summary>
public record MemoryRelationship
{
    /// <summary>Unique identifier for this relationship.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();
    
    /// <summary>Source page ID (relationship goes FROM this page).</summary>
    public required Guid SourcePageId { get; init; }
    
    /// <summary>Target page ID (relationship goes TO this page).</summary>
    public required Guid TargetPageId { get; init; }
    
    /// <summary>Type of relationship.</summary>
    public required RelationshipType Type { get; init; }
    
    /// <summary>Confidence score (0.0-1.0). Higher = more certain.</summary>
    public float Confidence { get; init; } = 1.0f;
    
    /// <summary>Who created this relationship.</summary>
    public RelationshipCreator CreatedBy { get; init; } = RelationshipCreator.System;
    
    /// <summary>When this relationship was created.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Types of relationships between memory pages.
/// </summary>
public enum RelationshipType
{
    /// <summary>
    /// Topically related memories (share tags, entities, or concepts).
    /// Created by background job based on tag/entity overlap.
    /// </summary>
    RelatesTo,
    
    /// <summary>
    /// Temporal sequence - memories from the same conversation in order.
    /// Created automatically during memorization.
    /// </summary>
    Follows,
    
    /// <summary>
    /// Conflicting information between memories.
    /// Detected by LLM during research integration.
    /// </summary>
    Contradicts,
    
    /// <summary>
    /// Supporting evidence - one memory reinforces another.
    /// Detected by LLM during research integration.
    /// </summary>
    Reinforces,
    
    /// <summary>
    /// Semantically similar memories (high embedding cosine similarity).
    /// Created by background job based on vector similarity ≥0.8.
    /// </summary>
    SimilarTo,
    
    /// <summary>
    /// Temporal proximity - this memory came before another.
    /// Created during memorization to link recent memories.
    /// </summary>
    PrecededBy
}

/// <summary>
/// Who created a relationship.
/// </summary>
public enum RelationshipCreator
{
    /// <summary>Created by system/background job.</summary>
    System,
    
    /// <summary>Created by LLM during research.</summary>
    Llm,
    
    /// <summary>Created explicitly by user.</summary>
    User
}

/// <summary>
/// Extension methods for relationship types.
/// </summary>
public static class RelationshipTypeExtensions
{
    /// <summary>Convert to database string format.</summary>
    public static string ToDbString(this RelationshipType type) => type switch
    {
        RelationshipType.RelatesTo => "RELATES_TO",
        RelationshipType.Follows => "FOLLOWS",
        RelationshipType.Contradicts => "CONTRADICTS",
        RelationshipType.Reinforces => "REINFORCES",
        RelationshipType.SimilarTo => "SIMILAR_TO",
        RelationshipType.PrecededBy => "PRECEDED_BY",
        _ => "RELATES_TO"
    };
    
    /// <summary>Parse from database string format.</summary>
    public static RelationshipType ParseRelationshipType(string? value) => value?.ToUpperInvariant() switch
    {
        "RELATES_TO" => RelationshipType.RelatesTo,
        "FOLLOWS" => RelationshipType.Follows,
        "CONTRADICTS" => RelationshipType.Contradicts,
        "REINFORCES" => RelationshipType.Reinforces,
        "SIMILAR_TO" => RelationshipType.SimilarTo,
        "PRECEDED_BY" => RelationshipType.PrecededBy,
        _ => RelationshipType.RelatesTo
    };
    
    /// <summary>Convert creator to database string format.</summary>
    public static string ToDbString(this RelationshipCreator creator) => creator.ToString().ToLowerInvariant();
    
    /// <summary>Parse creator from database string format.</summary>
    public static RelationshipCreator ParseCreator(string? value) => value?.ToLowerInvariant() switch
    {
        "system" => RelationshipCreator.System,
        "llm" => RelationshipCreator.Llm,
        "user" => RelationshipCreator.User,
        _ => RelationshipCreator.System
    };
}
