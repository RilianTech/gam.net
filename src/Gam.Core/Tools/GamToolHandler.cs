using System.Text.Json;
using Gam.Core.Abstractions;
using Gam.Core.Models;

namespace Gam.Core.Tools;

/// <summary>
/// Handles tool calls from AI models and executes the corresponding GAM operations.
/// Compatible with OpenAI function calling, AI SDK, and similar frameworks.
/// </summary>
public class GamToolHandler
{
    private readonly IGamService _gam;

    public GamToolHandler(IGamService gam)
    {
        _gam = gam;
    }

    /// <summary>
    /// Execute a tool call by name with JSON arguments.
    /// </summary>
    /// <param name="toolName">The tool name (e.g., "gam_memorize")</param>
    /// <param name="argumentsJson">JSON string of arguments</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Tool result as a string (for returning to the model)</returns>
    public async Task<ToolResult> ExecuteAsync(string toolName, string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            var args = JsonSerializer.Deserialize<JsonElement>(argumentsJson);

            return toolName switch
            {
                "gam_memorize" => await ExecuteMemorizeAsync(args, ct),
                "gam_research" => await ExecuteResearchAsync(args, ct),
                "gam_forget" => await ExecuteForgetAsync(args, ct),
                _ => new ToolResult { Success = false, Error = $"Unknown tool: {toolName}" }
            };
        }
        catch (JsonException ex)
        {
            return new ToolResult { Success = false, Error = $"Invalid JSON arguments: {ex.Message}" };
        }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = $"Tool execution failed: {ex.Message}" };
        }
    }

    private async Task<ToolResult> ExecuteMemorizeAsync(JsonElement args, CancellationToken ct)
    {
        var ownerId = args.GetProperty("owner_id").GetString()
            ?? throw new ArgumentException("owner_id is required");
        var userMessage = args.GetProperty("user_message").GetString()
            ?? throw new ArgumentException("user_message is required");
        var assistantMessage = args.GetProperty("assistant_message").GetString()
            ?? throw new ArgumentException("assistant_message is required");

        await _gam.MemorizeAsync(new MemorizeRequest
        {
            Turn = new ConversationTurn
            {
                OwnerId = ownerId,
                UserMessage = userMessage,
                AssistantMessage = assistantMessage,
                Timestamp = DateTimeOffset.UtcNow
            }
        }, ct);

        return new ToolResult
        {
            Success = true,
            Content = "Memory stored successfully."
        };
    }

    private async Task<ToolResult> ExecuteResearchAsync(JsonElement args, CancellationToken ct)
    {
        var ownerId = args.GetProperty("owner_id").GetString()
            ?? throw new ArgumentException("owner_id is required");
        var query = args.GetProperty("query").GetString()
            ?? throw new ArgumentException("query is required");

        var options = new ResearchOptions();
        if (args.TryGetProperty("max_tokens", out var maxTokens))
        {
            options = options with { MaxContextTokens = maxTokens.GetInt32() };
        }

        var context = await _gam.ResearchAsync(new ResearchRequest
        {
            OwnerId = ownerId,
            Query = query,
            Options = options
        }, ct);

        if (context.Pages.Count == 0)
        {
            return new ToolResult
            {
                Success = true,
                Content = "No relevant memories found.",
                Metadata = new Dictionary<string, object>
                {
                    ["pages_found"] = 0,
                    ["tokens"] = 0,
                    ["duration_ms"] = context.Duration.TotalMilliseconds
                }
            };
        }

        return new ToolResult
        {
            Success = true,
            Content = context.FormatForPrompt(),
            Metadata = new Dictionary<string, object>
            {
                ["pages_found"] = context.Pages.Count,
                ["tokens"] = context.TotalTokens,
                ["iterations"] = context.IterationsPerformed,
                ["duration_ms"] = context.Duration.TotalMilliseconds
            }
        };
    }

    private async Task<ToolResult> ExecuteForgetAsync(JsonElement args, CancellationToken ct)
    {
        var ownerId = args.GetProperty("owner_id").GetString()
            ?? throw new ArgumentException("owner_id is required");

        var request = new ForgetRequest { OwnerId = ownerId };

        if (args.TryGetProperty("all", out var all) && all.GetBoolean())
        {
            request = request with { All = true };
        }

        if (args.TryGetProperty("before", out var before))
        {
            request = request with { Before = DateTimeOffset.Parse(before.GetString()!) };
        }

        await _gam.ForgetAsync(request, ct);

        return new ToolResult
        {
            Success = true,
            Content = "Memories deleted successfully."
        };
    }
}

/// <summary>
/// Result of a tool execution.
/// </summary>
public class ToolResult
{
    /// <summary>Whether the tool executed successfully.</summary>
    public bool Success { get; init; }

    /// <summary>The content to return to the model.</summary>
    public string Content { get; init; } = "";

    /// <summary>Error message if Success is false.</summary>
    public string? Error { get; init; }

    /// <summary>Optional metadata about the operation.</summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>Serialize to JSON for API responses.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    });
}
