using System.Text;
using System.Text.Json;
using Gam.Core.Abstractions;
using Gam.Core.Models;

namespace Gam.Core.Prompts;

/// <summary>
/// Prompts for GAM Deep Research - the core innovation from the GAM paper.
/// These prompts enable multi-query planning, LLM integration, and two-step reflection.
/// </summary>
public static class DeepResearchPrompts
{
    /// <summary>
    /// System prompt for multi-query planning.
    /// Key difference from simple research: generates MULTIPLE queries per iteration.
    /// </summary>
    public const string DeepPlanSystemPrompt = """
        You are a research planning agent. Your task is to create a comprehensive search plan 
        to retrieve relevant memories that will help answer the user's question.

        You have access to a personal memory library with these retrieval methods:
        
        1. KEYWORD (BM25): Lexical search
           - Best for: specific terms, names, identifiers, exact phrases
           - Generate queries with exact terms likely used in the memories
           
        2. VECTOR (Semantic): Embedding similarity search  
           - Best for: conceptual similarity, paraphrased ideas, related topics
           - Generate natural language queries describing what you're looking for
           
        3. PAGE_INDEX: Direct page lookup by memory index
           - Best for: when you see specific memories in the MEMORY section that are relevant
           - Use the index numbers [0], [1], etc. from the MEMORY section

        KEY INSIGHT: Generate MULTIPLE queries per tool for better coverage. Different phrasings 
        find different relevant memories.

        Output a JSON object with this exact structure:
        {
            "strategy": "Brief description of your search approach",
            "info_needs": ["What specific information do you need?", "What sub-questions must be answered?"],
            "tools": ["keyword", "vector"],
            "keyword_queries": ["exact term 1", "specific name", "technical phrase"],
            "vector_queries": ["How does X work?", "What was decided about Y?"],
            "page_indices": [0, 3],
            "is_complete": false
        }

        Set is_complete=true ONLY when:
        - The EXISTING CONTEXT fully addresses all aspects of the question
        - Multiple search iterations have yielded diminishing returns
        
        Do NOT set is_complete=true on the first iteration unless the EXISTING CONTEXT is comprehensive.
        """;

    /// <summary>
    /// System prompt for LLM integration - synthesizing search hits into coherent context.
    /// This is what makes GAM different from simple RAG: LLM synthesizes, not just concatenates.
    /// </summary>
    public const string IntegrationSystemPrompt = """
        You are a research synthesis agent. Your task is to integrate new search results with 
        existing context to build comprehensive information that answers the user's question.

        Guidelines:
        1. SYNTHESIZE - Don't just list facts; create a coherent narrative
        2. RESOLVE CONFLICTS - If sources contradict, note the discrepancy
        3. CITE SOURCES - Reference page IDs when making claims
        4. STAY FOCUSED - Only include information relevant to the question
        5. PRESERVE DETAILS - Don't lose important specifics like names, numbers, dates

        Output a JSON object:
        {
            "content": "Synthesized context that addresses the question. Include relevant details, cite sources by page ID.",
            "source_page_ids": ["guid-1", "guid-2"]
        }
        """;

    /// <summary>
    /// System prompt for Step 1 of reflection: checking if information is sufficient.
    /// </summary>
    public const string ReflectCheckSystemPrompt = """
        You are evaluating whether the gathered information is sufficient to answer a question.

        Consider:
        - Does the context contain information that directly addresses the question?
        - Are there obvious gaps or missing pieces?
        - Is the information specific enough (names, dates, details)?
        - Would the user be satisfied with an answer based on this context?

        Output JSON:
        {
            "is_sufficient": true/false
        }

        Be conservative - if there's reasonable doubt, say false.
        """;

    /// <summary>
    /// System prompt for Step 2 of reflection: generating follow-up queries.
    /// </summary>
    public const string ReflectFollowUpSystemPrompt = """
        The current information is NOT sufficient to answer the question.
        
        Analyze what's missing and generate specific follow-up queries that would fill the gaps.
        
        Think about:
        - What specific facts are missing?
        - What names, dates, or details are unclear?
        - What related topics might provide context?
        
        Output JSON:
        {
            "gap_analysis": "Brief explanation of what's missing",
            "follow_up_queries": ["specific query 1", "specific query 2"]
        }
        
        Generate 2-4 targeted follow-up queries. Be specific, not generic.
        """;

