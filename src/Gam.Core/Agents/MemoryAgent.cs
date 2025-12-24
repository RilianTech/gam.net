using System.Text;
using Gam.Core.Abstractions;
using Gam.Core.Models;
using Gam.Core.Prompts;
using Microsoft.Extensions.Logging;

namespace Gam.Core.Agents;

/// <summary>
/// Processes conversation turns into memory pages.
/// Runs offline (not in the critical path of user requests).
/// </summary>
public class MemoryAgent : IMemoryAgent
{
    private readonly ILlmProvider _llm;
    private readonly IEmbeddingProvider _embedding;
    private readonly ILogger<MemoryAgent> _logger;

    public MemoryAgent(
        ILlmProvider llm,
        IEmbeddingProvider embedding,
        ILogger<MemoryAgent> logger)
    {
        _llm = llm;
        _embedding = embedding;
        _logger = logger;
    }

    public async Task<MemoryAbstract> GenerateAbstractAsync(
        ConversationTurn turn,
        CancellationToken ct = default)
    {
        var prompt = MemoryPrompts.BuildAbstractPrompt(turn);
        
        var messages = new List<LlmMessage>
        {
            new(LlmRole.System, MemoryPrompts.AbstractSystemPrompt),
            new(LlmRole.User, prompt)
        };

        _logger.LogDebug("Generating abstract for conversation turn from {OwnerId}", turn.OwnerId);

        var response = await _llm.CompleteAsync(messages, new LlmOptions
        {
            Temperature = 0.3f,  // Low temperature for consistent extraction
            MaxTokens = 1000
        }, ct);

        var parsed = ParseAbstractResponse(response.Content);
        
        // Generate embedding for the summary
        var summaryEmbedding = await _embedding.EmbedAsync(parsed.Summary, ct);

        _logger.LogDebug("Generated abstract with {HeaderCount} headers", parsed.Headers.Count);

        return new MemoryAbstract
        {
            PageId = Guid.NewGuid(),  // Will be set when creating page
            OwnerId = turn.OwnerId,
            Summary = parsed.Summary,
            Headers = parsed.Headers,
            CreatedAt = DateTimeOffset.UtcNow,
            SummaryEmbedding = summaryEmbedding
        };
    }

    public async Task<MemoryPage> CreatePageAsync(
        ConversationTurn turn,
        CancellationToken ct = default)
    {
        var pageId = Guid.NewGuid();
        var content = FormatPageContent(turn);
        var tokenCount = EstimateTokenCount(content);
        
        _logger.LogDebug("Creating memory page for {OwnerId}, ~{TokenCount} tokens", turn.OwnerId, tokenCount);
        
        // Generate embedding for the full content
        var embedding = await _embedding.EmbedAsync(content, ct);

        return new MemoryPage
        {
            Id = pageId,
            OwnerId = turn.OwnerId,
            Content = content,
            TokenCount = tokenCount,
            CreatedAt = DateTimeOffset.UtcNow,
            Embedding = embedding,
            Metadata = turn.Metadata
        };
    }

    private static string FormatPageContent(ConversationTurn turn)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"[Conversation on {turn.Timestamp:yyyy-MM-dd HH:mm}]");
        sb.AppendLine();
        sb.AppendLine($"User: {turn.UserMessage}");
        sb.AppendLine();
        sb.AppendLine($"Assistant: {turn.AssistantMessage}");
        
        if (turn.ToolCalls is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Tool Calls:");
            foreach (var tool in turn.ToolCalls)
            {
                sb.AppendLine($"  - {tool.ToolName}: {tool.Result}");
            }
        }
        
        return sb.ToString();
    }

    private static (string Summary, IReadOnlyList<string> Headers) ParseAbstractResponse(string response)
    {
        // Expected format:
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

        return (summary, headers);
    }

    private static int EstimateTokenCount(string text)
    {
        // Rough estimate: ~4 chars per token for English
        return text.Length / 4;
    }
}
