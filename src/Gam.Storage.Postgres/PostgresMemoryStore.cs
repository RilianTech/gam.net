using System.Text.Json;
using Gam.Core.Abstractions;
using Gam.Core.Models;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pgvector;

namespace Gam.Storage.Postgres;

/// <summary>
/// PostgreSQL implementation of IMemoryStore using pgvector for embeddings.
/// </summary>
public class PostgresMemoryStore : IMemoryStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresMemoryStore> _logger;

    public PostgresMemoryStore(NpgsqlDataSource dataSource, ILogger<PostgresMemoryStore> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task<MemoryPage?> GetPageAsync(Guid pageId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT id, owner_id, content, token_count, embedding, metadata, created_at
            FROM memory_pages WHERE id = @id
            """, conn);
        
        cmd.Parameters.AddWithValue("id", pageId);
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        
        return MapPage(reader);
    }

    public async Task<IReadOnlyList<MemoryPage>> GetPagesAsync(
        IEnumerable<Guid> pageIds, CancellationToken ct = default)
    {
        var ids = pageIds.ToList();
        if (ids.Count == 0) return Array.Empty<MemoryPage>();

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT id, owner_id, content, token_count, embedding, metadata, created_at
            FROM memory_pages WHERE id = ANY(@ids)
            """, conn);
        
        cmd.Parameters.AddWithValue("ids", ids.ToArray());
        
        var pages = new List<MemoryPage>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            pages.Add(MapPage(reader));
        }
        return pages;
    }

    public async Task StorePageAsync(MemoryPage page, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO memory_pages (id, owner_id, content, token_count, embedding, metadata, created_at)
            VALUES (@id, @owner_id, @content, @token_count, @embedding, @metadata, @created_at)
            ON CONFLICT (id) DO UPDATE SET
                content = EXCLUDED.content,
                token_count = EXCLUDED.token_count,
                embedding = EXCLUDED.embedding,
                metadata = EXCLUDED.metadata
            """, conn);

        cmd.Parameters.AddWithValue("id", page.Id);
        cmd.Parameters.AddWithValue("owner_id", page.OwnerId);
        cmd.Parameters.AddWithValue("content", page.Content);
        cmd.Parameters.AddWithValue("token_count", page.TokenCount);
        cmd.Parameters.AddWithValue("embedding", page.Embedding != null 
            ? new Vector(page.Embedding) : DBNull.Value);
        cmd.Parameters.AddWithValue("metadata", page.Metadata != null 
            ? JsonSerializer.Serialize(page.Metadata) : DBNull.Value);
        cmd.Parameters.AddWithValue("created_at", page.CreatedAt);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task StoreAbstractAsync(MemoryAbstract abs, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO memory_abstracts (page_id, owner_id, summary, headers, summary_embedding, created_at)
            VALUES (@page_id, @owner_id, @summary, @headers, @embedding, @created_at)
            ON CONFLICT (page_id) DO UPDATE SET
                summary = EXCLUDED.summary,
                headers = EXCLUDED.headers,
                summary_embedding = EXCLUDED.summary_embedding
            """, conn);

        cmd.Parameters.AddWithValue("page_id", abs.PageId);
        cmd.Parameters.AddWithValue("owner_id", abs.OwnerId);
        cmd.Parameters.AddWithValue("summary", abs.Summary);
        cmd.Parameters.AddWithValue("headers", abs.Headers.ToArray());
        cmd.Parameters.AddWithValue("embedding", abs.SummaryEmbedding != null 
            ? new Vector(abs.SummaryEmbedding) : DBNull.Value);
        cmd.Parameters.AddWithValue("created_at", abs.CreatedAt);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task StorePageWithAbstractAsync(
        MemoryPage page, MemoryAbstract abs, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        
        try
        {
            // Store page
            await using (var cmd = new NpgsqlCommand("""
                INSERT INTO memory_pages (id, owner_id, content, token_count, embedding, metadata, created_at)
                VALUES (@id, @owner_id, @content, @token_count, @embedding, @metadata, @created_at)
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("id", page.Id);
                cmd.Parameters.AddWithValue("owner_id", page.OwnerId);
                cmd.Parameters.AddWithValue("content", page.Content);
                cmd.Parameters.AddWithValue("token_count", page.TokenCount);
                cmd.Parameters.AddWithValue("embedding", page.Embedding != null 
                    ? new Vector(page.Embedding) : DBNull.Value);
                cmd.Parameters.AddWithValue("metadata", page.Metadata != null 
                    ? JsonSerializer.Serialize(page.Metadata) : DBNull.Value);
                cmd.Parameters.AddWithValue("created_at", page.CreatedAt);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // Store abstract  
            await using (var cmd = new NpgsqlCommand("""
                INSERT INTO memory_abstracts (page_id, owner_id, summary, headers, summary_embedding, created_at)
                VALUES (@page_id, @owner_id, @summary, @headers, @embedding, @created_at)
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("page_id", abs.PageId);
                cmd.Parameters.AddWithValue("owner_id", abs.OwnerId);
                cmd.Parameters.AddWithValue("summary", abs.Summary);
                cmd.Parameters.AddWithValue("headers", abs.Headers.ToArray());
                cmd.Parameters.AddWithValue("embedding", abs.SummaryEmbedding != null 
                    ? new Vector(abs.SummaryEmbedding) : DBNull.Value);
                cmd.Parameters.AddWithValue("created_at", abs.CreatedAt);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task DeletePageAsync(Guid pageId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM memory_pages WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", pageId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteByOwnerAsync(string ownerId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM memory_pages WHERE owner_id = @owner_id", conn);
        cmd.Parameters.AddWithValue("owner_id", ownerId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<MemoryStats> GetStatsAsync(string ownerId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT COUNT(*), COALESCE(SUM(token_count), 0), MIN(created_at), MAX(created_at)
            FROM memory_pages WHERE owner_id = @owner_id
            """, conn);
        cmd.Parameters.AddWithValue("owner_id", ownerId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);

        return new MemoryStats
        {
            TotalPages = reader.GetInt32(0),
            TotalTokens = reader.GetInt32(1),
            OldestPage = reader.IsDBNull(2) ? null : reader.GetDateTime(2),
            NewestPage = reader.IsDBNull(3) ? null : reader.GetDateTime(3)
        };
    }

    public async Task<MemoryAbstract?> GetAbstractAsync(Guid pageId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT page_id, owner_id, summary, headers, summary_embedding, created_at
            FROM memory_abstracts WHERE page_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", pageId);
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        
        return MapAbstract(reader);
    }

    public async Task<IReadOnlyList<MemoryAbstract>> GetAbstractsAsync(
        IEnumerable<Guid> pageIds, CancellationToken ct = default)
    {
        var ids = pageIds.ToList();
        if (ids.Count == 0) return Array.Empty<MemoryAbstract>();

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT page_id, owner_id, summary, headers, summary_embedding, created_at
            FROM memory_abstracts WHERE page_id = ANY(@ids)
            """, conn);
        cmd.Parameters.AddWithValue("ids", ids.ToArray());

        var abstracts = new List<MemoryAbstract>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            abstracts.Add(MapAbstract(reader));
        }
        return abstracts;
    }

    private static MemoryPage MapPage(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        OwnerId = reader.GetString(1),
        Content = reader.GetString(2),
        TokenCount = reader.GetInt32(3),
        Embedding = reader.IsDBNull(4) ? null : ((Vector)reader.GetValue(4)).ToArray(),
        Metadata = reader.IsDBNull(5) ? null : 
            JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(5)),
        CreatedAt = reader.GetDateTime(6)
    };

    private static MemoryAbstract MapAbstract(NpgsqlDataReader reader) => new()
    {
        PageId = reader.GetGuid(0),
        OwnerId = reader.GetString(1),
        Summary = reader.GetString(2),
        Headers = ((string[])reader.GetValue(3)).ToList(),
        SummaryEmbedding = reader.IsDBNull(4) ? null : ((Vector)reader.GetValue(4)).ToArray(),
        CreatedAt = reader.GetDateTime(5)
    };
}
