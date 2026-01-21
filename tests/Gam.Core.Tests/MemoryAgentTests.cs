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
        _promptProviderMock.Setup(x => x.BuildMemoryUserPrompt(It.IsAny<MemoryInput>())).Returns("User prompt");
        
        _agent = new MemoryAgent(_llmMock.Object, _embeddingMock.Object, _promptProviderMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task CreatePageAsync_ShouldGenerateEmbedding()
    {
        // Arrange
        var input = new MemoryInput
        {
            OwnerId = "test-user",
            Content = "User: Hello\nAssistant: Hi there!",
            Timestamp = DateTimeOffset.UtcNow
        };

        var expectedEmbedding = new float[] { 0.1f, 0.2f, 0.3f };
        _embeddingMock
            .Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedEmbedding);

        // Act
        var page = await _agent.CreatePageAsync(input);

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
        var input = new MemoryInput
        {
            OwnerId = "test-user",
            Content = "User: How do I use Docker?\nAssistant: Docker is a containerization platform...",
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
        var result = await _agent.GenerateAbstractAsync(input);

        // Assert
        result.Should().NotBeNull();
        result.Abstract.Summary.Should().Contain("Docker");
        result.Abstract.Headers.Should().Contain("Docker");
        result.Abstract.Headers.Should().Contain("containerization");
        result.Abstract.SummaryEmbedding.Should().BeEquivalentTo(expectedEmbedding);
        // Legacy format defaults
        result.Abstract.Type.Should().Be(MemoryType.Conversation);
        result.Importance.Should().Be(0.5f);
    }
    
    [Fact]
    public async Task GenerateAbstractAsync_ShouldParseJsonResponse()
    {
        // Arrange
        var input = new MemoryInput
        {
            OwnerId = "test-user",
            Content = "User: Let's use PostgreSQL for the database\nAssistant: PostgreSQL is a great choice for ACID compliance...",
            Timestamp = DateTimeOffset.UtcNow
        };

        var llmResponse = new LlmResponse
        {
            Content = """
                {
                  "summary": "Decision to use PostgreSQL for database due to ACID compliance.",
                  "headers": ["PostgreSQL", "database decision", "ACID"],
                  "type": "decision",
                  "importance": 0.85,
                  "tags": ["entity:tool:postgresql", "keyword:database", "keyword:acid"]
                }
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
        var result = await _agent.GenerateAbstractAsync(input);

        // Assert
        result.Should().NotBeNull();
        result.Abstract.Summary.Should().Contain("PostgreSQL");
        result.Abstract.Headers.Should().Contain("PostgreSQL");
        result.Abstract.Type.Should().Be(MemoryType.Decision);
        result.Importance.Should().BeApproximately(0.85f, 0.01f);
        result.Abstract.Tags.Should().Contain("entity:tool:postgresql");
        result.Abstract.Tags.Should().Contain("keyword:database");
    }
}
