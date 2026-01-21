using Gam.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Gam.Storage.Postgres.Retrievers;

/// <summary>
/// BM25 keyword-based retrieval for PostgreSQL.
/// 
/// Supports multiple backends (auto-detected in priority order):
/// 
/// 1. pg_textsearch (Timescale) - PostgreSQL licensed, simple syntax
///    https://github.com/timescale/pg_textsearch
///    Syntax: content &lt;@&gt; 'query' (requires BM25 index)
///    
/// 2. ParadeDB pg_search - AGPLv3, Tantivy-based, most mature
///    https://github.com/paradedb/paradedb
///    Syntax: content @@@ 'query'
///    
/// 3. VectorChord-bm25 (TensorChord) - AGPLv3/ELv2, requires tokenizer
///    https://github.com/tensorchord/VectorChord-bm25
///    Syntax: bm25_content &lt;&amp;&gt; to_bm25query('index', query)
///    
/// 4. Native PostgreSQL full-text search (fallback, not true BM25)
///    Uses ts_rank with tsvector/tsquery
/// </summary>
public class PostgresKeywordRetriever : IKeywordRetriever
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresKeywordRetriever> _logger;
    private Bm25Backend? _detectedBackend;
    private string? _bm25IndexName;
    
    public string Name => "keyword_bm25";

    public PostgresKeywordRetriever(NpgsqlDataSource dataSource, ILogger<PostgresKeywordRetriever> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        RetrievalQuery query, CancellationToken ct = default)
    {
        // Detect backend on first use
        _detectedBackend ??= await DetectBm25BackendAsync(ct);
        
        _logger.LogDebug("Keyword retrieval: backend={Backend}, query={Query}, minScore={MinScore}", 
            _detectedBackend, query.Query, query.MinScore);

        var results = await ExecuteWithFallbackAsync(query, ct);
        
        _logger.LogDebug("Keyword retrieval returned {Count} results", results.Count);
        return results;
    }

    /// <summary>
    /// Execute search with automatic fallback to native FTS if specialized backend fails.
    /// </summary>
    private async Task<IReadOnlyList<RetrievalResult>> ExecuteWithFallbackAsync(
        RetrievalQuery query, CancellationToken ct)
    {
        try
        {
            return _detectedBackend switch
            {
                Bm25Backend.PgTextSearch => await SearchWithPgTextSearchAsync(query, ct),
                Bm25Backend.ParadeDb => await SearchWithParadeDbAsync(query, ct),
                Bm25Backend.VectorChordBm25 => await SearchWithVectorChordAsync(query, ct),
                _ => await SearchWithNativeFullTextAsync(query, ct)
            };
        }
        catch (PostgresException ex)
        {
            _logger.LogWarning(ex, 
                "Keyword search with {Backend} failed, falling back to native FTS: {Message}", 
                _detectedBackend, ex.Message);
            
            // Fall back to native FTS
            _detectedBackend = Bm25Backend.NativeFullText;
            return await SearchWithNativeFullTextAsync(query, ct);
        }
    }

    private async Task<Bm25Backend> DetectBm25BackendAsync(CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        
        // Check for pg_textsearch extension AND verify BM25 index exists
        if (await ExtensionExistsAsync(conn, "pg_textsearch", ct))
        {
            var indexName = await GetBm25IndexNameAsync(conn, "memory_pages", ct);
            if (indexName != null)
            {
                _bm25IndexName = indexName;
                _logger.LogInformation("Using pg_textsearch backend with BM25 index: {IndexName}", indexName);
                return Bm25Backend.PgTextSearch;
            }
            _logger.LogWarning("pg_textsearch extension found but no BM25 index on memory_pages.content - falling back");
        }

        // Check for pg_search extension (ParadeDB - most mature)
        if (await ExtensionExistsAsync(conn, "pg_search", ct))
        {
            _logger.LogInformation("Using ParadeDB pg_search backend");
            return Bm25Backend.ParadeDb;
        }

        // Check for vchord_bm25 extension (TensorChord)
        if (await ExtensionExistsAsync(conn, "vchord_bm25", ct))
        {
            _logger.LogInformation("Using VectorChord-bm25 backend");
            return Bm25Backend.VectorChordBm25;
        }

        _logger.LogInformation("Using native PostgreSQL full-text search (no BM25 extensions available)");
        return Bm25Backend.NativeFullText;
    }

    private static async Task<bool> ExtensionExistsAsync(NpgsqlConnection conn, string extName, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT 1 FROM pg_extension WHERE extname = @name", conn);
        cmd.Parameters.AddWithValue("name", extName);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null;
    }

    /// <summary>
    /// Get the name of the BM25 index on memory_pages.content column, if it exists.
    /// pg_textsearch requires an explicit BM25 index to function.
    /// </summary>
    private static async Task<string?> GetBm25IndexNameAsync(NpgsqlConnection conn, string tableName, CancellationToken ct)
    {
        // Find the BM25 index name on the table
        await using var cmd = new NpgsqlCommand("""
            SELECT indexname FROM pg_indexes 
            WHERE tablename = @table 
              AND indexdef LIKE '%USING bm25%'
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("table", tableName);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }

    /// <summary>
    /// Search using pg_textsearch (Timescale) - PostgreSQL licensed
    /// https://github.com/timescale/pg_textsearch
    /// Operator: &lt;@&gt; with to_bm25query() returns negative BM25 scores (more negative = better match)
    /// Requires BM25 index: CREATE INDEX idx_pages_bm25 ON table USING bm25(column) WITH (text_config='english')
    /// </summary>
    private async Task<IReadOnlyList<RetrievalResult>> SearchWithPgTextSearchAsync(
        RetrievalQuery query, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        
        var excludeClause = query.ExcludePageIds?.Count > 0 
            ? "AND id != ALL(@exclude_ids)" 
            : "";

        // pg_textsearch: <@> with to_bm25query() returns negative BM25 distance scores
        // More negative = better match, so we negate to get positive scores (higher = better)
        // Must use to_bm25query(query, index_name) syntax
        var indexName = _bm25IndexName ?? "idx_pages_bm25";
        await using var cmd = new NpgsqlCommand($"""
            SELECT id, -(content <@> to_bm25query(@query, '{indexName}')) as score
            FROM memory_pages
            WHERE owner_id = @owner_id
              {excludeClause}
            ORDER BY content <@> to_bm25query(@query, '{indexName}')
            LIMIT @limit
            """, conn);

        cmd.Parameters.AddWithValue("owner_id", query.OwnerId);
        cmd.Parameters.AddWithValue("query", query.Query);
        cmd.Parameters.AddWithValue("limit", query.MaxResults);
        
        if (query.ExcludePageIds?.Count > 0)
            cmd.Parameters.AddWithValue("exclude_ids", query.ExcludePageIds.ToArray());

        return await ExecuteAndMapResultsAsync(cmd, "pg_textsearch", query.MinScore, ct);
    }

    /// <summary>
    /// Search using ParadeDB pg_search - AGPLv3, Tantivy-based
    /// https://github.com/paradedb/paradedb  
    /// Operator: @@@ for BM25 search, paradedb.score() for scoring
    /// </summary>
    private async Task<IReadOnlyList<RetrievalResult>> SearchWithParadeDbAsync(
        RetrievalQuery query, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        
        var excludeClause = query.ExcludePageIds?.Count > 0 
            ? "AND id != ALL(@exclude_ids)" 
            : "";

        // ParadeDB uses @@@ operator and paradedb.score() function
        await using var cmd = new NpgsqlCommand($"""
            SELECT id, paradedb.score(id) as score
            FROM memory_pages
            WHERE owner_id = @owner_id
              AND content @@@ @query
              {excludeClause}
            ORDER BY score DESC
            LIMIT @limit
            """, conn);

        cmd.Parameters.AddWithValue("owner_id", query.OwnerId);
        cmd.Parameters.AddWithValue("query", query.Query);
        cmd.Parameters.AddWithValue("limit", query.MaxResults);
        
        if (query.ExcludePageIds?.Count > 0)
            cmd.Parameters.AddWithValue("exclude_ids", query.ExcludePageIds.ToArray());

        return await ExecuteAndMapResultsAsync(cmd, "paradedb", query.MinScore, ct);
    }

    /// <summary>
    /// Search using VectorChord-bm25 (TensorChord) - AGPLv3/ELv2 licensed
    /// https://github.com/tensorchord/VectorChord-bm25
    /// Requires bm25vector column and separate tokenizer setup
    /// Operator: &lt;&amp;&gt; returns negative scores
    /// </summary>
    private async Task<IReadOnlyList<RetrievalResult>> SearchWithVectorChordAsync(
        RetrievalQuery query, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        
        var excludeClause = query.ExcludePageIds?.Count > 0 
            ? "AND id != ALL(@exclude_ids)" 
            : "";

        // VectorChord uses <&> operator with to_bm25query, returns negative scores
        // Assumes bm25_content column exists with bm25vector type
        await using var cmd = new NpgsqlCommand($"""
            SELECT id, -(bm25_content <&> to_bm25query('pages_bm25_idx', @query::bm25vector)) as score
            FROM memory_pages
            WHERE owner_id = @owner_id
              {excludeClause}
            ORDER BY bm25_content <&> to_bm25query('pages_bm25_idx', @query::bm25vector)
            LIMIT @limit
            """, conn);

        cmd.Parameters.AddWithValue("owner_id", query.OwnerId);
        cmd.Parameters.AddWithValue("query", query.Query);
        cmd.Parameters.AddWithValue("limit", query.MaxResults);
        
        if (query.ExcludePageIds?.Count > 0)
            cmd.Parameters.AddWithValue("exclude_ids", query.ExcludePageIds.ToArray());

        return await ExecuteAndMapResultsAsync(cmd, "vectorchord", query.MinScore, ct);
    }

    /// <summary>
    /// Search using native PostgreSQL full-text search (fallback)
    /// Works on any PostgreSQL installation without extensions
    /// Note: ts_rank is NOT true BM25 - uses tf-idf variant without proper saturation
    /// </summary>
    private async Task<IReadOnlyList<RetrievalResult>> SearchWithNativeFullTextAsync(
        RetrievalQuery query, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        
        var excludeClause = query.ExcludePageIds?.Count > 0 
            ? "AND id != ALL(@exclude_ids)" 
            : "";

        // Native PostgreSQL full-text search with ts_rank
        await using var cmd = new NpgsqlCommand($"""
            SELECT id, ts_rank_cd(to_tsvector('english', content), plainto_tsquery('english', @query)) as score
            FROM memory_pages
            WHERE owner_id = @owner_id
              AND to_tsvector('english', content) @@ plainto_tsquery('english', @query)
              {excludeClause}
            ORDER BY score DESC
            LIMIT @limit
            """, conn);

        cmd.Parameters.AddWithValue("owner_id", query.OwnerId);
        cmd.Parameters.AddWithValue("query", query.Query);
        cmd.Parameters.AddWithValue("limit", query.MaxResults);
        
        if (query.ExcludePageIds?.Count > 0)
            cmd.Parameters.AddWithValue("exclude_ids", query.ExcludePageIds.ToArray());

        return await ExecuteAndMapResultsAsync(cmd, "native_fts", query.MinScore, ct);
    }

    private async Task<IReadOnlyList<RetrievalResult>> ExecuteAndMapResultsAsync(
        NpgsqlCommand cmd, string retrieverSuffix, float minScore, CancellationToken ct)
    {
        var results = new List<RetrievalResult>();
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        
        while (await reader.ReadAsync(ct))
        {
            var score = reader.GetFloat(1);
            if (score < minScore) continue;
            
            results.Add(new RetrievalResult
            {
                PageId = reader.GetGuid(0),
                Score = score,
                RetrieverName = $"{Name}_{retrieverSuffix}"
            });
        }

        return results;
    }

    private enum Bm25Backend
    {
        NativeFullText,
        PgTextSearch,    // Timescale - PostgreSQL license
        ParadeDb,        // ParadeDB - AGPLv3
        VectorChordBm25  // TensorChord - AGPLv3/ELv2
    }
}
