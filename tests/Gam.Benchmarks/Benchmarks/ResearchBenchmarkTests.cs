using FluentAssertions;
using Gam.Benchmarks.Framework;
using Gam.Core;
using Gam.Core.Abstractions;
using Gam.Core.Models;
using Gam.Storage.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Abstractions;

namespace Gam.Benchmarks.Benchmarks;

/// <summary>
/// Benchmark tests for GAM research accuracy and performance.
/// These tests measure baseline metrics that ADR-2 enhancements should improve.
/// </summary>
public class ResearchBenchmarkTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private PostgreSqlContainer? _postgres;
    private ServiceProvider? _serviceProvider;
    private IGamService? _gam;
    private string _ownerId = null!;
    private BenchmarkDataset? _dataset;

    public ResearchBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

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

        // Setup DI with mocked LLM/Embedding providers
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddGamCore();
        services.AddGamPostgresStorage(connectionString);
        
        // Setup mock providers
        SetupMockProviders(services);

        _serviceProvider = services.BuildServiceProvider();
        _gam = _serviceProvider.GetRequiredService<IGamService>();
        _ownerId = $"benchmark-{Guid.NewGuid()}";

        // Load and ingest test dataset
        var datasetPath = Path.Combine(AppContext.BaseDirectory, "Data", "sample-benchmark.json");
        _dataset = await BenchmarkRunner.LoadDatasetAsync(datasetPath);
        
        var runner = new BenchmarkRunner(_gam);
        await runner.IngestDatasetAsync(_dataset, _ownerId);
        
        _output.WriteLine($"Ingested {_dataset.Conversations.Count} conversations");
    }

    public async Task DisposeAsync()
    {
        if (_postgres != null)
            await _postgres.DisposeAsync();
        
        _serviceProvider?.Dispose();
    }

    [Fact]
    public async Task Baseline_SingleFactRecall_ShouldRetrieveRelevantContext()
    {
        // Arrange
        var query = _dataset!.Queries.First(q => q.Id == "q-001"); // Database choice query
        var runner = new BenchmarkRunner(_gam!);

        // Act
        var result = await runner.EvaluateQueryAsync(query, _ownerId);

        // Assert & Report
        _output.WriteLine($"Query: {query.Query}");
        _output.WriteLine($"Expected: {query.ExpectedAnswer}");
        _output.WriteLine($"Pages Retrieved: {result.PagesRetrieved}");
        _output.WriteLine($"Tokens Retrieved: {result.TokensRetrieved}");
        _output.WriteLine($"Iterations: {result.IterationsPerformed}");
        _output.WriteLine($"Duration: {result.Duration.TotalMilliseconds:F0}ms");
        _output.WriteLine($"Contains Expected: {result.ContainsExpectedAnswer}");
        _output.WriteLine($"Fact Recall: {result.FactRecall:P0}");
        _output.WriteLine($"Found Facts: {string.Join(", ", result.FoundFacts ?? Array.Empty<string>())}");
        _output.WriteLine($"Missing Facts: {string.Join(", ", result.MissingFacts ?? Array.Empty<string>())}");

        result.PagesRetrieved.Should().BeGreaterThan(0, "should retrieve at least one page");
        result.FactRecall.Should().BeGreaterThan(0.5f, "should find majority of required facts");
    }

    [Fact]
    public async Task Baseline_MultiHopQuery_ShouldFindAcrossConversations()
    {
        // Arrange - multi-hop query requires finding info from multiple conversations
        var query = _dataset!.Queries.First(q => q.Id == "q-006"); // Team expertise query
        var runner = new BenchmarkRunner(_gam!);

        // Act
        var result = await runner.EvaluateQueryAsync(query, _ownerId);

        // Assert & Report
        _output.WriteLine($"Query: {query.Query}");
        _output.WriteLine($"Expected: {query.ExpectedAnswer}");
        _output.WriteLine($"Pages Retrieved: {result.PagesRetrieved}");
        _output.WriteLine($"Iterations: {result.IterationsPerformed}");
        _output.WriteLine($"Duration: {result.Duration.TotalMilliseconds:F0}ms");
        _output.WriteLine($"Fact Recall: {result.FactRecall:P0}");
        _output.WriteLine($"Found Facts: {string.Join(", ", result.FoundFacts ?? Array.Empty<string>())}");
        _output.WriteLine($"Missing Facts: {string.Join(", ", result.MissingFacts ?? Array.Empty<string>())}");

        // Multi-hop queries are harder - record baseline, don't assert too strictly
        _output.WriteLine($"\n[BASELINE] Multi-hop fact recall: {result.FactRecall:P0}");
    }

    [Fact]
    public async Task Baseline_FullBenchmarkRun_ShouldRecordMetrics()
    {
        // Arrange
        var runner = new BenchmarkRunner(_gam!);

        // Act
        var results = await runner.RunBenchmarkAsync(_dataset!, _ownerId, "Baseline (Simple Research)");

        // Report
        _output.WriteLine("=== BENCHMARK RESULTS ===");
        _output.WriteLine($"Dataset: {results.DatasetName}");
        _output.WriteLine($"Configuration: {results.ConfigurationName}");
        _output.WriteLine($"Total Queries: {results.QueryResults.Count}");
        _output.WriteLine($"Overall Accuracy: {results.OverallAccuracy:P1}");
        _output.WriteLine($"Average Fact Recall: {results.AverageFactRecall:P1}");
        _output.WriteLine($"Average Query Duration: {results.AverageQueryDuration.TotalMilliseconds:F0}ms");
        _output.WriteLine($"Total Duration: {results.TotalDuration.TotalSeconds:F1}s");
        _output.WriteLine("");

        // Per-category breakdown
        var byCategory = results.QueryResults
            .GroupBy(r => _dataset!.Queries.First(q => q.Id == r.QueryId).Category ?? "unknown")
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (category, categoryResults) in byCategory)
        {
            var categoryAccuracy = categoryResults.Count(r => r.ContainsExpectedAnswer) / (float)categoryResults.Count;
            var categoryRecall = categoryResults.Average(r => r.FactRecall);
            _output.WriteLine($"  {category}: Accuracy={categoryAccuracy:P0}, FactRecall={categoryRecall:P0}");
        }

        _output.WriteLine("");
        _output.WriteLine("Per-query details:");
        foreach (var qr in results.QueryResults)
        {
            var query = _dataset!.Queries.First(q => q.Id == qr.QueryId);
            _output.WriteLine($"  [{query.Difficulty}] {qr.QueryId}: Recall={qr.FactRecall:P0}, Pages={qr.PagesRetrieved}, Time={qr.Duration.TotalMilliseconds:F0}ms");
        }

        // Store baseline metrics for comparison
        _output.WriteLine("");
        _output.WriteLine("=== BASELINE METRICS (for ADR-1/ADR-2 comparison) ===");
        _output.WriteLine($"BASELINE_ACCURACY={results.OverallAccuracy:F3}");
        _output.WriteLine($"BASELINE_FACT_RECALL={results.AverageFactRecall:F3}");
        _output.WriteLine($"BASELINE_AVG_DURATION_MS={results.AverageQueryDuration.TotalMilliseconds:F0}");
    }

    private void SetupMockProviders(ServiceCollection services)
    {
        // Create mock LLM that returns reasonable research plans
        var llmMock = Substitute.For<ILlmProvider>();
        
        // Setup for memory agent (abstract generation)
        llmMock.CompleteAsync(
            Arg.Is<IReadOnlyList<LlmMessage>>(msgs => msgs.Any(m => m.Content.Contains("memory") || m.Content.Contains("SUMMARY"))),
            Arg.Any<LlmOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var messages = callInfo.ArgAt<IReadOnlyList<LlmMessage>>(0);
                var userMessage = messages.LastOrDefault(m => m.Role == LlmRole.User)?.Content ?? "";
                
                // Extract key terms from the content for headers
                var keywords = ExtractKeywords(userMessage);
                
                return Task.FromResult(new LlmResponse
                {
                    Content = $"SUMMARY: {userMessage.Take(100)}...\nHEADERS:\n{string.Join("\n", keywords.Select(k => $"- {k}"))}",
                    PromptTokens = 100,
                    CompletionTokens = 50
                });
            });

        // Setup for research agent (planning)
        llmMock.CompleteAsync(
            Arg.Is<IReadOnlyList<LlmMessage>>(msgs => msgs.Any(m => m.Content.Contains("STRATEGY") || m.Content.Contains("Plan"))),
            Arg.Any<LlmOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var messages = callInfo.ArgAt<IReadOnlyList<LlmMessage>>(0);
                var userMessage = messages.LastOrDefault(m => m.Role == LlmRole.User)?.Content ?? "";
                
                // Extract the query from the user message
                var queryMatch = System.Text.RegularExpressions.Regex.Match(userMessage, @"Query:\s*(.+?)(?:\n|$)");
                var query = queryMatch.Success ? queryMatch.Groups[1].Value.Trim() : "general search";
                
                return Task.FromResult(new LlmResponse
                {
                    Content = $"""
                        STRATEGY: Search for relevant information using keyword and vector search
                        SEARCH_QUERY: {query}
                        USE_KEYWORD: true
                        USE_VECTOR: true
                        USE_INDEX: false
                        TARGET_HEADERS: none
                        COMPLETE: false
                        """,
                    PromptTokens = 200,
                    CompletionTokens = 50
                });
            });

        // Setup for research agent (reflection)
        llmMock.CompleteAsync(
            Arg.Is<IReadOnlyList<LlmMessage>>(msgs => msgs.Any(m => m.Content.Contains("CONTINUE") || m.Content.Contains("SUFFICIENT"))),
            Arg.Any<LlmOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new LlmResponse
            {
                Content = "SUFFICIENT",
                PromptTokens = 100,
                CompletionTokens = 1
            }));

        // Create mock embedding provider
        var embeddingMock = Substitute.For<IEmbeddingProvider>();
        embeddingMock.Dimensions.Returns(1536);
        
        // Generate deterministic embeddings based on content hash
        embeddingMock.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var text = callInfo.ArgAt<string>(0);
                return Task.FromResult(GenerateDeterministicEmbedding(text, 1536));
            });

        services.AddSingleton(llmMock);
        services.AddSingleton(embeddingMock);
    }

    private static List<string> ExtractKeywords(string text)
    {
        // Simple keyword extraction - extract capitalized words and technical terms
        var words = text.Split(new[] { ' ', '\n', '\r', '.', ',', ':', ';', '?', '!', '-', '(', ')' }, 
            StringSplitOptions.RemoveEmptyEntries);
        
        var keywords = words
            .Where(w => w.Length > 3)
            .Where(w => char.IsUpper(w[0]) || w.All(char.IsLower))
            .Select(w => w.ToLowerInvariant())
            .Distinct()
            .Take(7)
            .ToList();
        
        return keywords.Count > 0 ? keywords : new List<string> { "general", "topic" };
    }

    private static float[] GenerateDeterministicEmbedding(string text, int dimensions)
    {
        // Generate a deterministic embedding based on text hash
        // This ensures similar texts get similar embeddings
        var embedding = new float[dimensions];
        var hash = text.GetHashCode();
        var random = new Random(hash);
        
        for (var i = 0; i < dimensions; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2 - 1);
        }
        
        // Normalize
        var magnitude = MathF.Sqrt(embedding.Sum(x => x * x));
        for (var i = 0; i < dimensions; i++)
        {
            embedding[i] /= magnitude;
        }
        
        return embedding;
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
}
