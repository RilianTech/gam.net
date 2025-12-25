using Gam.Core.Models;

namespace Gam.Core.Prompts;

/// <summary>
/// Provides prompts for GAM agents. Implement this interface to customize prompts.
/// </summary>
public interface IPromptProvider
{
    // Memory Agent prompts
    string GetMemorySystemPrompt();
    string BuildMemoryUserPrompt(ConversationTurn turn);
    
    // Research Agent prompts
    string GetPlanSystemPrompt();
    string BuildPlanUserPrompt(string query, List<RetrievedPage> pages);
    string GetReflectSystemPrompt();
    string BuildReflectUserPrompt(string query, List<RetrievedPage> pages);
}

/// <summary>
/// Configuration for customizing prompts.
/// </summary>
public class PromptOptions
{
    /// <summary>
    /// Optional: Path to a directory containing prompt template files.
    /// Files should be named: memory_system.txt, plan_system.txt, reflect_system.txt
    /// </summary>
    public string? PromptDirectory { get; set; }

    /// <summary>
    /// Optional: Override the memory system prompt directly.
    /// </summary>
    public string? MemorySystemPrompt { get; set; }

    /// <summary>
    /// Optional: Override the plan system prompt directly.
    /// </summary>
    public string? PlanSystemPrompt { get; set; }

    /// <summary>
    /// Optional: Override the reflect system prompt directly.
    /// </summary>
    public string? ReflectSystemPrompt { get; set; }

    /// <summary>
    /// Whether to include tool call details in memory prompts. Default: true
    /// </summary>
    public bool IncludeToolCalls { get; set; } = true;

    /// <summary>
    /// Whether to include conversation ID in memory prompts. Default: true
    /// </summary>
    public bool IncludeConversationId { get; set; } = true;

    /// <summary>
    /// Maximum characters of content preview in plan/reflect prompts. Default: 200
    /// </summary>
    public int ContentPreviewLength { get; set; } = 200;

    /// <summary>
    /// Maximum pages to show in plan prompt. Default: 5
    /// </summary>
    public int MaxPagesInPlanPrompt { get; set; } = 5;

    /// <summary>
    /// Maximum pages to show in reflect prompt. Default: 7
    /// </summary>
    public int MaxPagesInReflectPrompt { get; set; } = 7;
}
