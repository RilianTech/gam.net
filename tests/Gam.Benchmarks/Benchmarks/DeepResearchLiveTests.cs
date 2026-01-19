using FluentAssertions;
using Gam.Benchmarks.Framework;
using Gam.Core;
using Gam.Core.Abstractions;
using Gam.Core.Models;
using Gam.Providers.OpenAI;
using Gam.Storage.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Abstractions;

namespace Gam.Benchmarks.Benchmarks;

/// <summary>
/// Live tests using real OpenAI API for Deep Research validation.
/// 
/// To run these tests, create appsettings.Local.json (gitignored) with:
/// {
///   "OpenAI": {
///     "ApiKey": "sk-your-key-here"
///   }
/// }
/// 
/// Then run:
///   dotnet test tests/Gam.Benchmarks --filter "FullyQualifiedName~DeepResearchLiveTests"
/// 
/// Tests are skipped if no API key is configured.
/// </summary>
public class DeepResearchLiveTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly string? _apiKey;
    private readonly IConfiguration _configuration;
    private PostgreSqlContainer? _postgres;
    private ServiceProvider? _serviceProvider;
    private IGamService? _gam;
    private string _ownerId = null!;
    private BenchmarkDataset? _dataset;

    public DeepResearchLiveTests(ITestOutputHelper output)
    {
        _output = output;
        
        // Load configuration from appsettings files
        _configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true) // Gitignored - put your key here
            .AddEnvironmentVariables()
            .Build();
        
        _apiKey = _configuration["OpenAI:ApiKey"];
    }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            _output.WriteLine("OPENAI_API_KEY not set - tests will be skipped");
            return;
        }

        _output.WriteLine("Starting PostgreSQL container...");
        
        // Start PostgreSQL container with pgvector
        _postgres = new PostgreSqlBuilder()
            .WithImage("pgvector/pgvector:pg17")
            .Build();
        
        await _postgres.StartAsync();
        _output.WriteLine($"PostgreSQL started: {_postgres.GetConnectionString()}");

        // Run migrations
        var connectionString = _postgres.GetConnectionString();
        await RunMigrationsAsync(connectionString);
        _output.WriteLine("Migrations complete");

        // Setup DI with REAL OpenAI providers and Deep Research
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
        
        // Real OpenAI providers
        var model = _configuration["OpenAI:Model"] ?? "gpt-4o-mini";
        var embeddingModel = _configuration["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small";
        var embeddingDimensions = int.TryParse(_configuration["OpenAI:EmbeddingDimensions"], out var dims) ? dims : 1536;
        
        services.AddGamOpenAI(_apiKey!, model, embeddingModel, embeddingDimensions);
        services.AddGamPostgresStorage(connectionString);
        
        // Use Deep Research!
        services.AddGamCoreWithDeepResearch(opts =>
        {
            opts.MaxIterations = 3;
            opts.MaxKeywordQueries = 5;
            opts.MaxVectorQueries = 3;
            opts.MaxHitsPerIteration = 10;
        });

        _serviceProvider = services.BuildServiceProvider();
        _gam = _serviceProvider.GetRequiredService<IGamService>();
        _ownerId = $"live-test-{Guid.NewGuid()}";

        // Load and ingest test dataset
        var datasetPath = Path.Combine(AppContext.BaseDirectory, "Data", "sample-benchmark.json");
        _dataset = await BenchmarkRunner.LoadDatasetAsync(datasetPath);
        
        _output.WriteLine($"Ingesting {_dataset.Conversations.Count} conversations...");
        var runner = new BenchmarkRunner(_gam);
        await runner.IngestDatasetAsync(_dataset, _ownerId);
        
        _output.WriteLine($"Ingestion complete. Ready to test.");
    }

    public async Task DisposeAsync()
    {
        if (_postgres != null)
            await _postgres.DisposeAsync();
        
        _serviceProvider?.Dispose();
    }

    [SkippableFact]
    public async Task DeepResearch_SingleQuery_ShouldFindRelevantContext()
    {
        Skip.If(string.IsNullOrEmpty(_apiKey), "OPENAI_API_KEY not set");
        
        // Arrange
        var query = "What database did we choose for the auth service and why?";
        
        _output.WriteLine($"Query: {query}");
        _output.WriteLine("---");

        // Act
        var context = await _gam!.ResearchAsync(new ResearchRequest
        {
            OwnerId = _ownerId,
            Query = query
        });

        // Assert & Report
        _output.WriteLine($"Pages Retrieved: {context.Pages.Count}");
        _output.WriteLine($"Tokens: {context.TotalTokens}");
        _output.WriteLine($"Iterations: {context.IterationsPerformed}");
        _output.WriteLine($"Duration: {context.Duration.TotalMilliseconds:F0}ms");
        _output.WriteLine("---");
        _output.WriteLine("Context:");
        _output.WriteLine(context.FormatForPrompt());
        
        context.Pages.Should().NotBeEmpty("should retrieve at least one page");
        
        // Check if we found the key facts
        var formattedContext = context.FormatForPrompt();
        var containsPostgresql = formattedContext.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase);
        var containsAcid = formattedContext.Contains("ACID", StringComparison.OrdinalIgnoreCase);
        
        _output.WriteLine($"Contains 'PostgreSQL': {containsPostgresql}");
        _output.WriteLine($"Contains 'ACID': {containsAcid}");
    }

    [SkippableFact]
    public async Task DeepResearch_MultiHopQuery_ShouldFindAcrossConversations()
    {
        Skip.If(string.IsNullOrEmpty(_apiKey), "OPENAI_API_KEY not set");
        
        // This query requires info from multiple conversations
        var query = "Which team members have expertise in specific technologies?";
        
        _output.WriteLine($"Query: {query}");
        _output.WriteLine("---");

        var context = await _gam!.ResearchAsync(new ResearchRequest
        {
            OwnerId = _ownerId,
            Query = query
        });

        _output.WriteLine($"Pages Retrieved: {context.Pages.Count}");
        _output.WriteLine($"Iterations: {context.IterationsPerformed}");
        _output.WriteLine($"Duration: {context.Duration.TotalMilliseconds:F0}ms");
        _output.WriteLine("---");
        _output.WriteLine("Context:");
        _output.WriteLine(context.FormatForPrompt());

        // Check for key people/tech combinations
        var formattedContext = context.FormatForPrompt();
        _output.WriteLine("---");
        _output.WriteLine("Key facts found:");
        _output.WriteLine($"  John + Kafka: {formattedContext.Contains("John") && formattedContext.Contains("Kafka")}");
        _output.WriteLine($"  Sarah + TypeScript: {formattedContext.Contains("Sarah") && formattedContext.Contains("TypeScript")}");
        _output.WriteLine($"  Marcus + GraphQL: {formattedContext.Contains("Marcus") && formattedContext.Contains("GraphQL")}");
    }

    [SkippableFact]
    public async Task DeepResearch_FullBenchmark_WithRealLLM()
    {
        Skip.If(string.IsNullOrEmpty(_apiKey), "OPENAI_API_KEY not set");
        
        var runner = new BenchmarkRunner(_gam!);

        _output.WriteLine("Running full benchmark with real OpenAI...");
        _output.WriteLine("---");

        var results = await runner.RunBenchmarkAsync(_dataset!, _ownerId, "Deep Research (gpt-4o-mini)");

        // Report
        _output.WriteLine("=== DEEP RESEARCH BENCHMARK RESULTS ===");
        _output.WriteLine($"Dataset: {results.DatasetName}");
        _output.WriteLine($"Configuration: {results.ConfigurationName}");
        _output.WriteLine($"Total Queries: {results.QueryResults.Count}");
        _output.WriteLine($"Overall Accuracy: {results.OverallAccuracy:P1}");
        _output.WriteLine($"Average Fact Recall: {results.AverageFactRecall:P1}");
        _output.WriteLine($"Average Query Duration: {results.AverageQueryDuration.TotalMilliseconds:F0}ms");
        _output.WriteLine($"Total Duration: {results.TotalDuration.TotalSeconds:F1}s");
        _output.WriteLine("");

        // Per-query details
        _output.WriteLine("Per-query details:");
        foreach (var qr in results.QueryResults)
        {
            var query = _dataset!.Queries.First(q => q.Id == qr.QueryId);
            _output.WriteLine($"  [{query.Difficulty}] {qr.QueryId}:");
            _output.WriteLine($"    Query: {qr.Query}");
            _output.WriteLine($"    Fact Recall: {qr.FactRecall:P0}");
            _output.WriteLine($"    Pages: {qr.PagesRetrieved}, Iterations: {qr.IterationsPerformed}");
            _output.WriteLine($"    Duration: {qr.Duration.TotalMilliseconds:F0}ms");
            if (qr.MissingFacts?.Count > 0)
            {
                _output.WriteLine($"    Missing: {string.Join(", ", qr.MissingFacts)}");
            }
            _output.WriteLine("");
        }

        // Summary metrics
        _output.WriteLine("=== METRICS FOR COMPARISON ===");
        _output.WriteLine($"DEEP_RESEARCH_ACCURACY={results.OverallAccuracy:F3}");
        _output.WriteLine($"DEEP_RESEARCH_FACT_RECALL={results.AverageFactRecall:F3}");
        _output.WriteLine($"DEEP_RESEARCH_AVG_DURATION_MS={results.AverageQueryDuration.TotalMilliseconds:F0}");
    }

    private static async Task RunMigrationsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        // Create pgvector extension
        await using var cmdExtension = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector;", conn);
        await cmdExtension.ExecuteNonQueryAsync();

        // Create tables with ADR-0002 enhancements
        const string createTables = """
            CREATE TABLE IF NOT EXISTS memory_pages (
                id UUID PRIMARY KEY,
                owner_id VARCHAR(255) NOT NULL,
                content TEXT NOT NULL,
                token_count INTEGER NOT NULL,
                embedding vector(1536),
                metadata JSONB,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                -- ADR-0002: importance and access tracking
                importance FLOAT DEFAULT 0.5,
                access_count INTEGER DEFAULT 0,
                last_accessed_at TIMESTAMPTZ
            );

            CREATE TABLE IF NOT EXISTS memory_abstracts (
                page_id UUID PRIMARY KEY REFERENCES memory_pages(id) ON DELETE CASCADE,
                owner_id VARCHAR(255) NOT NULL,
                summary TEXT NOT NULL,
                headers TEXT[] NOT NULL,
                summary_embedding vector(1536),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                -- ADR-0002: memory type and tags
                memory_type VARCHAR(20) DEFAULT 'conversation',
                tags TEXT[] DEFAULT '{}'
            );

            CREATE INDEX IF NOT EXISTS idx_pages_owner ON memory_pages(owner_id);
            CREATE INDEX IF NOT EXISTS idx_pages_created ON memory_pages(created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_abstracts_owner ON memory_abstracts(owner_id);
            CREATE INDEX IF NOT EXISTS idx_pages_content_fts ON memory_pages USING gin(to_tsvector('english', content));
            CREATE INDEX IF NOT EXISTS idx_abstracts_headers ON memory_abstracts USING gin(headers);
            -- ADR-0002 indexes
            CREATE INDEX IF NOT EXISTS idx_pages_importance ON memory_pages(owner_id, importance DESC);
            CREATE INDEX IF NOT EXISTS idx_abstracts_type ON memory_abstracts(owner_id, memory_type);
            CREATE INDEX IF NOT EXISTS idx_abstracts_tags ON memory_abstracts USING GIN(tags);
            """;

        await using var cmdTables = new NpgsqlCommand(createTables, conn);
        await cmdTables.ExecuteNonQueryAsync();
    }
}