    /// <summary>
    /// Build the user prompt for deep planning.
    /// </summary>
    public static string BuildDeepPlanPrompt(
        string query, 
        IReadOnlyList<MemoryAbstract> abstracts,
        IntegratedResult? existingContext,
        int iteration)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"QUESTION: {query}");
        sb.AppendLine();
        sb.AppendLine($"ITERATION: {iteration}");
        sb.AppendLine();
        
        if (existingContext != null && !string.IsNullOrEmpty(existingContext.Content))
        {
            sb.AppendLine("EXISTING CONTEXT (from previous iterations):");
            sb.AppendLine(existingContext.Content);
            sb.AppendLine();
        }
        
        sb.AppendLine("MEMORY (available pages with summaries):");
        for (var i = 0; i < abstracts.Count; i++)
        {
            var abs = abstracts[i];
            sb.AppendLine($"[{i}] {abs.Summary}");
            if (abs.Headers.Count > 0)
            {
                sb.AppendLine($"    Headers: {string.Join(", ", abs.Headers)}");
            }
        }
        
        if (abstracts.Count == 0)
        {
            sb.AppendLine("(No memories available yet)");
        }
        
        sb.AppendLine();
        sb.AppendLine("Generate a search plan to find information relevant to the QUESTION:");
        
        return sb.ToString();
    }

    /// <summary>
    /// Build the user prompt for integration.
    /// </summary>
    public static string BuildIntegrationPrompt(
        string query,
        IReadOnlyList<RetrievedPage> newPages,
        IntegratedResult? existingContext)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"QUESTION: {query}");
        sb.AppendLine();
        
        if (existingContext != null && !string.IsNullOrEmpty(existingContext.Content))
        {
            sb.AppendLine("EXISTING CONTEXT:");
            sb.AppendLine(existingContext.Content);
            sb.AppendLine();
        }
        
        sb.AppendLine("NEW EVIDENCE (from current search):");
        foreach (var page in newPages)
        {
            sb.AppendLine($"--- Page {page.PageId} (relevance: {page.RelevanceScore:F2}) ---");
            sb.AppendLine(page.Content);
            sb.AppendLine();
        }
        
        sb.AppendLine("Synthesize the NEW EVIDENCE with EXISTING CONTEXT to answer the QUESTION:");
        
        return sb.ToString();
    }

    /// <summary>
    /// Build the user prompt for reflection check.
    /// </summary>
    public static string BuildReflectCheckPrompt(string query, IntegratedResult context)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"QUESTION: {query}");
        sb.AppendLine();
        sb.AppendLine("GATHERED INFORMATION:");
        sb.AppendLine(context.Content);
        sb.AppendLine();
        sb.AppendLine("Is this information sufficient to answer the QUESTION?");
        
        return sb.ToString();
    }

    /// <summary>
    /// Build the user prompt for follow-up generation.
    /// </summary>
    public static string BuildReflectFollowUpPrompt(string query, IntegratedResult context)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"QUESTION: {query}");
        sb.AppendLine();
        sb.AppendLine("CURRENT INFORMATION (insufficient):");
        sb.AppendLine(context.Content);
        sb.AppendLine();
        sb.AppendLine("What specific information is still needed? Generate follow-up queries:");
        
        return sb.ToString();
    }

    /// <summary>
    /// Parse the JSON response from planning.
    /// </summary>
    public static DeepResearchPlan ParsePlanResponse(string response)
    {
        try
        {
            // Extract JSON from response (may be wrapped in markdown code blocks)
            var json = ExtractJson(response);
            
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            return new DeepResearchPlan
            {
                Strategy = root.TryGetProperty("strategy", out var s) ? s.GetString() ?? "" : "",
                InfoNeeds = ParseStringArray(root, "info_needs"),
                Tools = ParseStringArray(root, "tools"),
                KeywordQueries = ParseStringArray(root, "keyword_queries"),
                VectorQueries = ParseStringArray(root, "vector_queries"),
                PageIndices = ParseIntArray(root, "page_indices"),
                IsComplete = root.TryGetProperty("is_complete", out var c) && c.GetBoolean()
            };
        }
        catch (JsonException)
        {
            // Fallback: try to extract some structure from non-JSON response
            return ParsePlanResponseFallback(response);
        }
    }

    /// <summary>
    /// Parse integration response.
    /// </summary>
    public static IntegratedResult ParseIntegrationResponse(string response)
    {
        try
        {
            var json = ExtractJson(response);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            var content = root.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
            var sourceIds = new List<Guid>();
            
            if (root.TryGetProperty("source_page_ids", out var ids))
            {
                foreach (var id in ids.EnumerateArray())
                {
                    if (Guid.TryParse(id.GetString(), out var guid))
                        sourceIds.Add(guid);
                }
            }
            
            return new IntegratedResult
            {
                Content = content,
                SourcePageIds = sourceIds,
                TokenCount = content.Length / 4 // Rough estimate
            };
        }
        catch (JsonException)
        {
            // If JSON parsing fails, use the raw response as content
            return new IntegratedResult
            {
                Content = response,
                SourcePageIds = Array.Empty<Guid>(),
                TokenCount = response.Length / 4
            };
        }
    }

    /// <summary>
    /// Parse reflection check response.
    /// </summary>
    public static bool ParseReflectCheckResponse(string response)
    {
        try
        {
            var json = ExtractJson(response);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            return root.TryGetProperty("is_sufficient", out var s) && s.GetBoolean();
        }
        catch (JsonException)
        {
            // Fallback: look for keywords
            var lower = response.ToLowerInvariant();
            return lower.Contains("sufficient") && !lower.Contains("not sufficient") && !lower.Contains("insufficient");
        }
    }

    /// <summary>
    /// Parse reflection follow-up response.
    /// </summary>
    public static (string? GapAnalysis, IReadOnlyList<string> FollowUps) ParseReflectFollowUpResponse(string response)
    {
        try
        {
            var json = ExtractJson(response);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            var gap = root.TryGetProperty("gap_analysis", out var g) ? g.GetString() : null;
            var followUps = ParseStringArray(root, "follow_up_queries");
            
            return (gap, followUps);
        }
        catch (JsonException)
        {
            // Fallback: return the whole response as gap analysis
            return (response, Array.Empty<string>());
        }
    }

    private static string ExtractJson(string response)
    {
        // Try to extract JSON from markdown code blocks
        var jsonStart = response.IndexOf('{');
        var jsonEnd = response.LastIndexOf('}');
        
        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            return response.Substring(jsonStart, jsonEnd - jsonStart + 1);
        }
        
        return response;
    }

    private static IReadOnlyList<string> ParseStringArray(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        
        return arr.EnumerateArray()
            .Select(e => e.GetString() ?? "")
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
    }

    private static IReadOnlyList<int> ParseIntArray(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<int>();
        
        return arr.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.Number)
            .Select(e => e.GetInt32())
            .ToList();
    }

    private static DeepResearchPlan ParsePlanResponseFallback(string response)
    {
        // Try to extract information from non-JSON format
        var lines = response.Split('\n');
        var keywordQueries = new List<string>();
        var vectorQueries = new List<string>();
        var strategy = "";
        var isComplete = false;
        
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.StartsWith("STRATEGY:", StringComparison.OrdinalIgnoreCase))
                strategy = t["STRATEGY:".Length..].Trim();
            else if (t.StartsWith("KEYWORD:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("SEARCH_QUERY:", StringComparison.OrdinalIgnoreCase))
                keywordQueries.Add(t.Split(':')[1].Trim());
            else if (t.StartsWith("VECTOR:", StringComparison.OrdinalIgnoreCase))
                vectorQueries.Add(t.Split(':')[1].Trim());
            else if (t.StartsWith("COMPLETE:", StringComparison.OrdinalIgnoreCase))
                isComplete = t.Contains("true", StringComparison.OrdinalIgnoreCase);
        }
        
        return new DeepResearchPlan
        {
            Strategy = strategy,
            InfoNeeds = Array.Empty<string>(),
            Tools = new[] { "keyword", "vector" },
            KeywordQueries = keywordQueries,
            VectorQueries = vectorQueries.Count > 0 ? vectorQueries : keywordQueries, // Use keyword as fallback
            PageIndices = Array.Empty<int>(),
            IsComplete = isComplete
        };
    }
}
