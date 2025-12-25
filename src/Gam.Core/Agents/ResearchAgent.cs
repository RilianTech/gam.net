using System.Diagnostics;
using System.Runtime.CompilerServices;
using Gam.Core.Abstractions;
using Gam.Core.Models;
using Gam.Core.Prompts;
using Microsoft.Extensions.Logging;

namespace Gam.Core.Agents;

/// <summary>
/// Researches memories relevant to a query using iterative retrieval.
/// Runs online (in the critical path of user requests).
/// </summary>
public class ResearchAgent : IResearchAgent
{
    private readonly ILlmProvider _llm;
    private readonly IEmbeddingProvider _embedding;
    private readonly IKeywordRetriever _keywordRetriever;
    private readonly IVectorRetriever _vectorRetriever;
    private readonly IPageIndexRetriever _pageIndexRetriever;
    private readonly IMemoryStore _store;
    private readonly IPromptProvider _promptProvider;
    private readonly ILogger<ResearchAgent> _logger;

    public ResearchAgent(
        ILlmProvider llm,
        IEmbeddingProvider embedding,
        IKeywordRetriever keywordRetriever,
        IVectorRetriever vectorRetriever,
        IPageIndexRetriever pageIndexRetriever,
        IMemoryStore store,
        IPromptProvider promptProvider,
        ILogger<ResearchAgent> logger)
    {
        _llm = llm;
        _embedding = embedding;
        _keywordRetriever = keywordRetriever;
        _vectorRetriever = vectorRetriever;
        _pageIndexRetriever = pageIndexRetriever;
        _store = store;
        _promptProvider = promptProvider;
        _logger = logger;
    }

    public async Task<MemoryContext> ResearchAsync(
        ResearchQuery query,
        ResearchOptions? options = null,
        CancellationToken ct = default)
    {
        var steps = new List<ResearchStep>();
        
        await foreach (var step in ResearchStreamAsync(query, options, ct))
        {
            steps.Add(step);
        }
        
        return steps.LastOrDefault()?.CurrentContext ?? MemoryContext.Empty;
    }

    public async IAsyncEnumerable<ResearchStep> ResearchStreamAsync(
        ResearchQuery query,
        ResearchOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        options ??= new ResearchOptions();
        var stopwatch = Stopwatch.StartNew();
        
        var context = new ResearchContext
        {
            Query = query,
            Options = options,
            RetrievedPageIds = new HashSet<Guid>(),
            Pages = new List<RetrievedPage>(),
            TotalTokens = 0
        };

        _logger.LogInformation("Starting research for query: {Query}", query.Query);

        for (var iteration = 1; iteration <= options.MaxIterations; iteration++)
        {
            _logger.LogDebug("Research iteration {Iteration}", iteration);

            // PLAN phase
            var planStart = stopwatch.Elapsed;
            var plan = await PlanAsync(context, ct);
            
            yield return new ResearchStep
            {
                Iteration = iteration,
                Phase = ResearchPhase.Plan,
                Summary = $"Planning: {plan.Strategy}",
                Duration = stopwatch.Elapsed - planStart,
                Plan = plan.Strategy
            };

            if (plan.IsComplete)
            {
                _logger.LogInformation("Research complete after {Iter} iterations", iteration);
                break;
            }

            // SEARCH phase
            var searchStart = stopwatch.Elapsed;
            var results = await SearchAsync(context, plan, ct);
            
            yield return new ResearchStep
            {
                Iteration = iteration,
                Phase = ResearchPhase.Search,
                Summary = $"Retrieved {results.Count} results",
                Duration = stopwatch.Elapsed - searchStart,
                RetrievalResults = results
            };

            // INTEGRATE phase  
            var integrateStart = stopwatch.Elapsed;
            var integrated = await IntegrateAsync(context, results, ct);
            
            yield return new ResearchStep
            {
                Iteration = iteration,
                Phase = ResearchPhase.Integrate,
                Summary = $"Integrated {integrated} new pages",
                Duration = stopwatch.Elapsed - integrateStart,
                PagesIntegrated = integrated,
                CurrentContext = BuildContext(context, stopwatch.Elapsed, iteration)
            };

            // REFLECT phase
            var reflectStart = stopwatch.Elapsed;
            var shouldContinue = await ReflectAsync(context, ct);
            
            yield return new ResearchStep
            {
                Iteration = iteration,
                Phase = ResearchPhase.Reflect,
                Summary = shouldContinue ? "Continuing research" : "Context sufficient",
                Duration = stopwatch.Elapsed - reflectStart,
                ShouldContinue = shouldContinue,
                CurrentContext = BuildContext(context, stopwatch.Elapsed, iteration)
            };

            if (!shouldContinue) break;
        }

        _logger.LogInformation("Research completed with {PageCount} pages, {TokenCount} tokens", 
            context.Pages.Count, context.TotalTokens);
    }

