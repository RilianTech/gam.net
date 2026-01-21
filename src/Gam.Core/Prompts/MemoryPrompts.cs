using System.Text;
using System.Text.Json;
using Gam.Core.Models;

namespace Gam.Core.Prompts;

/// <summary>
/// Prompts used by the MemoryAgent for abstract generation.
/// Based on the original GAM paper: https://arxiv.org/abs/2511.18423
/// Simplified based on SimpleMem's proven approach: atomic facts, no pronouns, absolute times.
/// </summary>
public static class MemoryPrompts
{
    /// <summary>
    /// System prompt for the Memory Agent (Memorizer).
    /// Simplified to focus on what matters: atomic facts with disambiguation.
    /// Inspired by SimpleMem's approach which achieves 43.24% F1 on LoCoMo.
    /// </summary>
    public const string AbstractSystemPrompt = """
        You extract searchable facts from conversations.
        
        RULES - NEVER VIOLATE:
        ======================
        1. PROHIBIT all pronouns (he, she, it, they, this, that, their, his, her)
           Replace with actual names from the content.
        
        2. PROHIBIT relative time (yesterday, next week, last month, recently)
           Convert to absolute dates using the provided timestamp.
        
        3. Extract MULTIPLE searchable facts, not just one summary.
           Each fact must be self-contained and independently understandable.
        
        OUTPUT FORMAT (JSON only, no markdown):
        {
          "summary": "Brief 1-2 sentence overview with names and dates",
          "facts": [
            "Fact 1 with specific names and absolute dates",
            "Fact 2 with specific names and absolute dates",
            "..."
          ],
          "entities": ["person1", "person2", "place1", "topic1"],
          "type": "decision|preference|fact|task|conversation",
          "importance": 0.0-1.0
        }
        
        EXAMPLES:
        
        Input: "He mentioned he'll visit the doctor next week"
        Timestamp: 2023-05-15
        Output facts: ["John will visit the doctor during week of 2023-05-22"]
        
        Input: "She said her son had an accident on the road trip"
        Output facts: ["Maria's son had an accident during road trip", "Maria discussed son's road trip accident"]
        """;

    /// <summary>
    /// Build the user prompt for abstract generation.
    /// </summary>
    public static string BuildAbstractPrompt(MemoryInput input)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"TIMESTAMP: {input.Timestamp:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"Use this timestamp to convert any relative times to absolute dates.");
        sb.AppendLine();
        sb.AppendLine("CONTENT:");
        sb.AppendLine(input.Content);
        
        if (input.ToolCalls is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("TOOLS USED:");
            foreach (var tool in input.ToolCalls)
            {
                sb.AppendLine($"  - {tool.ToolName}: {tool.Result}");
            }
        }
        
        sb.AppendLine();
        sb.AppendLine("Extract facts. Remember: NO pronouns, NO relative times.");
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Parse the LLM response for abstract generation.
    /// Supports new simplified format (facts), standard format (headers), and legacy text format.
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
                
                // Get facts (new format) or headers (old format) - facts become headers for search
                var headers = new List<string>();
                
                // Prefer "facts" (new simplified format)
                if (root.TryGetProperty("facts", out var facts) && facts.ValueKind == JsonValueKind.Array)
                {
                    headers = facts.EnumerateArray()
                        .Select(x => x.GetString() ?? "")
                        .Where(x => !string.IsNullOrEmpty(x))
                        .ToList();
                }
                // Fall back to "headers" (old format)
                else if (root.TryGetProperty("headers", out var h) && h.ValueKind == JsonValueKind.Array)
                {
                    headers = h.EnumerateArray()
                        .Select(x => x.GetString() ?? "")
                        .Where(x => !string.IsNullOrEmpty(x))
                        .ToList();
                }
                
                // Get entities and convert to tags
                var tags = new List<string>();
                if (root.TryGetProperty("entities", out var entities) && entities.ValueKind == JsonValueKind.Array)
                {
                    tags = entities.EnumerateArray()
                        .Select(x => x.GetString() ?? "")
                        .Where(x => !string.IsNullOrEmpty(x))
                        .Select(e => $"entity:{e.ToLowerInvariant()}")
                        .ToList();
                }
                // Also support old "tags" format
                else if (root.TryGetProperty("tags", out var oldTags) && oldTags.ValueKind == JsonValueKind.Array)
                {
                    tags = oldTags.EnumerateArray()
                        .Select(x => x.GetString() ?? "")
                        .Where(x => !string.IsNullOrEmpty(x))
                        .ToList();
                }
                
                return new ParsedAbstract
                {
                    Summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "",
                    Headers = headers,
                    Type = root.TryGetProperty("type", out var t) 
                        ? MemoryTypeExtensions.ParseMemoryType(t.GetString()) 
                        : MemoryType.Conversation,
                    Importance = root.TryGetProperty("importance", out var i) 
                        ? (float)i.GetDouble() 
                        : 0.5f,
                    Tags = tags,
                    TemporalFacts = root.TryGetProperty("temporal_facts", out var tf) 
                        ? ParseTemporalFacts(tf)
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
    
    private static IReadOnlyList<TemporalFact> ParseTemporalFacts(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return [];
        
        var facts = new List<TemporalFact>();
        
        foreach (var item in element.EnumerateArray())
        {
            var eventDesc = item.TryGetProperty("event", out var e) ? e.GetString() : null;
            var dateStr = item.TryGetProperty("date", out var d) ? d.GetString() : null;
            
            if (string.IsNullOrEmpty(eventDesc) || string.IsNullOrEmpty(dateStr))
                continue;
            
            // Try to parse the date
            if (!DateTimeOffset.TryParse(dateStr, out var date))
                continue;
            
            var participants = new List<string>();
            if (item.TryGetProperty("participants", out var p) && p.ValueKind == JsonValueKind.Array)
            {
                foreach (var participant in p.EnumerateArray())
                {
                    var name = participant.GetString();
                    if (!string.IsNullOrEmpty(name))
                        participants.Add(name);
                }
            }
            
            var location = item.TryGetProperty("location", out var loc) ? loc.GetString() : null;
            
            facts.Add(new TemporalFact
            {
                Event = eventDesc,
                Date = date,
                Participants = participants,
                Location = location
            });
        }
        
        return facts;
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
    
    /// <summary>
    /// Extracted temporal facts with absolute dates (from SimpleMem's Phi_time anchoring).
    /// </summary>
    public IReadOnlyList<TemporalFact> TemporalFacts { get; init; } = [];
}

/// <summary>
/// A temporal fact extracted from memory content with an absolute date.
/// </summary>
public record TemporalFact
{
    /// <summary>Description of the event.</summary>
    public required string Event { get; init; }
    
    /// <summary>Absolute date of the event (resolved from relative time).</summary>
    public required DateTimeOffset Date { get; init; }
    
    /// <summary>People involved in the event.</summary>
    public IReadOnlyList<string> Participants { get; init; } = [];
    
    /// <summary>Optional location of the event.</summary>
    public string? Location { get; init; }
}
