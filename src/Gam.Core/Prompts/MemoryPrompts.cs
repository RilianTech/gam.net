using System.Text;
using Gam.Core.Models;

namespace Gam.Core.Prompts;

/// <summary>
/// Prompts used by the MemoryAgent for abstract generation.
/// Based on the original GAM paper: https://arxiv.org/abs/2511.18423
/// </summary>
public static class MemoryPrompts
{
    /// <summary>
    /// System prompt for the Memory Agent (Memorizer) from the GAM paper.
    /// </summary>
    public const string AbstractSystemPrompt = """
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

    /// <summary>
    /// Build the user prompt for abstract generation.
    /// </summary>
    public static string BuildAbstractPrompt(ConversationTurn turn)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("Write an abstract for the following page to be added to the library:");
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
        sb.AppendLine("Generate the SUMMARY and HEADERS for this page:");
        
        return sb.ToString();
    }
}
