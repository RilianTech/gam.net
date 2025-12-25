using System.Text;
using Gam.Core.Models;
using Microsoft.Extensions.Options;

namespace Gam.Core.Prompts;

/// <summary>
/// Default prompt provider with GAM paper prompts.
/// Supports customization via PromptOptions and file-based overrides.
/// </summary>
public class DefaultPromptProvider : IPromptProvider
{
    private readonly PromptOptions _options;
    private readonly Lazy<string> _memorySystemPrompt;
    private readonly Lazy<string> _planSystemPrompt;
    private readonly Lazy<string> _reflectSystemPrompt;

    public DefaultPromptProvider(IOptions<PromptOptions>? options = null)
    {
        _options = options?.Value ?? new PromptOptions();
        
        _memorySystemPrompt = new Lazy<string>(() => LoadPrompt("memory_system.txt", _options.MemorySystemPrompt, DefaultPrompts.MemorySystem));
        _planSystemPrompt = new Lazy<string>(() => LoadPrompt("plan_system.txt", _options.PlanSystemPrompt, DefaultPrompts.PlanSystem));
        _reflectSystemPrompt = new Lazy<string>(() => LoadPrompt("reflect_system.txt", _options.ReflectSystemPrompt, DefaultPrompts.ReflectSystem));
    }

    private string LoadPrompt(string filename, string? configOverride, string defaultValue)
    {
        // Priority: 1) Config override, 2) File, 3) Default
        if (!string.IsNullOrWhiteSpace(configOverride))
            return configOverride;

        if (!string.IsNullOrWhiteSpace(_options.PromptDirectory))
        {
            var path = Path.Combine(_options.PromptDirectory, filename);
            if (File.Exists(path))
                return File.ReadAllText(path);
        }

        return defaultValue;
    }

    public string GetMemorySystemPrompt() => _memorySystemPrompt.Value;

    public string BuildMemoryUserPrompt(ConversationTurn turn)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("Write an abstract for the following page to be added to the library:");
        sb.AppendLine();
        sb.AppendLine("---PAGE CONTENT---");
        sb.AppendLine($"Date: {turn.Timestamp:yyyy-MM-dd HH:mm}");
        
        if (_options.IncludeConversationId && !string.IsNullOrEmpty(turn.ConversationId))
        {
            sb.AppendLine($"Conversation: {turn.ConversationId}");
        }
        
        sb.AppendLine();
        sb.AppendLine($"User: {turn.UserMessage}");
        sb.AppendLine();
        sb.AppendLine($"Assistant: {turn.AssistantMessage}");
        
        if (_options.IncludeToolCalls && turn.ToolCalls is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Tools used:");
            foreach (var tool in turn.ToolCalls)
            {
                sb.AppendLine($"  - {tool.ToolName}: {tool.Result}");
            }
        }
        
        sb.AppendLine("---END PAGE---");
        sb.AppendLine();
        sb.AppendLine("Generate the SUMMARY and HEADERS for this page:");
        
        return sb.ToString();
    }

    public string GetPlanSystemPrompt() => _planSystemPrompt.Value;

    public string BuildPlanUserPrompt(string query, List<RetrievedPage> pages)
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
            foreach (var page in pages.Take(_options.MaxPagesInPlanPrompt))
            {
                var preview = page.Content.Length > _options.ContentPreviewLength 
                    ? page.Content[.._options.ContentPreviewLength] + "..." 
                    : page.Content;
                sb.AppendLine($"  [{page.RetrievedBy}] {preview}");
                sb.AppendLine();
            }
            
            if (pages.Count > _options.MaxPagesInPlanPrompt)
            {
                sb.AppendLine($"  ... and {pages.Count - _options.MaxPagesInPlanPrompt} more pages");
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

    public string GetReflectSystemPrompt() => _reflectSystemPrompt.Value;

    public string BuildReflectUserPrompt(string query, List<RetrievedPage> pages)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"Query: {query}");
        sb.AppendLine();
        sb.AppendLine($"Retrieved {pages.Count} pages containing {pages.Sum(p => p.TokenCount)} tokens.");
        sb.AppendLine();
        sb.AppendLine("Context coverage:");
        
        foreach (var page in pages.Take(_options.MaxPagesInReflectPrompt))
        {
            var preview = page.Content.Length > _options.ContentPreviewLength - 50 
                ? page.Content[..(_options.ContentPreviewLength - 50)] + "..." 
                : page.Content;
            sb.AppendLine($"  - {preview}");
        }
        
        if (pages.Count > _options.MaxPagesInReflectPrompt)
        {
            sb.AppendLine($"  ... and {pages.Count - _options.MaxPagesInReflectPrompt} more pages");
        }
        
        sb.AppendLine();
        sb.AppendLine("Is this context sufficient to answer the query? (CONTINUE or SUFFICIENT)");
        
        return sb.ToString();
    }
}

/// <summary>
/// Default prompts based on the GAM paper: https://arxiv.org/abs/2511.18423
/// </summary>
public static class DefaultPrompts
{
    public const string MemorySystem = """
        You are an intelligent librarian managing a personal knowledge library for a user.
        Your task is to write abstracts for new pages (documents) to be added to the library.
        
        The library is organized as follows:
        - Each page contains raw information from a conversation or document
        - Each page has an abstract that summarizes its content
        - Abstracts contain headers that act as an index for retrieval
        
        When writing an abstract, you must:
        1. Write a concise summary (2-3 sentences) capturing the essential information
        2. Extract headers that would help locate this page during search
        
        Header guidelines:
        - Headers should be specific and descriptive keywords/phrases
        - Include: main topics, entities (names, products, technologies), actions taken
        - Include temporal context if present (dates, recurring events)
        - Aim for 3-7 headers per page
        - Be specific: "Python asyncio debugging" not just "Python"
        - Think about what search queries would need this page
        
        Output format (strict):
        SUMMARY: <your concise summary here>
        HEADERS:
        - <header 1>
        - <header 2>
        - <header 3>
        ...
        """;

    public const string PlanSystem = """
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

    public const string ReflectSystem = """
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
}
