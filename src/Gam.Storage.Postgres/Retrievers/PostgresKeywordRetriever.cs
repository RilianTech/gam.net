using Gam.Core.Abstractions;
using Npgsql;

namespace Gam.Storage.Postgres.Retrievers;

/// <summary>
/// BM25 keyword-based retrieval for PostgreSQL.
/// 
/// Supports multiple backends (auto-detected in priority order):
/// 
/// 1. pg_textsearch (Timescale) - PostgreSQL licensed, simple syntax
///    https://github.com/timescale/pg_textsearch
///    Syntax: content &lt;@&gt; 'query'
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
    private Bm25Backend? _detectedBackend;
    
    public string Name => "keyword_bm25";

    public PostgresKeywordRetriever(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        RetrievalQuery query, CancellationToken ct = default)
    {
        // Detect backend on first use
        _detectedBackend ??= await DetectBm25BackendAsync(ct);

        return _detectedBackend switch
        {
            Bm25Backend.PgTextSearch => await SearchWithPgTextSearchAsync(query, ct),
            Bm25Backend.ParadeDb => await SearchWithParadeDbAsync(query, ct),
            Bm25Backend.VectorChordBm25 => await SearchWithVectorChordAsync(query, ct),
            _ => await SearchWithNativeFullTextAsync(query, ct)
        };
    }

    private async Task<Bm25Backend> DetectBm25BackendAsync(CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        
        // Check for pg_textsearch extension (Timescale - most permissive license)
        if (await ExtensionExistsAsync(conn, "pg_textsearch", ct))
            return Bm25Backend.PgTextSearch;

        // Check for pg_search extension (ParadeDB - most mature)
        if (await ExtensionExistsAsync(conn, "pg_search", ct))
            return Bm25Backend.ParadeDb;

        // Check for vchord_bm25 extension (TensorChord)
        if (await ExtensionExistsAsync(conn, "vchord_bm25", ct))
            return Bm25Backend.VectorChordBm25;

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
    /// Search using pg_textsearch (Timescale) - PostgreSQL licensed
    /// https://github.com/timescale/pg_textsearch
    /// Operator: &lt;@&gt; returns negative scores (lower = better match)
    /// </summary>
    private async Task<IReadOnlyList<RetrievalResult>> SearchWithPgTextSearchAsync(
        RetrievalQuery query, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        
        var excludeClause = query.ExcludePageIds?.Count > 0 
            ? "AND id != ALL(@exclude_ids)" 
            : "";

        // pg_textsearch: <@> returns negative scores, negate for positive
        await using var cmd = new NpgsqlCommand($"""
            SELECT id, -(content <@> @query) as score
            FROM memory_pages
            WHERE owner_id = @owner_id
              {excludeClause}
            ORDER BY content <@> @query
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

        try
        {
            return await ExecuteAndMapResultsAsync(cmd, "vectorchord", query.MinScore, ct);
        }
        catch (PostgresException)
        {
            // Fall back to native if VectorChord query fails
            return await SearchWithNativeFullTextAsync(query, ct);
        }
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
        
        try
        {
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
        }
        catch (PostgresException)
        {
            // Query failed, return empty results
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
