using FluentAssertions;
using Gam.Core;
using Gam.Core.Abstractions;
using Gam.Core.Models;
using Gam.Storage.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gam.Integration.Tests;

/// <summary>
/// Integration tests for GAM service with real PostgreSQL.
/// Uses Testcontainers to spin up a PostgreSQL instance with pgvector.
/// </summary>
public class GamServiceIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private ServiceProvider? _serviceProvider;

    public async Task InitializeAsync()
    {
        // Start PostgreSQL container with pgvector
        _postgres = new PostgreSqlBuilder()
            .WithImage("pgvector/pgvector:pg16")
            .Build();
        
        await _postgres.StartAsync();

        // Run migrations
        var connectionString = _postgres.GetConnectionString();
        // Note: In real setup, run the SQL migration script here

        // Setup DI
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddGamCore();
        services.AddGamPostgresStorage(connectionString);
        
        // Mock LLM and Embedding providers for testing
        var llmMock = new Mock<ILlmProvider>();
        llmMock.Setup(x => x.CompleteAsync(It.IsAny<IReadOnlyList<LlmMessage>>(), It.IsAny<LlmOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse
            {
                Content = "SUMMARY: Test summary\nHEADERS:\n- test\n- header",
                PromptTokens = 10,
                CompletionTokens = 5
            });

        var embeddingMock = new Mock<IEmbeddingProvider>();
        embeddingMock.Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, 1536).Select(i => (float)i / 1536).ToArray());
        embeddingMock.Setup(x => x.Dimensions).Returns(1536);

        services.AddSingleton(llmMock.Object);
        services.AddSingleton(embeddingMock.Object);

        _serviceProvider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        if (_postgres != null)
            await _postgres.DisposeAsync();
        
        _serviceProvider?.Dispose();
    }

    [Fact(Skip = "Requires Docker and pgvector image")]
    public async Task MemorizeAndResearch_ShouldWork()
    {
        // Arrange
        var gam = _serviceProvider!.GetRequiredService<IGamService>();
        var ownerId = $"test-user-{Guid.NewGuid()}";

        // Act - Memorize
        await gam.MemorizeAsync(new MemorizeRequest
        {
            Turn = new ConversationTurn
            {
                OwnerId = ownerId,
                UserMessage = "What is Kubernetes?",
                AssistantMessage = "Kubernetes is a container orchestration platform.",
                Timestamp = DateTimeOffset.UtcNow
            }
        });

        // Act - Research
        var context = await gam.ResearchAsync(new ResearchRequest
        {
            OwnerId = ownerId,
            Query = "container orchestration"
        });

        // Assert
        context.Should().NotBeNull();
        context.Pages.Count.Should().BeGreaterThan(0);
        context.Pages.First().Content.Should().Contain("Kubernetes");
    }
}
