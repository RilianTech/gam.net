using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gam.Core.Tools;

/// <summary>
/// OpenAI-compatible tool/function schemas for GAM operations.
/// These can be used with AI SDK, OpenAI function calling, or any compatible framework.
/// </summary>
public static class GamToolSchemas
{
    /// <summary>
    /// Get all GAM tool definitions in OpenAI function calling format.
    /// </summary>
    public static IReadOnlyList<ToolDefinition> GetAllTools() =>
    [
        MemorizeTool,
        ResearchTool,
        ForgetTool
    ];

    /// <summary>
    /// Tool for storing a conversation turn in memory.
    /// </summary>
    public static ToolDefinition MemorizeTool => new()
    {
        Type = "function",
        Function = new FunctionDefinition
        {
            Name = "gam_memorize",
            Description = "Store a conversation turn in long-term memory. Use this to save important information from the current conversation that should be remembered for future sessions.",
            Parameters = new ParameterSchema
            {
                Type = "object",
                Properties = new Dictionary<string, PropertySchema>
                {
                    ["user_message"] = new()
                    {
                        Type = "string",
                        Description = "The user's message or question"
                    },
                    ["assistant_message"] = new()
                    {
                        Type = "string",
                        Description = "The assistant's response"
                    },
                    ["owner_id"] = new()
                    {
                        Type = "string",
                        Description = "The user/owner ID to associate this memory with"
                    }
                },
                Required = ["user_message", "assistant_message", "owner_id"]
            }
        }
    };

    /// <summary>
    /// Tool for researching/retrieving relevant memories.
    /// </summary>
    public static ToolDefinition ResearchTool => new()
    {
        Type = "function",
        Function = new FunctionDefinition
        {
            Name = "gam_research",
            Description = "Search long-term memory for information relevant to a query. Use this to recall past conversations, user preferences, or previously discussed topics.",
            Parameters = new ParameterSchema
            {
                Type = "object",
                Properties = new Dictionary<string, PropertySchema>
                {
                    ["query"] = new()
                    {
                        Type = "string",
                        Description = "The search query describing what information to find"
                    },
                    ["owner_id"] = new()
                    {
                        Type = "string",
                        Description = "The user/owner ID whose memories to search"
                    },
                    ["max_tokens"] = new()
                    {
                        Type = "integer",
                        Description = "Maximum tokens to return in context (default: 8000)"
                    }
                },
                Required = ["query", "owner_id"]
            }
        }
    };

    /// <summary>
    /// Tool for forgetting/deleting memories.
    /// </summary>
    public static ToolDefinition ForgetTool => new()
    {
        Type = "function",
        Function = new FunctionDefinition
        {
            Name = "gam_forget",
            Description = "Delete memories for a user. Use with caution - this permanently removes stored information.",
            Parameters = new ParameterSchema
            {
                Type = "object",
                Properties = new Dictionary<string, PropertySchema>
                {
                    ["owner_id"] = new()
                    {
                        Type = "string",
                        Description = "The user/owner ID whose memories to delete"
                    },
                    ["all"] = new()
                    {
                        Type = "boolean",
                        Description = "If true, delete ALL memories for this user"
                    },
                    ["before"] = new()
                    {
                        Type = "string",
                        Description = "ISO 8601 datetime - delete memories before this date"
                    }
                },
                Required = ["owner_id"]
            }
        }
    };

    /// <summary>
    /// Serialize tools to JSON for API responses.
    /// </summary>
    public static string ToJson(bool indented = false) =>
        JsonSerializer.Serialize(GetAllTools(), new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
}

#region Schema Models

public class ToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public FunctionDefinition Function { get; set; } = new();
}

public class FunctionDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("parameters")]
    public ParameterSchema Parameters { get; set; } = new();
}

public class ParameterSchema
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, PropertySchema> Properties { get; set; } = new();

    [JsonPropertyName("required")]
    public string[] Required { get; set; } = [];
}

public class PropertySchema
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("enum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Enum { get; set; }
}

#endregion
