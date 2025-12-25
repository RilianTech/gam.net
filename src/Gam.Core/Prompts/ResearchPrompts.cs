using System.Text;
using Gam.Core.Models;

namespace Gam.Core.Prompts;

/// <summary>
/// Prompts used by the ResearchAgent for iterative retrieval.
/// Based on the original GAM paper: https://arxiv.org/abs/2511.18423
/// </summary>
public static class ResearchPrompts
{
    /// <summary>
    /// System prompt for the Research Agent (Researcher) planning phase.
    /// </summary>
    public const string PlanSystemPrompt = """
        You are a research assistant helping to find relevant information from a personal knowledge library.
        
        Your task is to plan search strategies to retrieve pages that would help answer a user's query.
        You have access to three retrieval methods:
        
        1. KEYWORD (BM25): Lexical search - best for:
           - Specific terms, names, identifiers
           - Exact phrases or technical terms
           - When you know the exact words used
        
        2. VECTOR (Semantic): Embedding similarity - best for:
           - Conceptual or thematic similarity
           - Paraphrased or related ideas
           - When the exact wording is unknown
        
        3. INDEX (Header lookup): Direct header matching - best for:
           - Known topic categories
           - When previous iterations revealed relevant headers
           - Targeted retrieval of specific subjects
        
        Based on the query and any context already retrieved, plan the next search step.
        
        Output format (all fields required):
        STRATEGY: <1-2 sentence description of your search approach>
        SEARCH_QUERY: <the optimized query string to search with>
        USE_KEYWORD: true/false
        USE_VECTOR: true/false
        USE_INDEX: true/false
        TARGET_HEADERS: <comma-separated list of headers to target, or "none">
        COMPLETE: true/false
        
        Set COMPLETE: true only when:
        - The retrieved context fully addresses the query, OR
        - Multiple search strategies have been exhausted with diminishing returns
        
        Search strategy tips:
        - Start with both KEYWORD and VECTOR for broad coverage
        - If results are sparse, try rephrasing the SEARCH_QUERY
        - Use INDEX when you've seen relevant headers in previous results
        - Don't repeat the exact same search that yielded no results
        """;

    /// <summary>
    /// System prompt for the reflection phase - evaluating if context is sufficient.
    /// </summary>
    public const string ReflectSystemPrompt = """
        You are evaluating whether the retrieved context is sufficient to answer a user's query.
        
        Consider:
        - Does the context contain information directly relevant to the query?
        - Are there obvious gaps that additional searching might fill?
        - Is the context coherent and actionable?
        - Would more iterations likely yield significant new information?
        
        Respond with exactly one word:
        - CONTINUE: More relevant information likely exists and would meaningfully improve the response
        - SUFFICIENT: The context adequately addresses the query, or further search is unlikely to help
        
        Err toward SUFFICIENT if:
        - You have multiple relevant pages covering the topic
        - The query can be reasonably answered with current context
        - Recent iterations returned mostly duplicate or low-relevance results
        """;

    /// <summary>
    /// Build the user prompt for the planning phase.
    /// </summary>
    public static string BuildPlanPrompt(string query, List<RetrievedPage> pages)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"User Query: {query}");
        sb.AppendLine();
        sb.AppendLine($"Search Progress:");
        sb.AppendLine($"- Pages retrieved so far: {pages.Count}");
        sb.AppendLine($"- Total tokens in context: {pages.Sum(p => p.TokenCount)}");
        sb.AppendLine();
        
        if (pages.Count > 0)
        {
            sb.AppendLine("Retrieved context summaries (most recent first):");
            foreach (var page in pages.Take(5))
            {
                var preview = page.Content.Length > 200 
                    ? page.Content[..200] + "..." 
                    : page.Content;
                sb.AppendLine($"  [{page.RetrievedBy}] {preview}");
                sb.AppendLine();
            }
            
            if (pages.Count > 5)
            {
                sb.AppendLine($"  ... and {pages.Count - 5} more pages");
            }
        }
        else
        {
            sb.AppendLine("No pages retrieved yet. This is the first search iteration.");
        }
        
        sb.AppendLine();
        sb.AppendLine("Plan the next search step:");
        
        return sb.ToString();
    }

    /// <summary>
    /// Build the user prompt for the reflection phase.
    /// </summary>
    public static string BuildReflectPrompt(string query, List<RetrievedPage> pages)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"Query: {query}");
        sb.AppendLine();
        sb.AppendLine($"Retrieved {pages.Count} pages containing {pages.Sum(p => p.TokenCount)} tokens.");
        sb.AppendLine();
        sb.AppendLine("Context coverage:");
        
        foreach (var page in pages.Take(7))
        {
            var preview = page.Content.Length > 150 
                ? page.Content[..150] + "..." 
                : page.Content;
            sb.AppendLine($"  - {preview}");
        }
        
        if (pages.Count > 7)
        {
            sb.AppendLine($"  ... and {pages.Count - 7} more pages");
        }
        
        sb.AppendLine();
        sb.AppendLine("Is this context sufficient to answer the query? (CONTINUE or SUFFICIENT)");
        
        return sb.ToString();
    }
}
