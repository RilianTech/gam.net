using System.Text;
using Gam.Core.Models;

namespace Gam.Core.Prompts;

/// <summary>
/// Prompts used by the ResearchAgent for planning and reflection.
/// </summary>
public static class ResearchPrompts
{
    public const string PlanSystemPrompt = """
        You are a research planning assistant. Given a query and current context,
        decide on the best search strategy to find relevant memories.
        
        Available retrieval methods:
        - KEYWORD: BM25 text search, good for specific terms/names
        - VECTOR: Semantic similarity, good for conceptual matches  
        - INDEX: Direct header lookup, good when you know specific topics
        
        Output format:
        STRATEGY: <brief description of approach>
        SEARCH_QUERY: <optimized search query>
        USE_KEYWORD: true/false
        USE_VECTOR: true/false
        USE_INDEX: true/false
        TARGET_HEADERS: <comma-separated headers if using index>
        COMPLETE: true/false (true if no more search needed)
        """;

    public const string ReflectSystemPrompt = """
        Evaluate if gathered memory context is sufficient to answer a query.
        Respond with exactly one word: CONTINUE or SUFFICIENT.
        """;

    public static string BuildPlanPrompt(string query, List<RetrievedPage> pages)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Query: {query}");
        sb.AppendLine($"Pages retrieved: {pages.Count}");
        if (pages.Count > 0)
        {
            sb.AppendLine("Current context (summaries):");
            foreach (var p in pages.Take(5))
            {
                var preview = p.Content.Length > 150 ? p.Content[..150] + "..." : p.Content;
                sb.AppendLine($"  - {preview}");
            }
        }
        return sb.ToString();
    }

    public static string BuildReflectPrompt(string query, List<RetrievedPage> pages)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Query: {query}");
        sb.AppendLine($"Retrieved {pages.Count} pages. Topics:");
        foreach (var p in pages)
        {
            var preview = p.Content.Length > 100 ? p.Content[..100] + "..." : p.Content;
            sb.AppendLine($"  - {preview}");
        }
        sb.AppendLine("Is this sufficient?");
        return sb.ToString();
    }
}
