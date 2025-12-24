namespace Gam.Core.Abstractions;

/// <summary>
/// Abstraction for LLM calls. GAM uses this for both MemoryAgent and ResearchAgent.
/// </summary>
public interface ILlmProvider
{
    /// <summary>
    /// Generate a completion for the given messages.
    /// </summary>
    Task<LlmResponse> CompleteAsync(
        IReadOnlyList<LlmMessage> messages, 
        LlmOptions? options = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Generate a completion with streaming.
    /// </summary>
    IAsyncEnumerable<string> CompleteStreamAsync(
        IReadOnlyList<LlmMessage> messages, 
        LlmOptions? options = null,
        CancellationToken ct = default);
}

/// <summary>
/// A message in an LLM conversation.
/// </summary>
public record LlmMessage(LlmRole Role, string Content);

/// <summary>
/// Role of a message sender.
/// </summary>
public enum LlmRole 
{ 
    System, 
    User, 
    Assistant 
}

/// <summary>
/// Options for LLM completion.
/// </summary>
public record LlmOptions
{
    public float Temperature { get; init; } = 0.7f;
    public int? MaxTokens { get; init; }
    public string? Model { get; init; }
}

/// <summary>
/// Response from an LLM completion.
/// </summary>
public record LlmResponse
{
    public required string Content { get; init; }
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public string? Model { get; init; }
}