    private async Task<ResearchPlan> PlanAsync(ResearchContext ctx, CancellationToken ct)
    {
        var messages = new List<LlmMessage>
        {
            new(LlmRole.System, _promptProvider.GetPlanSystemPrompt()),
            new(LlmRole.User, _promptProvider.BuildPlanUserPrompt(ctx.Query.Query, ctx.Pages))
        };

        var response = await _llm.CompleteAsync(messages, 
            new LlmOptions { Temperature = 0.3f, MaxTokens = 500 }, ct);

        return ParsePlanResponse(response.Content);
    }

    private async Task<IReadOnlyList<RetrievalResult>> SearchAsync(
        ResearchContext ctx, ResearchPlan plan, CancellationToken ct)
    {
        var queryEmbedding = await _embedding.EmbedAsync(plan.SearchQuery, ct);
        var tasks = new List<Task<IReadOnlyList<RetrievalResult>>>();

        var baseQuery = new RetrievalQuery
        {
            OwnerId = ctx.Query.OwnerId,
            Query = plan.SearchQuery,
            QueryEmbedding = queryEmbedding,
            MaxResults = ctx.Options.MaxPagesPerIteration,
            MinScore = ctx.Options.MinRelevanceScore,
            ExcludePageIds = ctx.RetrievedPageIds
        };

        if (plan.UseKeywordSearch)
            tasks.Add(_keywordRetriever.RetrieveAsync(baseQuery, ct));
        if (plan.UseVectorSearch)
            tasks.Add(_vectorRetriever.RetrieveAsync(baseQuery, ct));
        if (plan.UsePageIndex && plan.TargetHeaders?.Count > 0)
        {
            foreach (var header in plan.TargetHeaders)
                tasks.Add(_pageIndexRetriever.RetrieveAsync(baseQuery with { Query = header }, ct));
        }

        if (tasks.Count == 0)
        {
            // Default to both keyword and vector search
            tasks.Add(_keywordRetriever.RetrieveAsync(baseQuery, ct));
            tasks.Add(_vectorRetriever.RetrieveAsync(baseQuery, ct));
        }

        var allResults = await Task.WhenAll(tasks);
        
        // Merge and deduplicate
        var seen = new HashSet<Guid>();
        var results = new List<RetrievalResult>();
        foreach (var set in allResults)
            foreach (var r in set)
                if (seen.Add(r.PageId)) results.Add(r);

        return results.OrderByDescending(r => r.Score).ToList();
    }

    private async Task<int> IntegrateAsync(
        ResearchContext ctx, IReadOnlyList<RetrievalResult> results, CancellationToken ct)
    {
        var newIds = results.Where(r => !ctx.RetrievedPageIds.Contains(r.PageId))
            .Select(r => r.PageId).ToList();
        if (newIds.Count == 0) return 0;

        var pages = await _store.GetPagesAsync(newIds, ct);
        var count = 0;

        foreach (var page in pages)
        {
            if (ctx.TotalTokens + page.TokenCount > ctx.Options.MaxContextTokens) break;

            var result = results.First(r => r.PageId == page.Id);
            ctx.Pages.Add(new RetrievedPage
            {
                PageId = page.Id,
                Content = page.Content,
                TokenCount = page.TokenCount,
                RelevanceScore = result.Score,
                RetrievedBy = result.RetrieverName,
                CreatedAt = page.CreatedAt
            });
            ctx.RetrievedPageIds.Add(page.Id);
            ctx.TotalTokens += page.TokenCount;
            count++;
        }
        return count;
    }

