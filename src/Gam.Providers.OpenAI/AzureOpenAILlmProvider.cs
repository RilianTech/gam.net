using System.ClientModel;
using System.Runtime.CompilerServices;
using Azure.AI.OpenAI;
using Gam.Core.Abstractions;
using OpenAI.Chat;

namespace Gam.Providers.OpenAI;

/// <summary>
/// Azure OpenAI LLM provider implementation.
/// </summary>
public class AzureOpenAILlmProvider : ILlmProvider
{
    private readonly AzureOpenAIClient _client;
    private readonly string _deploymentName;

    public AzureOpenAILlmProvider(AzureOpenAIClient client, string deploymentName)
    {
        _client = client;
        _deploymentName = deploymentName;
    }

    public AzureOpenAILlmProvider(string endpoint, string apiKey, string deploymentName)
        : this(new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey)), deploymentName)
    {
    }

    public async Task<LlmResponse> CompleteAsync(
        IReadOnlyList<LlmMessage> messages,
        LlmOptions? options = null,
        CancellationToken ct = default)
    {
        var chatClient = _client.GetChatClient(_deploymentName);
        
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
            Model = _deploymentName
        };
    }

    public async IAsyncEnumerable<string> CompleteStreamAsync(
        IReadOnlyList<LlmMessage> messages,
        LlmOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var chatClient = _client.GetChatClient(_deploymentName);
        
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
