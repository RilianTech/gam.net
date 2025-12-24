using Gam.Core.Abstractions;
using Npgsql;

namespace Gam.Storage.Postgres.Retrievers;

/// <summary>
/// Direct page lookup by header/index matching.
/// </summary>
public class PostgresPageIndexRetriever : IPageIndexRetriever
{
    private readonly NpgsqlDataSource _dataSource;
    
    public string Name => "page_index";

    public PostgresPageIndexRetriever(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        RetrievalQuery query, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        
        var excludeClause = query.ExcludePageIds?.Count > 0 
            ? "AND page_id != ALL(@exclude_ids)" 
            : "";

        // Search headers array for matching terms
        await using var cmd = new NpgsqlCommand($"""
            SELECT page_id, 
                   (SELECT h FROM unnest(headers) h WHERE h ILIKE '%' || @query || '%' LIMIT 1) as matched_header
            FROM memory_abstracts
            WHERE owner_id = @owner_id
              AND EXISTS (SELECT 1 FROM unnest(headers) h WHERE h ILIKE '%' || @query || '%')
              {excludeClause}
            LIMIT @limit
            """, conn);

        cmd.Parameters.AddWithValue("owner_id", query.OwnerId);
        cmd.Parameters.AddWithValue("query", query.Query);
        cmd.Parameters.AddWithValue("limit", query.MaxResults);
        
        if (query.ExcludePageIds?.Count > 0)
            cmd.Parameters.AddWithValue("exclude_ids", query.ExcludePageIds.ToArray());

        var results = new List<RetrievalResult>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        
        while (await reader.ReadAsync(ct))
        {
            results.Add(new RetrievalResult
            {
                PageId = reader.GetGuid(0),
                Score = 1.0f,  // Direct match = high confidence
                RetrieverName = Name,
                MatchedHeader = reader.IsDBNull(1) ? null : reader.GetString(1)
            });
        }

        return results;
    }
}
