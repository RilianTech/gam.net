using Gam.Core.Abstractions;
using Npgsql;
using Pgvector;

namespace Gam.Storage.Postgres.Retrievers;

/// <summary>
/// Vector/semantic similarity retrieval using pgvector.
/// </summary>
public class PostgresVectorRetriever : IVectorRetriever
{
    private readonly NpgsqlDataSource _dataSource;
    
    public string Name => "vector_semantic";

    public PostgresVectorRetriever(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        RetrievalQuery query, CancellationToken ct = default)
    {
        if (query.QueryEmbedding == null)
            throw new ArgumentException("QueryEmbedding required for vector search");

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        
        var excludeClause = query.ExcludePageIds?.Count > 0 
            ? "AND id != ALL(@exclude_ids)" 
            : "";

        // Using cosine distance: 1 - distance = similarity score
        await using var cmd = new NpgsqlCommand($"""
            SELECT id, 1 - (embedding <=> @embedding) as similarity
            FROM memory_pages
            WHERE owner_id = @owner_id
              AND embedding IS NOT NULL
              {excludeClause}
            ORDER BY embedding <=> @embedding
            LIMIT @limit
            """, conn);

        cmd.Parameters.AddWithValue("owner_id", query.OwnerId);
        cmd.Parameters.AddWithValue("embedding", new Vector(query.QueryEmbedding));
        cmd.Parameters.AddWithValue("limit", query.MaxResults);
        
        if (query.ExcludePageIds?.Count > 0)
            cmd.Parameters.AddWithValue("exclude_ids", query.ExcludePageIds.ToArray());

        var results = new List<RetrievalResult>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        
        while (await reader.ReadAsync(ct))
        {
            var score = reader.GetFloat(1);
            if (score < query.MinScore) continue;
            
            results.Add(new RetrievalResult
            {
                PageId = reader.GetGuid(0),
                Score = score,
                RetrieverName = Name
            });
        }

        return results;
    }
}
