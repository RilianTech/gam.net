using System.Text;

namespace Gam.Core.Models;

/// <summary>
/// Result of research - context to be included in LLM prompts.
/// </summary>
public record MemoryContext
{
    /// <summary>Retrieved pages, ordered by relevance.</summary>
    public required IReadOnlyList<RetrievedPage> Pages { get; init; }
    
    /// <summary>Total token count of all pages.</summary>
    public required int TotalTokens { get; init; }
    
    /// <summary>Number of research iterations performed.</summary>
    public required int IterationsPerformed { get; init; }
    
    /// <summary>How long research took.</summary>
    public required TimeSpan Duration { get; init; }
    
    /// <summary>Empty context singleton.</summary>
    public static MemoryContext Empty { get; } = new()
    {
        Pages = Array.Empty<RetrievedPage>(),
        TotalTokens = 0,
        IterationsPerformed = 0,
        Duration = TimeSpan.Zero
    };
    
    /// <summary>
    /// Format context for inclusion in a prompt.
    /// </summary>
    public string FormatForPrompt()
    {
        if (Pages.Count == 0) return string.Empty;
        
        var sb = new StringBuilder();
        sb.AppendLine("# Relevant Memory Context");
        sb.AppendLine();
        
        foreach (var page in Pages)
        {
            sb.AppendLine($"## Memory from {page.CreatedAt:yyyy-MM-dd}");
            sb.AppendLine(page.Content);
            sb.AppendLine();
        }
        
        return sb.ToString();
    }
}

/// <summary>
/// A page retrieved during research.
/// </summary>
public record RetrievedPage
{
    public required Guid PageId { get; init; }
    public required string Content { get; init; }
    public required int TokenCount { get; init; }
    public required float RelevanceScore { get; init; }
    public required string RetrievedBy { get; init; }  // Which retriever found it
    public required DateTimeOffset CreatedAt { get; init; }
}
