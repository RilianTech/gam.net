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
            SELECT id, owner_id, content, token_count, embedding, metadata, created_at,
                   importance, access_count, last_accessed_at
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
            SELECT id, owner_id, content, token_count, embedding, metadata, created_at,
                   importance, access_count, last_accessed_at
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
            INSERT INTO memory_pages (id, owner_id, content, token_count, embedding, metadata, created_at,
                                      importance, access_count, last_accessed_at)
            VALUES (@id, @owner_id, @content, @token_count, @embedding, @metadata, @created_at,
                    @importance, @access_count, @last_accessed_at)
            ON CONFLICT (id) DO UPDATE SET
                content = EXCLUDED.content,
                token_count = EXCLUDED.token_count,
                embedding = EXCLUDED.embedding,
                metadata = EXCLUDED.metadata,
                importance = EXCLUDED.importance
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
        cmd.Parameters.AddWithValue("importance", page.Importance);
        cmd.Parameters.AddWithValue("access_count", page.AccessCount);
        cmd.Parameters.AddWithValue("last_accessed_at", page.LastAccessedAt.HasValue 
            ? page.LastAccessedAt.Value.UtcDateTime : DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task StoreAbstractAsync(MemoryAbstract abs, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO memory_abstracts (page_id, owner_id, summary, headers, summary_embedding, created_at,
                                          memory_type, tags)
            VALUES (@page_id, @owner_id, @summary, @headers, @embedding, @created_at,
                    @memory_type, @tags)
            ON CONFLICT (page_id) DO UPDATE SET
                summary = EXCLUDED.summary,
                headers = EXCLUDED.headers,
                summary_embedding = EXCLUDED.summary_embedding,
                memory_type = EXCLUDED.memory_type,
                tags = EXCLUDED.tags
            """, conn);

        cmd.Parameters.AddWithValue("page_id", abs.PageId);
        cmd.Parameters.AddWithValue("owner_id", abs.OwnerId);
        cmd.Parameters.AddWithValue("summary", abs.Summary);
        cmd.Parameters.AddWithValue("headers", abs.Headers.ToArray());
        cmd.Parameters.AddWithValue("embedding", abs.SummaryEmbedding != null 
            ? new Vector(abs.SummaryEmbedding) : DBNull.Value);
        cmd.Parameters.AddWithValue("created_at", abs.CreatedAt);
        cmd.Parameters.AddWithValue("memory_type", abs.Type.ToDbString());
        cmd.Parameters.AddWithValue("tags", abs.Tags.ToArray());

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
                INSERT INTO memory_pages (id, owner_id, content, token_count, embedding, metadata, created_at,
                                          importance, access_count, last_accessed_at)
                VALUES (@id, @owner_id, @content, @token_count, @embedding, @metadata, @created_at,
                        @importance, @access_count, @last_accessed_at)
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
                cmd.Parameters.AddWithValue("importance", page.Importance);
                cmd.Parameters.AddWithValue("access_count", page.AccessCount);
                cmd.Parameters.AddWithValue("last_accessed_at", page.LastAccessedAt.HasValue 
                    ? page.LastAccessedAt.Value.UtcDateTime : DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // Store abstract  
            await using (var cmd = new NpgsqlCommand("""
                INSERT INTO memory_abstracts (page_id, owner_id, summary, headers, summary_embedding, created_at,
                                              memory_type, tags)
                VALUES (@page_id, @owner_id, @summary, @headers, @embedding, @created_at,
                        @memory_type, @tags)
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("page_id", abs.PageId);
                cmd.Parameters.AddWithValue("owner_id", abs.OwnerId);
                cmd.Parameters.AddWithValue("summary", abs.Summary);
                cmd.Parameters.AddWithValue("headers", abs.Headers.ToArray());
                cmd.Parameters.AddWithValue("embedding", abs.SummaryEmbedding != null 
                    ? new Vector(abs.SummaryEmbedding) : DBNull.Value);
                cmd.Parameters.AddWithValue("created_at", abs.CreatedAt);
                cmd.Parameters.AddWithValue("memory_type", abs.Type.ToDbString());
                cmd.Parameters.AddWithValue("tags", abs.Tags.ToArray());
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

    public async Task<int> CleanupExpiredAsync(TimeSpan maxAge, string? ownerId = null, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        return await DeleteBeforeAsync(cutoff, ownerId, ct);
    }

    public async Task<int> DeleteBeforeAsync(DateTimeOffset before, string? ownerId = null, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        
        var sql = ownerId != null
            ? "DELETE FROM memory_pages WHERE created_at < @before AND owner_id = @owner_id"
            : "DELETE FROM memory_pages WHERE created_at < @before";
            
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("before", before.UtcDateTime);
        
        if (ownerId != null)
            cmd.Parameters.AddWithValue("owner_id", ownerId);

        var deleted = await cmd.ExecuteNonQueryAsync(ct);
        
        if (deleted > 0)
            _logger.LogInformation("Cleaned up {Count} expired memory pages (before {Before})", deleted, before);
            
        return deleted;
    }

    public async Task<MemoryAbstract?> GetAbstractAsync(Guid pageId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT page_id, owner_id, summary, headers, summary_embedding, created_at,
                   memory_type, tags
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
            SELECT page_id, owner_id, summary, headers, summary_embedding, created_at,
                   memory_type, tags
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

    public async Task<IReadOnlyList<MemoryAbstract>> GetAbstractsAsync(
        string ownerId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT page_id, owner_id, summary, headers, summary_embedding, created_at,
                   memory_type, tags
            FROM memory_abstracts 
            WHERE owner_id = @owner_id
            ORDER BY created_at DESC
            """, conn);
        cmd.Parameters.AddWithValue("owner_id", ownerId);

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
        CreatedAt = reader.GetDateTime(6),
        // ADR-0002 fields (with fallback for existing data without these columns)
        Importance = reader.FieldCount > 7 && !reader.IsDBNull(7) ? reader.GetFloat(7) : 0.5f,
        AccessCount = reader.FieldCount > 8 && !reader.IsDBNull(8) ? reader.GetInt32(8) : 0,
        LastAccessedAt = reader.FieldCount > 9 && !reader.IsDBNull(9) ? reader.GetDateTime(9) : null
    };

    private static MemoryAbstract MapAbstract(NpgsqlDataReader reader) => new()
    {
        PageId = reader.GetGuid(0),
        OwnerId = reader.GetString(1),
        Summary = reader.GetString(2),
        Headers = ((string[])reader.GetValue(3)).ToList(),
        SummaryEmbedding = reader.IsDBNull(4) ? null : ((Vector)reader.GetValue(4)).ToArray(),
        CreatedAt = reader.GetDateTime(5),
        // ADR-0002 fields (with fallback for existing data without these columns)
        Type = reader.FieldCount > 6 && !reader.IsDBNull(6) 
            ? MemoryTypeExtensions.ParseMemoryType(reader.GetString(6)) 
            : MemoryType.Conversation,
        Tags = reader.FieldCount > 7 && !reader.IsDBNull(7) 
            ? ((string[])reader.GetValue(7)).ToList() 
            : []
    };
    
    /// <summary>
    /// Updates access tracking for retrieved pages.
    /// </summary>
    public async Task UpdateAccessAsync(IEnumerable<Guid> pageIds, CancellationToken ct = default)
    {
        var ids = pageIds.ToList();
        if (ids.Count == 0) return;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            UPDATE memory_pages 
            SET access_count = access_count + 1,
                last_accessed_at = NOW()
            WHERE id = ANY(@ids)
            """, conn);
        cmd.Parameters.AddWithValue("ids", ids.ToArray());
        await cmd.ExecuteNonQueryAsync(ct);
    }
    
    // ========================================================================
    // Relationship operations (ADR-0002 Phase 4)
    // ========================================================================
    
    public async Task StoreRelationshipAsync(MemoryRelationship relationship, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO memory_relationships (id, source_page_id, target_page_id, relationship_type, confidence, created_by, created_at)
            VALUES (@id, @source_page_id, @target_page_id, @relationship_type, @confidence, @created_by, @created_at)
            ON CONFLICT (source_page_id, target_page_id, relationship_type) DO UPDATE SET
                confidence = EXCLUDED.confidence,
                created_by = EXCLUDED.created_by
            """, conn);
        
        AddRelationshipParams(cmd, relationship);
        await cmd.ExecuteNonQueryAsync(ct);
    }
    
    public async Task StoreRelationshipsAsync(IEnumerable<MemoryRelationship> relationships, CancellationToken ct = default)
    {
        var list = relationships.ToList();
        if (list.Count == 0) return;
        
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        
        try
        {
            foreach (var rel in list)
            {
                await using var cmd = new NpgsqlCommand("""
                    INSERT INTO memory_relationships (id, source_page_id, target_page_id, relationship_type, confidence, created_by, created_at)
                    VALUES (@id, @source_page_id, @target_page_id, @relationship_type, @confidence, @created_by, @created_at)
                    ON CONFLICT (source_page_id, target_page_id, relationship_type) DO NOTHING
                    """, conn, tx);
                
                AddRelationshipParams(cmd, rel);
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
    
    public async Task<IReadOnlyList<MemoryRelationship>> GetRelationshipsFromAsync(
        IEnumerable<Guid> sourcePageIds,
        RelationshipType[]? types = null,
        CancellationToken ct = default)
    {
        var ids = sourcePageIds.ToList();
        if (ids.Count == 0) return [];
        
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        
        var sql = """
            SELECT id, source_page_id, target_page_id, relationship_type, confidence, created_by, created_at
            FROM memory_relationships
            WHERE source_page_id = ANY(@ids)
            """;
        
        if (types is { Length: > 0 })
        {
            sql += " AND relationship_type = ANY(@types)";
        }
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("ids", ids.ToArray());
        
        if (types is { Length: > 0 })
        {
            cmd.Parameters.AddWithValue("types", types.Select(t => t.ToDbString()).ToArray());
        }
        
        var relationships = new List<MemoryRelationship>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            relationships.Add(MapRelationship(reader));
        }
        
        return relationships;
    }
    
    public async Task<IReadOnlyList<Guid>> GetRelatedPageIdsAsync(
        IEnumerable<Guid> sourcePageIds,
        RelationshipType[]? types = null,
        int maxPerSource = 3,
        CancellationToken ct = default)
    {
        var ids = sourcePageIds.ToList();
        if (ids.Count == 0) return [];
        
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        
        // Use window function to limit results per source
        var sql = """
            WITH ranked AS (
                SELECT target_page_id,
                       ROW_NUMBER() OVER (PARTITION BY source_page_id ORDER BY confidence DESC) as rn
                FROM memory_relationships
                WHERE source_page_id = ANY(@ids)
            """;
        
        if (types is { Length: > 0 })
        {
            sql += " AND relationship_type = ANY(@types)";
        }
        
        sql += $"""
            )
            SELECT DISTINCT target_page_id FROM ranked WHERE rn <= {maxPerSource}
            """;
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("ids", ids.ToArray());
        
        if (types is { Length: > 0 })
        {
            cmd.Parameters.AddWithValue("types", types.Select(t => t.ToDbString()).ToArray());
        }
        
        var relatedIds = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            relatedIds.Add(reader.GetGuid(0));
        }
        
        return relatedIds;
    }
    
    private static void AddRelationshipParams(NpgsqlCommand cmd, MemoryRelationship rel)
    {
        cmd.Parameters.AddWithValue("id", rel.Id);
        cmd.Parameters.AddWithValue("source_page_id", rel.SourcePageId);
        cmd.Parameters.AddWithValue("target_page_id", rel.TargetPageId);
        cmd.Parameters.AddWithValue("relationship_type", rel.Type.ToDbString());
        cmd.Parameters.AddWithValue("confidence", rel.Confidence);
        cmd.Parameters.AddWithValue("created_by", rel.CreatedBy.ToDbString());
        cmd.Parameters.AddWithValue("created_at", rel.CreatedAt.UtcDateTime);
    }
    
    private static MemoryRelationship MapRelationship(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        SourcePageId = reader.GetGuid(1),
        TargetPageId = reader.GetGuid(2),
        Type = RelationshipTypeExtensions.ParseRelationshipType(reader.GetString(3)),
        Confidence = reader.GetFloat(4),
        CreatedBy = RelationshipTypeExtensions.ParseCreator(reader.GetString(5)),
        CreatedAt = reader.GetDateTime(6)
    };
}