    private async Task<bool> ReflectAsync(ResearchContext ctx, CancellationToken ct)
    {
        if (ctx.TotalTokens >= ctx.Options.MaxContextTokens * 0.9) return false;
        if (ctx.Pages.Count == 0) return true;

        var messages = new List<LlmMessage>
        {
            new(LlmRole.System, _promptProvider.GetReflectSystemPrompt()),
            new(LlmRole.User, _promptProvider.BuildReflectUserPrompt(ctx.Query.Query, ctx.Pages))
        };

        var response = await _llm.CompleteAsync(messages,
            new LlmOptions { Temperature = 0.1f, MaxTokens = 50 }, ct);

        return response.Content.Contains("CONTINUE", StringComparison.OrdinalIgnoreCase);
    }

    private static MemoryContext BuildContext(ResearchContext ctx, TimeSpan duration, int iter)
        => new()
        {
            Pages = ctx.Pages.OrderByDescending(p => p.RelevanceScore).ToList(),
            TotalTokens = ctx.TotalTokens,
            IterationsPerformed = iter,
            Duration = duration
        };

    private static ResearchPlan ParsePlanResponse(string response)
    {
        var plan = new ResearchPlan();
        foreach (var line in response.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("STRATEGY:", StringComparison.OrdinalIgnoreCase))
                plan.Strategy = t["STRATEGY:".Length..].Trim();
            else if (t.StartsWith("SEARCH_QUERY:", StringComparison.OrdinalIgnoreCase))
                plan.SearchQuery = t["SEARCH_QUERY:".Length..].Trim();
            else if (t.StartsWith("USE_KEYWORD:", StringComparison.OrdinalIgnoreCase))
                plan.UseKeywordSearch = t.Contains("true", StringComparison.OrdinalIgnoreCase);
            else if (t.StartsWith("USE_VECTOR:", StringComparison.OrdinalIgnoreCase))
                plan.UseVectorSearch = t.Contains("true", StringComparison.OrdinalIgnoreCase);
            else if (t.StartsWith("USE_INDEX:", StringComparison.OrdinalIgnoreCase))
                plan.UsePageIndex = t.Contains("true", StringComparison.OrdinalIgnoreCase);
            else if (t.StartsWith("TARGET_HEADERS:", StringComparison.OrdinalIgnoreCase))
                plan.TargetHeaders = t["TARGET_HEADERS:".Length..]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(h => h.Trim()).ToList();
            else if (t.StartsWith("COMPLETE:", StringComparison.OrdinalIgnoreCase))
                plan.IsComplete = t.Contains("true", StringComparison.OrdinalIgnoreCase);
        }
        
        // Default search query to the original query if not specified
        if (string.IsNullOrEmpty(plan.SearchQuery))
            plan.SearchQuery = "general search";
            
        return plan;
    }

    private class ResearchContext
    {
        public required ResearchQuery Query { get; init; }
        public required ResearchOptions Options { get; init; }
        public required HashSet<Guid> RetrievedPageIds { get; init; }
        public required List<RetrievedPage> Pages { get; init; }
        public int TotalTokens { get; set; }
    }

    private class ResearchPlan
    {
        public string Strategy { get; set; } = "";
        public string SearchQuery { get; set; } = "";
        public bool UseKeywordSearch { get; set; } = true;
        public bool UseVectorSearch { get; set; } = true;
        public bool UsePageIndex { get; set; }
        public List<string>? TargetHeaders { get; set; }
        public bool IsComplete { get; set; }
    }
}
