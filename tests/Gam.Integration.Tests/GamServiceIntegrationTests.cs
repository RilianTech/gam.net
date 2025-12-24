using FluentAssertions;
using Gam.Core;
using Gam.Core.Abstractions;
using Gam.Core.Models;
using Gam.Storage.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
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
            .WithImage("pgvector/pgvector:pg17")
            .Build();
        
        await _postgres.StartAsync();

        // Run migrations
        var connectionString = _postgres.GetConnectionString();
        await RunMigrationsAsync(connectionString);

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

    private static async Task RunMigrationsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        // Create pgvector extension
        await using var cmdExtension = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector;", conn);
        await cmdExtension.ExecuteNonQueryAsync();

        // Create tables
        const string createTables = """
            CREATE TABLE IF NOT EXISTS memory_pages (
                id UUID PRIMARY KEY,
                owner_id VARCHAR(255) NOT NULL,
                content TEXT NOT NULL,
                token_count INTEGER NOT NULL,
                embedding vector(1536),
                metadata JSONB,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS memory_abstracts (
                page_id UUID PRIMARY KEY REFERENCES memory_pages(id) ON DELETE CASCADE,
                owner_id VARCHAR(255) NOT NULL,
                summary TEXT NOT NULL,
                headers TEXT[] NOT NULL,
                summary_embedding vector(1536),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE INDEX IF NOT EXISTS idx_pages_owner ON memory_pages(owner_id);
            CREATE INDEX IF NOT EXISTS idx_pages_created ON memory_pages(created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_abstracts_owner ON memory_abstracts(owner_id);
            CREATE INDEX IF NOT EXISTS idx_pages_content_fts ON memory_pages USING gin(to_tsvector('english', content));
            CREATE INDEX IF NOT EXISTS idx_abstracts_headers ON memory_abstracts USING gin(headers);
            """;

        await using var cmdTables = new NpgsqlCommand(createTables, conn);
        await cmdTables.ExecuteNonQueryAsync();
    }

    [Fact]
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
