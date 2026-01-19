namespace Gam.Benchmarks.Framework;

/// <summary>
/// A conversation in the benchmark dataset.
/// Based on LoCoMo format: https://github.com/snap-stanford/locomo
/// </summary>
public record BenchmarkConversation
{
    /// <summary>Unique conversation identifier.</summary>
    public required string Id { get; init; }
    
    /// <summary>The conversation turns (messages).</summary>
    public required IReadOnlyList<BenchmarkTurn> Turns { get; init; }
    
    /// <summary>Metadata about the conversation.</summary>
    public BenchmarkConversationMetadata? Metadata { get; init; }
}

public record BenchmarkTurn
{
    public required string Role { get; init; }  // "user" or "assistant"
    public required string Content { get; init; }
    public DateTimeOffset? Timestamp { get; init; }
}

public record BenchmarkConversationMetadata
{
    public string? Topic { get; init; }
    public string? Domain { get; init; }
    public int? TurnCount { get; init; }
}

/// <summary>
/// A query in the benchmark dataset with expected answer.
/// </summary>
public record BenchmarkQuery
{
    /// <summary>Unique query identifier.</summary>
    public required string Id { get; init; }
    
    /// <summary>The query text.</summary>
    public required string Query { get; init; }
    
    /// <summary>Expected answer or key facts that should be found.</summary>
    public required string ExpectedAnswer { get; init; }
    
    /// <summary>Conversation IDs that contain the relevant information.</summary>
    public IReadOnlyList<string>? RelevantConversationIds { get; init; }
    
    /// <summary>Key facts/entities that must be present in a correct answer.</summary>
    public IReadOnlyList<string>? RequiredFacts { get; init; }
    
    /// <summary>Query category for analysis.</summary>
    public string? Category { get; init; }
    
    /// <summary>Difficulty level: easy, medium, hard.</summary>
    public string? Difficulty { get; init; }
}

/// <summary>
/// A complete benchmark dataset.
/// </summary>
public record BenchmarkDataset
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyList<BenchmarkConversation> Conversations { get; init; }
    public required IReadOnlyList<BenchmarkQuery> Queries { get; init; }
    public BenchmarkDatasetMetadata? Metadata { get; init; }
}

public record BenchmarkDatasetMetadata
{
    public string? Description { get; init; }
    public string? Source { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
}

/// <summary>
/// Result of evaluating a single query.
/// </summary>
public record BenchmarkQueryResult
{
    public required string QueryId { get; init; }
    public required string Query { get; init; }
    public required string ExpectedAnswer { get; init; }
    public required string RetrievedContext { get; init; }
    public required int PagesRetrieved { get; init; }
    public required int TokensRetrieved { get; init; }
    public required int IterationsPerformed { get; init; }
    public required TimeSpan Duration { get; init; }
    
    // Accuracy metrics
    public required bool ContainsExpectedAnswer { get; init; }
    public required float FactRecall { get; init; }  // % of required facts found
    public IReadOnlyList<string>? FoundFacts { get; init; }
    public IReadOnlyList<string>? MissingFacts { get; init; }
    
    // Relevance metrics
    public required float RelevantPageRatio { get; init; }  // % of retrieved pages that are relevant
}

/// <summary>
/// Aggregated results for a benchmark run.
/// </summary>
public record BenchmarkRunResult
{
    public required string DatasetName { get; init; }
    public required string ConfigurationName { get; init; }
    public required DateTimeOffset RunAt { get; init; }
    public required IReadOnlyList<BenchmarkQueryResult> QueryResults { get; init; }
    
    // Aggregate metrics
    public required float OverallAccuracy { get; init; }  // % of queries with correct answer
    public required float AverageFactRecall { get; init; }
    public required float AverageRelevantPageRatio { get; init; }
    public required TimeSpan AverageQueryDuration { get; init; }
    public required TimeSpan TotalDuration { get; init; }
    
    // Resource metrics
    public required int TotalLlmCalls { get; init; }
    public required int TotalTokensProcessed { get; init; }
}
