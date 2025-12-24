using System.Text;
using Gam.Core.Models;

namespace Gam.Core.Prompts;

/// <summary>
/// Prompts used by the MemoryAgent for abstract generation.
/// </summary>
public static class MemoryPrompts
{
    public const string AbstractSystemPrompt = """
        You are a memory indexing assistant. Your job is to analyze conversation turns 
        and extract a concise summary and searchable headers.
        
        Guidelines:
        - Summary should be 1-2 sentences capturing the key information exchanged
        - Headers should be searchable keywords/phrases (3-7 headers typically)
        - Headers should include: topics discussed, entities mentioned, actions taken
        - Be specific - "user's Python debugging issue" not just "programming"
        - Include temporal references if relevant ("weekly standup", "Q4 planning")
        
        Output format:
        SUMMARY: <your summary>
        HEADERS:
        - <header 1>
        - <header 2>
        - ...
        """;

    public static string BuildAbstractPrompt(ConversationTurn turn)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("Analyze this conversation turn and extract summary + headers:");
        sb.AppendLine();
        sb.AppendLine($"Timestamp: {turn.Timestamp:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine($"User: {turn.UserMessage}");
        sb.AppendLine();
        sb.AppendLine($"Assistant: {turn.AssistantMessage}");
        
        if (turn.ToolCalls is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Tool calls made:");
            foreach (var tool in turn.ToolCalls)
            {
                sb.AppendLine($"  - {tool.ToolName}");
            }
        }
        
        return sb.ToString();
    }
}
