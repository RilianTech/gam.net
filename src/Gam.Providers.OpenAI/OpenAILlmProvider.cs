using System.Runtime.CompilerServices;
using Gam.Core.Abstractions;
using OpenAI;
using OpenAI.Chat;

namespace Gam.Providers.OpenAI;

/// <summary>
/// OpenAI LLM provider implementation.
/// </summary>
public class OpenAILlmProvider : ILlmProvider
{
    private readonly OpenAIClient _client;
    private readonly string _defaultModel;

    public OpenAILlmProvider(OpenAIClient client, string defaultModel = "gpt-4o")
    {
        _client = client;
        _defaultModel = defaultModel;
    }

    public OpenAILlmProvider(string apiKey, string defaultModel = "gpt-4o")
        : this(new OpenAIClient(apiKey), defaultModel)
    {
    }

    public async Task<LlmResponse> CompleteAsync(
        IReadOnlyList<LlmMessage> messages,
        LlmOptions? options = null,
        CancellationToken ct = default)
    {
        var model = options?.Model ?? _defaultModel;
        var chatClient = _client.GetChatClient(model);
        
        var chatMessages = messages.Select(MapMessage).ToList();

        var chatOptions = new ChatCompletionOptions
        {
            Temperature = options?.Temperature ?? 0.7f,
            MaxOutputTokenCount = options?.MaxTokens
        };

        var response = await chatClient.CompleteChatAsync(chatMessages, chatOptions, ct);
        
        return new LlmResponse
        {
            Content = response.Value.Content[0].Text,
            PromptTokens = response.Value.Usage.InputTokenCount,
            CompletionTokens = response.Value.Usage.OutputTokenCount,
            Model = model
        };
    }

    public async IAsyncEnumerable<string> CompleteStreamAsync(
        IReadOnlyList<LlmMessage> messages,
        LlmOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = options?.Model ?? _defaultModel;
        var chatClient = _client.GetChatClient(model);
        
        var chatMessages = messages.Select(MapMessage).ToList();

        var chatOptions = new ChatCompletionOptions
        {
            Temperature = options?.Temperature ?? 0.7f,
            MaxOutputTokenCount = options?.MaxTokens
        };

        await foreach (var update in chatClient.CompleteChatStreamingAsync(
            chatMessages, chatOptions, ct))
        {
            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                    yield return part.Text;
            }
        }
    }

    private static ChatMessage MapMessage(LlmMessage m) => m.Role switch
    {
        LlmRole.System => new SystemChatMessage(m.Content),
        LlmRole.User => new UserChatMessage(m.Content),
        LlmRole.Assistant => new AssistantChatMessage(m.Content),
        _ => throw new ArgumentException($"Unknown role: {m.Role}")
    };
}
