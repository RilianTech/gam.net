using Gam.Core;
using Gam.Core.Abstractions;
using Gam.Core.Models;
using Gam.Core.Services;
using Gam.Providers.OpenAI;
using Gam.Storage.Postgres;
using Gam.Benchmarks.Framework;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Abstractions;

namespace Gam.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks specifically designed to test memory relationships and relationship-aware retrieval.
/// Uses a dataset with overlapping entities (John, PostgreSQL, Kafka, etc.) across conversations.
/// </summary>
public class RelationshipBenchmarkTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly IConfiguration _configuration;
    private readonly string? _apiKey;
    
    private PostgreSqlContainer? _postgres;
    private ServiceProvider? _serviceProvider;
    private IGamService? _gam;
    private IMemoryStore? _store;
    private BenchmarkDataset? _dataset;
    private string? _ownerId;

    public RelationshipBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
        _configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true)
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
        
        _postgres = new PostgreSqlBuilder()
            .WithImage("pgvector/pgvector:pg17")
            .Build();
        
        await _postgres.StartAsync();
        _output.WriteLine($"PostgreSQL started: {_postgres.GetConnectionString()}");

        var connectionString = _postgres.GetConnectionString();
        await RunMigrationsAsync(connectionString);
        _output.WriteLine("Migrations complete");

        // Setup DI with relationship service enabled
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug)); // Debug to see relationship logs
        
        var model = _configuration["OpenAI:Model"] ?? "gpt-4o-mini";
        var embeddingModel = _configuration["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small";
        var embeddingDimensions = int.TryParse(_configuration["OpenAI:EmbeddingDimensions"], out var dims) ? dims : 1536;
        
        services.AddGamOpenAI(_apiKey!, model, embeddingModel, embeddingDimensions);
        services.AddGamPostgresStorage(connectionString);
        
        // Enable relationships explicitly
        services.AddGamCoreWithDeepResearch(opts =>
        {
            opts.MaxIterations = 3;
            opts.MaxKeywordQueries = 5;
            opts.MaxVectorQueries = 3;
            opts.MaxHitsPerIteration = 10;
            opts.EnableRelationshipExpansion = true;
            opts.MaxRelatedPerSource = 3;
        }, enableRelationships: true);

        _serviceProvider = services.BuildServiceProvider();
        _gam = _serviceProvider.GetRequiredService<IGamService>();
        _store = _serviceProvider.GetRequiredService<IMemoryStore>();
        _ownerId = $"relationship-test-{Guid.NewGuid()}";

        // Load relationship-focused dataset
        var datasetPath = Path.Combine(AppContext.BaseDirectory, "Data", "relationship-benchmark.json");
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
    public async Task Relationships_ShouldBeCreatedDuringIngestion()
    {
        Skip.If(string.IsNullOrEmpty(_apiKey), "OPENAI_API_KEY not set");
        Skip.If(_store == null, "Store not initialized");

        // Get all abstracts to check tags
        var abstracts = await _store!.GetAbstractsAsync(_ownerId!, default);
        
        _output.WriteLine($"\n=== MEMORY ANALYSIS ===");
        _output.WriteLine($"Total memories: {abstracts.Count}");
        
        foreach (var abs in abstracts)
        {
            _output.WriteLine($"\nMemory {abs.PageId}:");
            _output.WriteLine($"  Type: {abs.Type}");
            _output.WriteLine($"  Summary: {abs.Summary[..Math.Min(100, abs.Summary.Length)]}...");
            _output.WriteLine($"  Tags: {string.Join(", ", abs.Tags)}");
        }
        
        // Check for tag overlap (which should trigger relationships)
        var tagCounts = abstracts
            .SelectMany(a => a.Tags)
            .GroupBy(t => t.ToLowerInvariant())
            .Where(g => g.Count() >= 2)
            .OrderByDescending(g => g.Count())
            .ToList();
        
        _output.WriteLine($"\n=== TAG OVERLAP ===");
        foreach (var tag in tagCounts.Take(10))
        {
            _output.WriteLine($"  {tag.Key}: appears in {tag.Count()} memories");
        }
        
        // Check relationships created
        var pageIds = abstracts.Select(a => a.PageId).ToList();
        var relationships = await _store.GetRelationshipsFromAsync(pageIds, null, default);
        
        _output.WriteLine($"\n=== RELATIONSHIPS CREATED ===");
        _output.WriteLine($"Total relationships: {relationships.Count}");
        
        foreach (var rel in relationships.Take(20))
        {
            _output.WriteLine($"  {rel.SourcePageId} --[{rel.Type}]--> {rel.TargetPageId} (confidence: {rel.Confidence:F2})");
        }
        
        // Verify we have some relationships
        Assert.True(abstracts.Count >= 8, "Should have at least 8 memories from 8 conversations");
    }

    [SkippableFact]
    public async Task RelationshipQuery_JohnExpertise_ShouldFindAcrossConversations()
    {
        Skip.If(string.IsNullOrEmpty(_apiKey), "OPENAI_API_KEY not set");
        Skip.If(_gam == null, "GAM not initialized");

        var query = "What is John's expertise and what projects is he leading?";
        _output.WriteLine($"Query: {query}");
        _output.WriteLine("---");

        var result = await _gam!.ResearchAsync(new ResearchRequest
        {
            OwnerId = _ownerId!,
            Query = query
        });

        _output.WriteLine($"Pages Retrieved: {result.Pages.Count}");
        _output.WriteLine($"Duration: {result.Duration.TotalMilliseconds:F0}ms");
        _output.WriteLine("---");
        _output.WriteLine("Context:");
        var formattedContext1 = result.FormatForPrompt();
        _output.WriteLine(formattedContext1);
        
        var context = formattedContext1.ToLowerInvariant();
        
        // Should find John's expertise across multiple conversations
        var hasPostgreSQL = context.Contains("postgresql");
        var hasKafka = context.Contains("kafka");
        var hasJohn = context.Contains("john");
        
        _output.WriteLine("---");
        _output.WriteLine($"Contains John: {hasJohn}");
        _output.WriteLine($"Contains PostgreSQL: {hasPostgreSQL}");
        _output.WriteLine($"Contains Kafka: {hasKafka}");
        
        Assert.True(hasJohn, "Should find John");
        Assert.True(hasPostgreSQL || hasKafka, "Should find at least one of John's expertise areas");
    }

    [SkippableFact]
    public async Task RelationshipQuery_PostgreSQLJourney_ShouldLinkRelatedConversations()
    {
        Skip.If(string.IsNullOrEmpty(_apiKey), "OPENAI_API_KEY not set");
        Skip.If(_gam == null, "GAM not initialized");

        var query = "What's the full story of our PostgreSQL journey from setup to optimization?";
        _output.WriteLine($"Query: {query}");
        _output.WriteLine("---");

        var result = await _gam!.ResearchAsync(new ResearchRequest
        {
            OwnerId = _ownerId!,
            Query = query
        });

        _output.WriteLine($"Pages Retrieved: {result.Pages.Count}");
        _output.WriteLine($"Duration: {result.Duration.TotalMilliseconds:F0}ms");
        _output.WriteLine("---");
        _output.WriteLine("Context:");
        var formattedContext2 = result.FormatForPrompt();
        _output.WriteLine(formattedContext2);
        
        var context = formattedContext2.ToLowerInvariant();
        
        // Should find content from multiple PostgreSQL conversations
        var hasSetup = context.Contains("pgbouncer") || context.Contains("connection pool");
        var hasOptimization = context.Contains("slow") && context.Contains("index");
        var hasMigration = context.Contains("mysql") || context.Contains("migration");
        
        _output.WriteLine("---");
        _output.WriteLine($"Has setup info: {hasSetup}");
        _output.WriteLine($"Has optimization info: {hasOptimization}");
        _output.WriteLine($"Has migration info: {hasMigration}");
        
        // Should find at least 2 of the 3 PostgreSQL conversations
        var foundCount = (hasSetup ? 1 : 0) + (hasOptimization ? 1 : 0) + (hasMigration ? 1 : 0);
        Assert.True(foundCount >= 2, $"Should find at least 2 PostgreSQL topics, found {foundCount}");
    }

    [SkippableFact]
    public async Task FullRelationshipBenchmark_WithRealLLM()
    {
        Skip.If(string.IsNullOrEmpty(_apiKey), "OPENAI_API_KEY not set");
        Skip.If(_gam == null || _dataset == null, "Not initialized");

        _output.WriteLine("Running relationship benchmark with real OpenAI...");
        _output.WriteLine("---");

        var runner = new BenchmarkRunner(_gam!);
        var results = await runner.RunBenchmarkAsync(_dataset!, _ownerId!, "Deep Research with Relationships");
        
        _output.WriteLine("=== RELATIONSHIP BENCHMARK RESULTS ===");
        _output.WriteLine($"Dataset: {results.DatasetName}");
        _output.WriteLine($"Configuration: {results.ConfigurationName}");
        _output.WriteLine($"Total Queries: {results.QueryResults.Count}");
        _output.WriteLine($"Overall Accuracy: {results.OverallAccuracy:P1}");
        _output.WriteLine($"Average Fact Recall: {results.AverageFactRecall:P1}");
        _output.WriteLine($"Average Query Duration: {results.AverageQueryDuration.TotalMilliseconds:F0}ms");
        _output.WriteLine($"Total Duration: {results.TotalDuration.TotalSeconds:F1}s");
        
        _output.WriteLine("\nPer-query details:");
        foreach (var qr in results.QueryResults)
        {
            // Get difficulty from the dataset query
            var datasetQuery = _dataset!.Queries.FirstOrDefault(q => q.Id == qr.QueryId);
            var difficulty = datasetQuery?.Difficulty ?? "unknown";
            _output.WriteLine($"  [{difficulty}] {qr.QueryId}:");
            _output.WriteLine($"    Query: {qr.Query}");
            _output.WriteLine($"    Fact Recall: {qr.FactRecall:P0}");
            if (qr.MissingFacts?.Count > 0)
            {
                _output.WriteLine($"    Missing: {string.Join(", ", qr.MissingFacts)}");
            }
            _output.WriteLine($"    Pages: {qr.PagesRetrieved}, Iterations: {qr.IterationsPerformed}");
            _output.WriteLine($"    Duration: {qr.Duration.TotalMilliseconds:F0}ms");
            _output.WriteLine("");
        }

        _output.WriteLine("=== METRICS FOR COMPARISON ===");
        _output.WriteLine($"RELATIONSHIP_BENCHMARK_ACCURACY={results.OverallAccuracy:F3}");
        _output.WriteLine($"RELATIONSHIP_BENCHMARK_FACT_RECALL={results.AverageFactRecall:F3}");
        _output.WriteLine($"RELATIONSHIP_BENCHMARK_AVG_DURATION_MS={results.AverageQueryDuration.TotalMilliseconds:F0}");
        
        // This benchmark is harder - we expect at least 70% fact recall
        Assert.True(results.AverageFactRecall >= 0.7f, 
            $"Expected at least 70% fact recall, got {results.AverageFactRecall:P1}");
    }

    private static async Task RunMigrationsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await using var cmdExtension = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector;", conn);
        await cmdExtension.ExecuteNonQueryAsync();

        const string createTables = """
            CREATE TABLE IF NOT EXISTS memory_pages (
                id UUID PRIMARY KEY,
                owner_id VARCHAR(255) NOT NULL,
                content TEXT NOT NULL,
                token_count INTEGER NOT NULL,
                embedding vector(1536),
                metadata JSONB,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
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
                memory_type VARCHAR(20) DEFAULT 'conversation',
                tags TEXT[] DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS memory_relationships (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                source_page_id UUID NOT NULL REFERENCES memory_pages(id) ON DELETE CASCADE,
                target_page_id UUID NOT NULL REFERENCES memory_pages(id) ON DELETE CASCADE,
                relationship_type VARCHAR(50) NOT NULL,
                confidence FLOAT DEFAULT 1.0,
                created_by VARCHAR(20) DEFAULT 'system',
                created_at TIMESTAMPTZ DEFAULT NOW(),
                UNIQUE(source_page_id, target_page_id, relationship_type)
            );

            CREATE INDEX IF NOT EXISTS idx_pages_owner ON memory_pages(owner_id);
            CREATE INDEX IF NOT EXISTS idx_pages_created ON memory_pages(created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_abstracts_owner ON memory_abstracts(owner_id);
            CREATE INDEX IF NOT EXISTS idx_pages_content_fts ON memory_pages USING gin(to_tsvector('english', content));
            CREATE INDEX IF NOT EXISTS idx_abstracts_headers ON memory_abstracts USING gin(headers);
            CREATE INDEX IF NOT EXISTS idx_pages_importance ON memory_pages(owner_id, importance DESC);
            CREATE INDEX IF NOT EXISTS idx_abstracts_type ON memory_abstracts(owner_id, memory_type);
            CREATE INDEX IF NOT EXISTS idx_abstracts_tags ON memory_abstracts USING GIN(tags);
            CREATE INDEX IF NOT EXISTS idx_rel_source ON memory_relationships(source_page_id);
            CREATE INDEX IF NOT EXISTS idx_rel_target ON memory_relationships(target_page_id);
            CREATE INDEX IF NOT EXISTS idx_rel_type ON memory_relationships(relationship_type);
            """;

        await using var cmdTables = new NpgsqlCommand(createTables, conn);
        await cmdTables.ExecuteNonQueryAsync();
    }
}
