namespace Gam.Core.Models;

/// <summary>
/// Classification of memory content type for filtered retrieval.
/// </summary>
public enum MemoryType
{
    /// <summary>General discussion without specific action or decision.</summary>
    Conversation,
    
    /// <summary>A choice was made between alternatives.</summary>
    Decision,
    
    /// <summary>User expressed a like/dislike without deciding.</summary>
    Preference,
    
    /// <summary>Factual information was shared or learned.</summary>
    Fact,
    
    /// <summary>A realization or understanding was reached.</summary>
    Insight,
    
    /// <summary>An action item or todo was identified.</summary>
    Task,
    
    /// <summary>Background information, no specific action.</summary>
    Context
}

/// <summary>
/// Extension methods for MemoryType.
/// </summary>
public static class MemoryTypeExtensions
{
    /// <summary>
    /// Parse a string to MemoryType, defaulting to Conversation if invalid.
    /// </summary>
    public static MemoryType ParseMemoryType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return MemoryType.Conversation;
            
        return Enum.TryParse<MemoryType>(value, ignoreCase: true, out var result)
            ? result
            : MemoryType.Conversation;
    }
    
    /// <summary>
    /// Convert to lowercase string for database storage.
    /// </summary>
    public static string ToDbString(this MemoryType type) => type.ToString().ToLowerInvariant();
}
