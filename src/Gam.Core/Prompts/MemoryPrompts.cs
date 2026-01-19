using System.Text;
using System.Text.Json;
using Gam.Core.Models;

namespace Gam.Core.Prompts;

/// <summary>
/// Prompts used by the MemoryAgent for abstract generation.
/// Based on the original GAM paper: https://arxiv.org/abs/2511.18423
/// Enhanced with ADR-0002 memory enhancements (type, importance, tags).
/// </summary>
public static class MemoryPrompts
{
    /// <summary>
    /// System prompt for the Memory Agent (Memorizer) from the GAM paper.
    /// Enhanced with ADR-0002 fields: type, importance, tags.
    /// </summary>
    public const string AbstractSystemPrompt = """
        You are an intelligent librarian managing a personal knowledge library for a user.
        Your task is to analyze new pages (documents) and generate structured metadata for retrieval.
        
        The library is organized as follows:
        - Each page contains raw information from a conversation or document
        - Each page has an abstract with summary, headers, type, importance, and tags
        - This metadata enables precise retrieval during research
        
        You must analyze the content and output JSON with these fields:
        
        1. **summary**: 2-3 sentence overview capturing the essential information
        
        2. **headers**: 3-7 searchable keywords/phrases for index lookup
           - Be specific: "Python asyncio debugging" not just "Python"
           - Include: topics, entities, actions, temporal context
        
        3. **type**: Classify as ONE of:
           - "decision": A choice was made between alternatives
           - "preference": User expressed like/dislike without deciding
           - "fact": Factual information was shared or learned
           - "insight": A realization or understanding was reached
           - "task": An action item or todo was identified
           - "context": Background information, no specific action
           - "conversation": General discussion (default if unclear)
        
        4. **importance**: 0.0-1.0 score for future relevance:
           - 0.8-1.0: Critical decisions, key facts, important preferences
           - 0.5-0.7: Useful context, moderate relevance
           - 0.2-0.4: Minor details, low future relevance
        
        5. **tags**: Entity and topic tags for precise retrieval:
           - Format: "entity:<type>:<name>" or "keyword:<topic>"
           - Entity types: person, organization, tool, project, concept, location
           - Examples: "entity:person:john", "entity:tool:postgresql", "keyword:database"
           - Extract 3-10 tags
        
        Output ONLY valid JSON, no markdown code blocks:
        {
          "summary": "...",
          "headers": ["...", "..."],
          "type": "decision|preference|fact|insight|task|context|conversation",
          "importance": 0.0-1.0,
          "tags": ["entity:type:name", "keyword:topic", ...]
        }
        """;

    /// <summary>
    /// Build the user prompt for abstract generation.
    /// </summary>
    public static string BuildAbstractPrompt(ConversationTurn turn)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("Analyze the following page and generate JSON metadata:");
        sb.AppendLine();
        sb.AppendLine("---PAGE CONTENT---");
        sb.AppendLine($"Date: {turn.Timestamp:yyyy-MM-dd HH:mm}");
        if (!string.IsNullOrEmpty(turn.ConversationId))
        {
            sb.AppendLine($"Conversation: {turn.ConversationId}");
        }
        sb.AppendLine();
        sb.AppendLine($"User: {turn.UserMessage}");
        sb.AppendLine();
        sb.AppendLine($"Assistant: {turn.AssistantMessage}");
        
        if (turn.ToolCalls is { Count: > 0 })
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
        sb.AppendLine("Output JSON with: summary, headers, type, importance, tags");
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Parse the LLM response for abstract generation.
    /// Supports both JSON format (preferred) and legacy text format for backward compatibility.
    /// </summary>
    public static ParsedAbstract ParseAbstractResponse(string response)
    {
        // Try JSON parsing first
        var json = TryExtractJson(response);
        if (json != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                return new ParsedAbstract
                {
                    Summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "",
                    Headers = root.TryGetProperty("headers", out var h) 
                        ? h.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrEmpty(x)).ToList()
                        : [],
                    Type = root.TryGetProperty("type", out var t) 
                        ? MemoryTypeExtensions.ParseMemoryType(t.GetString()) 
                        : MemoryType.Conversation,
                    Importance = root.TryGetProperty("importance", out var i) 
                        ? (float)i.GetDouble() 
                        : 0.5f,
                    Tags = root.TryGetProperty("tags", out var tags) 
                        ? tags.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrEmpty(x)).ToList()
                        : []
                };
            }
            catch
            {
                // Fall through to legacy parsing
            }
        }
        
        // Legacy text format parsing (backward compatibility)
        return ParseLegacyFormat(response);
    }
    
    private static string? TryExtractJson(string response)
    {
        // Try to find JSON object in response
        var start = response.IndexOf('{');
        var end = response.LastIndexOf('}');
        
        if (start >= 0 && end > start)
        {
            return response[start..(end + 1)];
        }
        
        return null;
    }
    
    private static ParsedAbstract ParseLegacyFormat(string response)
    {
        // Legacy format:
        // SUMMARY: <summary text>
        // HEADERS:
        // - Header 1
        // - Header 2
        
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var summary = "";
        var headers = new List<string>();
        var inHeaders = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            
            if (trimmed.StartsWith("SUMMARY:", StringComparison.OrdinalIgnoreCase))
            {
                summary = trimmed["SUMMARY:".Length..].Trim();
            }
            else if (trimmed.StartsWith("HEADERS:", StringComparison.OrdinalIgnoreCase))
            {
                inHeaders = true;
            }
            else if (inHeaders && trimmed.StartsWith("-"))
            {
                headers.Add(trimmed[1..].Trim());
            }
        }

        return new ParsedAbstract
        {
            Summary = summary,
            Headers = headers,
            Type = MemoryType.Conversation,  // Default for legacy
            Importance = 0.5f,               // Default for legacy
            Tags = []                        // Empty for legacy
        };
    }
}

/// <summary>
/// Result of parsing an abstract generation response.
/// </summary>
public record ParsedAbstract
{
    public required string Summary { get; init; }
    public required IReadOnlyList<string> Headers { get; init; }
    public MemoryType Type { get; init; } = MemoryType.Conversation;
    public float Importance { get; init; } = 0.5f;
    public IReadOnlyList<string> Tags { get; init; } = [];
}
