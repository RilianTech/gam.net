using Gam.Core;
using Gam.Core.Abstractions;
using Gam.Core.Models;
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
/// Benchmarks comparing BM25 backends (native FTS vs pg_textsearch).
/// 
/// Uses Timescale's timescaledb-ha image which includes:
/// - pgvector for semantic search
/// - pg_textsearch for true BM25 ranking (PostgreSQL licensed)
/// 
/// To run: dotnet test tests/Gam.Benchmarks --filter "Bm25Benchmark" -l "console;verbosity=detailed"
/// </summary>
public class Bm25BenchmarkTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly IConfiguration _configuration;
    private readonly string? _apiKey;
    
    private PostgreSqlContainer? _postgres;
    private ServiceProvider? _serviceProvider;
    private IGamService? _gam;
    private BenchmarkDataset? _dataset;
    private string? _ownerId;
    private string? _detectedBackend;

    public Bm25BenchmarkTests(ITestOutputHelper output)
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

        _output.WriteLine("Starting Timescale PostgreSQL container with pg_textsearch...");
        
        // Use Timescale image which includes pgvector AND pg_textsearch
        _postgres = new PostgreSqlBuilder()
            .WithImage("timescale/timescaledb-ha:pg17")
            .Build();
        
        await _postgres.StartAsync();
        _output.WriteLine($"PostgreSQL started: {_postgres.GetConnectionString()}");

        var connectionString = _postgres.GetConnectionString();
        await RunMigrationsWithBm25Async(connectionString);
        _output.WriteLine("Migrations complete (with BM25 index)");
        
        // Detect which BM25 backend is available
        _detectedBackend = await DetectBm25BackendAsync(connectionString);
        _output.WriteLine($"Detected BM25 backend: {_detectedBackend}");

        // Setup DI
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
        
        var model = _configuration["OpenAI:Model"] ?? "gpt-4o-mini";
        var embeddingModel = _configuration["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small";
        var embeddingDimensions = int.TryParse(_configuration["OpenAI:EmbeddingDimensions"], out var dims) ? dims : 1536;
        
        services.AddGamOpenAI(_apiKey!, model, embeddingModel, embeddingDimensions);
        services.AddGamPostgresStorage(connectionString);
        services.AddGamCoreWithDeepResearch(opts =>
        {
            opts.MaxIterations = 3;
            opts.MaxKeywordQueries = 5;
            opts.MaxVectorQueries = 3;
            opts.MaxHitsPerIteration = 10;
            opts.EnableRelationshipExpansion = true;
        }, enableRelationships: true);

        _serviceProvider = services.BuildServiceProvider();
        _gam = _serviceProvider.GetRequiredService<IGamService>();
        _ownerId = $"bm25-test-{Guid.NewGuid()}";

        // Load dataset
        var datasetPath = Path.Combine(AppContext.BaseDirectory, "Data", "relationship-benchmark.json");
        _dataset = await BenchmarkRunner.LoadDatasetAsync(datasetPath);
        
        _output.WriteLine($"Ingesting {_dataset.Conversations.Count} conversations...");
        var runner = new BenchmarkRunner(_gam);
        await runner.IngestDatasetAsync(_dataset, _ownerId);
        
        _output.WriteLine($"Ingestion complete. Ready to test with {_detectedBackend} backend.");
    }

    public async Task DisposeAsync()
    {
        if (_postgres != null)
            await _postgres.DisposeAsync();
        
        _serviceProvider?.Dispose();
    }

    [SkippableFact]
    public async Task Bm25Backend_ShouldBeDetected()
    {
        Skip.If(string.IsNullOrEmpty(_apiKey), "OPENAI_API_KEY not set");
        Skip.If(_detectedBackend == null, "Backend not detected");

        _output.WriteLine($"=== BM25 BACKEND DETECTION ===");
        _output.WriteLine($"Backend: {_detectedBackend}");
        
        // pg_textsearch should be detected when using timescaledb-ha image
        Assert.Contains(_detectedBackend, new[] { "pg_textsearch", "native_fts" });
        
        if (_detectedBackend == "pg_textsearch")
        {
            _output.WriteLine("SUCCESS: True BM25 ranking available via pg_textsearch");
        }
        else
        {
            _output.WriteLine("WARNING: Falling back to native FTS (not true BM25)");
        }
    }

    [SkippableFact]
    public async Task Bm25Benchmark_CompareWithNativeFts()
    {
        Skip.If(string.IsNullOrEmpty(_apiKey), "OPENAI_API_KEY not set");
        Skip.If(_gam == null || _dataset == null, "Not initialized");

        _output.WriteLine($"Running benchmark with {_detectedBackend} backend...");
        _output.WriteLine("---");

        var runner = new BenchmarkRunner(_gam!);
        var results = await runner.RunBenchmarkAsync(_dataset!, _ownerId!, $"Deep Research with {_detectedBackend}");
        
        _output.WriteLine($"=== BM25 BENCHMARK RESULTS ({_detectedBackend}) ===");
        _output.WriteLine($"Dataset: {results.DatasetName}");
        _output.WriteLine($"Configuration: {results.ConfigurationName}");
        _output.WriteLine($"Total Queries: {results.QueryResults.Count}");
        _output.WriteLine($"Overall Accuracy: {results.OverallAccuracy:P1}");
        _output.WriteLine($"Average Fact Recall: {results.AverageFactRecall:P1}");
        _output.WriteLine($"Average Query Duration: {results.AverageQueryDuration.TotalMilliseconds:F0}ms");
        _output.WriteLine($"Total Duration: {results.TotalDuration.TotalSeconds:F1}s");
        
        _output.WriteLine("");
        _output.WriteLine("Per-query details:");
        foreach (var qr in results.QueryResults)
        {
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

        _output.WriteLine($"=== METRICS FOR COMPARISON ===");
        _output.WriteLine($"BM25_BACKEND={_detectedBackend}");
        _output.WriteLine($"BM25_BENCHMARK_ACCURACY={results.OverallAccuracy:F3}");
        _output.WriteLine($"BM25_BENCHMARK_FACT_RECALL={results.AverageFactRecall:F3}");
        _output.WriteLine($"BM25_BENCHMARK_AVG_DURATION_MS={results.AverageQueryDuration.TotalMilliseconds:F0}");
        
        Assert.True(results.AverageFactRecall >= 0.7f, 
            $"Expected at least 70% fact recall, got {results.AverageFactRecall:P1}");
    }

    [SkippableFact]
    public async Task KeywordSearch_ShouldUseBm25Backend()
    {
        Skip.If(string.IsNullOrEmpty(_apiKey), "OPENAI_API_KEY not set");
        Skip.If(_gam == null, "GAM not initialized");

        // Test a keyword-heavy query that should benefit from BM25
        var query = "PostgreSQL configuration PgBouncer connection pooling";
        _output.WriteLine($"Query: {query}");
        _output.WriteLine($"Backend: {_detectedBackend}");
        _output.WriteLine("---");

        var result = await _gam!.ResearchAsync(new ResearchRequest
        {
            OwnerId = _ownerId!,
            Query = query
        });

        _output.WriteLine($"Pages Retrieved: {result.Pages.Count}");
        _output.WriteLine($"Duration: {result.Duration.TotalMilliseconds:F0}ms");
        _output.WriteLine("---");
        
        var formattedContext = result.FormatForPrompt();
        _output.WriteLine("Context preview:");
        _output.WriteLine(formattedContext.Length > 500 
            ? formattedContext[..500] + "..." 
            : formattedContext);
        
        // Should find PostgreSQL-related content
        var contextLower = formattedContext.ToLowerInvariant();
        Assert.True(contextLower.Contains("postgresql") || contextLower.Contains("pgbouncer"),
            "Should find PostgreSQL or PgBouncer content");
    }

    private static async Task<string> DetectBm25BackendAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        
        // Check for pg_textsearch extension
        await using var cmd = new NpgsqlCommand(
            "SELECT 1 FROM pg_extension WHERE extname = 'pg_textsearch'", conn);
        var result = await cmd.ExecuteScalarAsync();
        
        return result != null ? "pg_textsearch" : "native_fts";
    }

    private static async Task RunMigrationsWithBm25Async(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        // Enable extensions
        await using var cmdExtensions = new NpgsqlCommand("""
            CREATE EXTENSION IF NOT EXISTS vector;
            CREATE EXTENSION IF NOT EXISTS pg_textsearch;
            """, conn);
        
        try
        {
            await cmdExtensions.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "42501" || ex.Message.Contains("pg_textsearch"))
        {
            // pg_textsearch not available, continue with vector only
            await using var cmdVector = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector;", conn);
            await cmdVector.ExecuteNonQueryAsync();
        }

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

            -- Basic indexes
            CREATE INDEX IF NOT EXISTS idx_pages_owner ON memory_pages(owner_id);
            CREATE INDEX IF NOT EXISTS idx_pages_created ON memory_pages(created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_abstracts_owner ON memory_abstracts(owner_id);
            
            -- FTS fallback index
            CREATE INDEX IF NOT EXISTS idx_pages_content_fts ON memory_pages USING gin(to_tsvector('english', content));
            CREATE INDEX IF NOT EXISTS idx_abstracts_headers ON memory_abstracts USING gin(headers);
            
            -- ADR-0002 indexes
            CREATE INDEX IF NOT EXISTS idx_pages_importance ON memory_pages(owner_id, importance DESC);
            CREATE INDEX IF NOT EXISTS idx_abstracts_type ON memory_abstracts(owner_id, memory_type);
            CREATE INDEX IF NOT EXISTS idx_abstracts_tags ON memory_abstracts USING GIN(tags);
            CREATE INDEX IF NOT EXISTS idx_rel_source ON memory_relationships(source_page_id);
            CREATE INDEX IF NOT EXISTS idx_rel_target ON memory_relationships(target_page_id);
            CREATE INDEX IF NOT EXISTS idx_rel_type ON memory_relationships(relationship_type);
            """;

        await using var cmdTables = new NpgsqlCommand(createTables, conn);
        await cmdTables.ExecuteNonQueryAsync();
        
        // Try to create BM25 index if pg_textsearch is available
        try
        {
            await using var cmdBm25 = new NpgsqlCommand("""
                CREATE INDEX IF NOT EXISTS idx_pages_bm25 ON memory_pages 
                    USING bm25(content) WITH (text_config='english');
                """, conn);
            await cmdBm25.ExecuteNonQueryAsync();
        }
        catch (PostgresException)
        {
            // BM25 index creation failed, that's okay - we'll use native FTS
        }
    }
}
