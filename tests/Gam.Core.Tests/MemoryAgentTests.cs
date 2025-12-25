using FluentAssertions;
using Gam.Core.Abstractions;
using Gam.Core.Agents;
using Gam.Core.Models;
using Gam.Core.Prompts;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Gam.Core.Tests;

public class MemoryAgentTests
{
    private readonly Mock<ILlmProvider> _llmMock;
    private readonly Mock<IEmbeddingProvider> _embeddingMock;
    private readonly Mock<IPromptProvider> _promptProviderMock;
    private readonly Mock<ILogger<MemoryAgent>> _loggerMock;
    private readonly MemoryAgent _agent;

    public MemoryAgentTests()
    {
        _llmMock = new Mock<ILlmProvider>();
        _embeddingMock = new Mock<IEmbeddingProvider>();
        _promptProviderMock = new Mock<IPromptProvider>();
        _loggerMock = new Mock<ILogger<MemoryAgent>>();
        
        // Setup default prompt provider behavior
        _promptProviderMock.Setup(x => x.GetMemorySystemPrompt()).Returns("System prompt");
        _promptProviderMock.Setup(x => x.BuildMemoryUserPrompt(It.IsAny<ConversationTurn>())).Returns("User prompt");
        
        _agent = new MemoryAgent(_llmMock.Object, _embeddingMock.Object, _promptProviderMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task CreatePageAsync_ShouldGenerateEmbedding()
    {
        // Arrange
        var turn = new ConversationTurn
        {
            OwnerId = "test-user",
            UserMessage = "Hello",
            AssistantMessage = "Hi there!",
            Timestamp = DateTimeOffset.UtcNow
        };

        var expectedEmbedding = new float[] { 0.1f, 0.2f, 0.3f };
        _embeddingMock
            .Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedEmbedding);

        // Act
        var page = await _agent.CreatePageAsync(turn);

        // Assert
        page.Should().NotBeNull();
        page.OwnerId.Should().Be("test-user");
        page.Content.Should().Contain("Hello");
        page.Content.Should().Contain("Hi there!");
        page.Embedding.Should().BeEquivalentTo(expectedEmbedding);
        page.TokenCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GenerateAbstractAsync_ShouldParseResponse()
    {
        // Arrange
        var turn = new ConversationTurn
        {
            OwnerId = "test-user",
            UserMessage = "How do I use Docker?",
            AssistantMessage = "Docker is a containerization platform...",
            Timestamp = DateTimeOffset.UtcNow
        };

        var llmResponse = new LlmResponse
        {
            Content = """
                SUMMARY: User asked about Docker containerization.
                HEADERS:
                - Docker
                - containerization
                - getting started
                """,
            PromptTokens = 100,
            CompletionTokens = 50
        };

        _llmMock
            .Setup(x => x.CompleteAsync(It.IsAny<IReadOnlyList<LlmMessage>>(), It.IsAny<LlmOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        var expectedEmbedding = new float[] { 0.1f, 0.2f };
        _embeddingMock
            .Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedEmbedding);

        // Act
        var abstractResult = await _agent.GenerateAbstractAsync(turn);

        // Assert
        abstractResult.Should().NotBeNull();
        abstractResult.Summary.Should().Contain("Docker");
        abstractResult.Headers.Should().Contain("Docker");
        abstractResult.Headers.Should().Contain("containerization");
        abstractResult.SummaryEmbedding.Should().BeEquivalentTo(expectedEmbedding);
    }
}
