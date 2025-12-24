namespace Gam.Core.Abstractions;

using Gam.Core.Models;

/// <summary>
/// Base interface for all retrievers.
/// </summary>
public interface IRetriever
{
    string Name { get; }
    Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        RetrievalQuery query, 
        CancellationToken ct = default);
}

/// <summary>
/// BM25 keyword-based retrieval.
/// </summary>
public interface IKeywordRetriever : IRetriever { }

/// <summary>
/// Vector/semantic similarity retrieval.
/// </summary>
public interface IVectorRetriever : IRetriever { }

/// <summary>
/// Direct page lookup by header/index.
/// </summary>
public interface IPageIndexRetriever : IRetriever { }

/// <summary>
/// Query for retrieving memories.
/// </summary>
public record RetrievalQuery
{
    public required string OwnerId { get; init; }
    public required string Query { get; init; }
    public float[]? QueryEmbedding { get; init; }
    public int MaxResults { get; init; } = 10;
    public float MinScore { get; init; } = 0.0f;
    public IReadOnlySet<Guid>? ExcludePageIds { get; init; }
}

/// <summary>
/// Result from a retrieval operation.
/// </summary>
public record RetrievalResult
{
    public required Guid PageId { get; init; }
    public required float Score { get; init; }
    public required string RetrieverName { get; init; }
    public string? MatchedHeader { get; init; }
    public string? MatchedSnippet { get; init; }
}
