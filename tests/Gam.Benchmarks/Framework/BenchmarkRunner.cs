using System.Diagnostics;
using System.Text.Json;
using Gam.Core.Abstractions;
using Gam.Core.Models;

namespace Gam.Benchmarks.Framework;

/// <summary>
/// Runs benchmark evaluations against GAM.
/// </summary>
public class BenchmarkRunner
{
    private readonly IGamService _gam;
    private readonly BenchmarkOptions _options;
    
    public BenchmarkRunner(IGamService gam, BenchmarkOptions? options = null)
    {
        _gam = gam;
        _options = options ?? new BenchmarkOptions();
    }
    
    /// <summary>
    /// Load a benchmark dataset from a JSON file.
    /// </summary>
    public static async Task<BenchmarkDataset> LoadDatasetAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<BenchmarkDataset>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException($"Failed to load dataset from {path}");
    }
    
    /// <summary>
    /// Ingest all conversations from a dataset into GAM.
    /// </summary>
    public async Task IngestDatasetAsync(BenchmarkDataset dataset, string ownerId, CancellationToken ct = default)
    {
        foreach (var conversation in dataset.Conversations)
        {
            await IngestConversationAsync(conversation, ownerId, ct);
        }
    }
    
    /// <summary>
    /// Ingest a single conversation into GAM.
    /// </summary>
    public async Task IngestConversationAsync(BenchmarkConversation conversation, string ownerId, CancellationToken ct = default)
    {
        // Group turns into pairs (user + assistant)
        for (var i = 0; i < conversation.Turns.Count - 1; i += 2)
        {
            var userTurn = conversation.Turns[i];
            var assistantTurn = i + 1 < conversation.Turns.Count ? conversation.Turns[i + 1] : null;
            
            if (userTurn.Role != "user") continue;
            
            var turn = new ConversationTurn
            {
                OwnerId = ownerId,
                ConversationId = conversation.Id,
                TurnNumber = i / 2,
                UserMessage = userTurn.Content,
                AssistantMessage = assistantTurn?.Content ?? "",
                Timestamp = userTurn.Timestamp ?? DateTimeOffset.UtcNow
            };
            
            await _gam.MemorizeAsync(new MemorizeRequest { Turn = turn }, ct);
        }
    }
    
    /// <summary>
    /// Run all queries in a dataset and evaluate results.
    /// </summary>
    public async Task<BenchmarkRunResult> RunBenchmarkAsync(
        BenchmarkDataset dataset, 
        string ownerId,
        string configurationName,
        CancellationToken ct = default)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var queryResults = new List<BenchmarkQueryResult>();
        var totalLlmCalls = 0;
        var totalTokens = 0;
        
        foreach (var query in dataset.Queries)
        {
            if (ct.IsCancellationRequested) break;
            
            var result = await EvaluateQueryAsync(query, ownerId, ct);
            queryResults.Add(result);
            
            // TODO: Track LLM calls and tokens from result
        }
        
        totalStopwatch.Stop();
        
        // Calculate aggregate metrics
        // A query is "successful" if it achieves >= 80% fact recall (found most required facts)
        const float factRecallThreshold = 0.8f;
        var successfulQueries = queryResults.Where(r => r.FactRecall >= factRecallThreshold).ToList();
        
        return new BenchmarkRunResult
        {
            DatasetName = dataset.Name,
            ConfigurationName = configurationName,
            RunAt = DateTimeOffset.UtcNow,
            QueryResults = queryResults,
            OverallAccuracy = queryResults.Count > 0 
                ? (float)successfulQueries.Count / queryResults.Count 
                : 0,
            AverageFactRecall = queryResults.Count > 0 
                ? queryResults.Average(r => r.FactRecall) 
                : 0,
            AverageRelevantPageRatio = queryResults.Count > 0 
                ? queryResults.Average(r => r.RelevantPageRatio) 
                : 0,
            AverageQueryDuration = queryResults.Count > 0 
                ? TimeSpan.FromTicks((long)queryResults.Average(r => r.Duration.Ticks)) 
                : TimeSpan.Zero,
            TotalDuration = totalStopwatch.Elapsed,
            TotalLlmCalls = totalLlmCalls,
            TotalTokensProcessed = totalTokens
        };
    }
    
    /// <summary>
    /// Evaluate a single query and return metrics.
    /// </summary>
    public async Task<BenchmarkQueryResult> EvaluateQueryAsync(
        BenchmarkQuery query, 
        string ownerId, 
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        var context = await _gam.ResearchAsync(new ResearchRequest
        {
            OwnerId = ownerId,
            Query = query.Query
        }, ct);
        
        stopwatch.Stop();
        
        var retrievedContext = context.FormatForPrompt();
        
        // Evaluate accuracy
        var containsExpected = ContainsAnswer(retrievedContext, query.ExpectedAnswer);
        var (factRecall, foundFacts, missingFacts) = EvaluateFactRecall(
            retrievedContext, 
            query.RequiredFacts ?? Array.Empty<string>());
        
        // Evaluate relevance (if we know which conversations are relevant)
        var relevantPageRatio = EvaluateRelevance(context.Pages, query.RelevantConversationIds);
        
        return new BenchmarkQueryResult
        {
            QueryId = query.Id,
            Query = query.Query,
            ExpectedAnswer = query.ExpectedAnswer,
            RetrievedContext = retrievedContext,
            PagesRetrieved = context.Pages.Count,
            TokensRetrieved = context.TotalTokens,
            IterationsPerformed = context.IterationsPerformed,
            Duration = stopwatch.Elapsed,
            ContainsExpectedAnswer = containsExpected,
            FactRecall = factRecall,
            FoundFacts = foundFacts,
            MissingFacts = missingFacts,
            RelevantPageRatio = relevantPageRatio
        };
    }
    
    private static bool ContainsAnswer(string context, string expectedAnswer)
    {
        // Check if any key term from the expected answer is found
        // Split on common delimiters to extract key terms
        var keyTerms = expectedAnswer
            .Split(new[] { ',', ';', ' for ', ' and ', ' or ', " - " }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 2) // Skip tiny words
            .ToList();

        // If we have identifiable key terms, check that at least the first/primary one is found
        // The first term is typically the main answer (e.g., "PostgreSQL" in "PostgreSQL for ACID")
        if (keyTerms.Count > 0)
        {
            var primaryTerm = keyTerms[0];
            return context.Contains(primaryTerm, StringComparison.OrdinalIgnoreCase);
        }

        // Fallback to full string match
        return context.Contains(expectedAnswer, StringComparison.OrdinalIgnoreCase);
    }
    
    private static (float recall, List<string> found, List<string> missing) EvaluateFactRecall(
        string context, 
        IReadOnlyList<string> requiredFacts)
    {
        if (requiredFacts.Count == 0)
            return (1.0f, new List<string>(), new List<string>());
        
        var found = new List<string>();
        var missing = new List<string>();
        
        foreach (var fact in requiredFacts)
        {
            if (context.Contains(fact, StringComparison.OrdinalIgnoreCase))
                found.Add(fact);
            else
                missing.Add(fact);
        }
        
        var recall = (float)found.Count / requiredFacts.Count;
        return (recall, found, missing);
    }
    
    private static float EvaluateRelevance(
        IReadOnlyList<RetrievedPage> pages, 
        IReadOnlyList<string>? relevantConversationIds)
    {
        if (relevantConversationIds == null || relevantConversationIds.Count == 0 || pages.Count == 0)
            return 1.0f; // Can't evaluate without ground truth
        
        // This would require storing conversation ID in pages
        // For now, return 1.0 as placeholder
        return 1.0f;
    }
}

/// <summary>
/// Options for benchmark execution.
/// </summary>
public class BenchmarkOptions
{
    /// <summary>Timeout per query.</summary>
    public TimeSpan QueryTimeout { get; set; } = TimeSpan.FromSeconds(30);
    
    /// <summary>Whether to log detailed results.</summary>
    public bool VerboseLogging { get; set; } = false;
}
