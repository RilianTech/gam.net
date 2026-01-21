using System.Diagnostics;
using System.Runtime.CompilerServices;
using Gam.Core.Abstractions;
using Gam.Core.Models;
using Gam.Core.Prompts;
using Microsoft.Extensions.Logging;

namespace Gam.Core.Agents;

/// <summary>
/// Deep Research Agent implementing the full GAM research loop:
/// Plan (multi-query) → Search (all queries) → Integrate (LLM synthesis) → Reflect (two-step)
/// 
/// This is the core innovation of GAM - intensive memory research at runtime.
/// </summary>
public class DeepResearchAgent : IResearchAgent
{
    private readonly ILlmProvider _llm;
    private readonly IEmbeddingProvider _embedding;
    private readonly IKeywordRetriever _keywordRetriever;
    private readonly IVectorRetriever _vectorRetriever;
    private readonly IPageIndexRetriever _pageIndexRetriever;
    private readonly IMemoryStore _store;
    private readonly ILogger<DeepResearchAgent> _logger;
    private readonly DeepResearchOptions _options;

    public DeepResearchAgent(
        ILlmProvider llm,
        IEmbeddingProvider embedding,
        IKeywordRetriever keywordRetriever,
        IVectorRetriever vectorRetriever,
        IPageIndexRetriever pageIndexRetriever,
        IMemoryStore store,
        ILogger<DeepResearchAgent> logger,
        DeepResearchOptions? options = null)
    {
        _llm = llm;
        _embedding = embedding;
        _keywordRetriever = keywordRetriever;
        _vectorRetriever = vectorRetriever;
        _pageIndexRetriever = pageIndexRetriever;
        _store = store;
        _logger = logger;
        _options = options ?? new DeepResearchOptions();
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
        var stopwatch = Stopwatch.StartNew();
        
        var context = new DeepResearchContext
        {
            Query = query,
            RetrievedPageIds = new HashSet<Guid>(),
            Pages = new List<RetrievedPage>(),
            IntegratedResult = IntegratedResult.Empty,
            TotalTokens = 0
        };

        _logger.LogInformation("Starting Deep Research for query: {Query}", query.Query);

        // Load memory abstracts once
        var abstracts = await _store.GetAbstractsAsync(query.OwnerId, ct);
        _logger.LogDebug("Loaded {Count} memory abstracts", abstracts.Count);

        for (var iteration = 1; iteration <= _options.MaxIterations; iteration++)
        {
            _logger.LogDebug("Deep Research iteration {Iteration}", iteration);

            // === PLAN PHASE ===
            var planStart = stopwatch.Elapsed;
            var plan = await PlanAsync(context, abstracts, iteration, ct);
            
            yield return new ResearchStep
            {
                Iteration = iteration,
                Phase = ResearchPhase.Plan,
                Summary = $"Planning: {plan.Strategy} ({plan.KeywordQueries.Count} keyword, {plan.VectorQueries.Count} vector queries)",
                Duration = stopwatch.Elapsed - planStart,
                Plan = plan.Strategy
            };

            if (plan.IsComplete)
            {
                _logger.LogInformation("Deep Research complete after {Iter} iterations (plan indicated complete)", iteration);
                yield return CreateFinalStep(context, stopwatch.Elapsed, iteration);
                yield break;
            }

            // === SEARCH PHASE ===
            var searchStart = stopwatch.Elapsed;
            var results = await SearchAsync(context, plan, ct);
            
            yield return new ResearchStep
            {
                Iteration = iteration,
                Phase = ResearchPhase.Search,
                Summary = $"Retrieved {results.Count} results from {plan.KeywordQueries.Count + plan.VectorQueries.Count} queries",
                Duration = stopwatch.Elapsed - searchStart,
                RetrievalResults = results
            };

            // === INTEGRATE PHASE ===
            var integrateStart = stopwatch.Elapsed;
            var (newPages, integrated) = await IntegrateAsync(context, results, ct);
            context.IntegratedResult = integrated;
            
            yield return new ResearchStep
            {
                Iteration = iteration,
                Phase = ResearchPhase.Integrate,
                Summary = $"Integrated {newPages} new pages into synthesized context",
                Duration = stopwatch.Elapsed - integrateStart,
                PagesIntegrated = newPages,
                CurrentContext = BuildContext(context, stopwatch.Elapsed, iteration)
            };

            // === REFLECT PHASE (Two-Step) ===
            var reflectStart = stopwatch.Elapsed;
            var reflection = await ReflectAsync(context, ct);
            
            yield return new ResearchStep
            {
                Iteration = iteration,
                Phase = ResearchPhase.Reflect,
                Summary = reflection.IsSufficient 
                    ? "Context sufficient" 
                    : $"Needs more info: {reflection.GapAnalysis ?? "continuing search"}",
                Duration = stopwatch.Elapsed - reflectStart,
                ShouldContinue = !reflection.IsSufficient,
                CurrentContext = BuildContext(context, stopwatch.Elapsed, iteration)
            };

            if (reflection.IsSufficient)
            {
                _logger.LogInformation("Deep Research complete after {Iter} iterations (reflection: sufficient)", iteration);
                yield break;
            }

            // Update query with follow-ups for next iteration
            if (reflection.FollowUpQueries?.Count > 0)
            {
                context.FollowUpQueries = reflection.FollowUpQueries;
                _logger.LogDebug("Follow-up queries: {Queries}", string.Join(", ", reflection.FollowUpQueries));
            }
        }

        _logger.LogInformation("Deep Research reached max iterations ({Max}) with {Pages} pages, {Tokens} tokens",
            _options.MaxIterations, context.Pages.Count, context.TotalTokens);
    }

    private async Task<DeepResearchPlan> PlanAsync(
        DeepResearchContext ctx, 
        IReadOnlyList<MemoryAbstract> abstracts,
        int iteration,
        CancellationToken ct)
    {
        var messages = new List<LlmMessage>
        {
            new(LlmRole.System, DeepResearchPrompts.DeepPlanSystemPrompt),
            new(LlmRole.User, DeepResearchPrompts.BuildDeepPlanPrompt(
                ctx.Query.Query, 
                abstracts, 
                ctx.IntegratedResult.Content.Length > 0 ? ctx.IntegratedResult : null,
                iteration))
        };

        var response = await _llm.CompleteAsync(messages, 
            new LlmOptions { Temperature = 0.3f, MaxTokens = 800 }, ct);

        _logger.LogDebug("Plan LLM response:\n{Response}", response.Content);
        
        var plan = DeepResearchPrompts.ParsePlanResponse(response.Content);
        
        _logger.LogInformation("Parsed plan - Tools: [{Tools}], KeywordQueries: {KwCount} [{KwQueries}], VectorQueries: {VecCount} [{VecQueries}]",
            string.Join(", ", plan.Tools),
            plan.KeywordQueries.Count,
            string.Join(", ", plan.KeywordQueries.Take(3)),
            plan.VectorQueries.Count,
            string.Join(", ", plan.VectorQueries.Take(3)));
        
        // Apply limits
        return plan with
        {
            KeywordQueries = plan.KeywordQueries.Take(_options.MaxKeywordQueries).ToList(),
            VectorQueries = plan.VectorQueries.Take(_options.MaxVectorQueries).ToList()
        };
    }

    private async Task<IReadOnlyList<RetrievalResult>> SearchAsync(
        DeepResearchContext ctx, 
        DeepResearchPlan plan, 
        CancellationToken ct)
    {
        var allHits = new List<RetrievalResult>();
        var tasks = new List<Task<IReadOnlyList<RetrievalResult>>>();

        var baseQuery = new RetrievalQuery
        {
            OwnerId = ctx.Query.OwnerId,
            Query = ctx.Query.Query,
            MaxResults = _options.TopKPerQuery,
            MinScore = _options.MinRelevanceScore,
            ExcludePageIds = ctx.RetrievedPageIds
        };

        // Execute ALL keyword queries
        var keywordToolEnabled = plan.Tools.Contains("keyword");
        _logger.LogDebug("Keyword search: tools.Contains('keyword')={Enabled}, KeywordQueries.Count={Count}", 
            keywordToolEnabled, plan.KeywordQueries.Count);
        
        if (keywordToolEnabled && plan.KeywordQueries.Count > 0)
        {
            foreach (var kw in plan.KeywordQueries)
            {
                _logger.LogDebug("Adding keyword query: {Query}", kw);
                tasks.Add(ExecuteKeywordQueryAsync(baseQuery with { Query = kw }, ct));
            }
        }

        // Execute ALL vector queries
        var vectorToolEnabled = plan.Tools.Contains("vector");
        _logger.LogDebug("Vector search: tools.Contains('vector')={Enabled}, VectorQueries.Count={Count}", 
            vectorToolEnabled, plan.VectorQueries.Count);
        
        if (vectorToolEnabled && plan.VectorQueries.Count > 0)
        {
            foreach (var vq in plan.VectorQueries)
            {
                _logger.LogDebug("Adding vector query: {Query}", vq);
                tasks.Add(ExecuteVectorQueryAsync(baseQuery with { Query = vq }, ct));
            }
        }

        // Execute page index lookups
        if (plan.Tools.Contains("page_index") && plan.PageIndices.Count > 0)
        {
            tasks.Add(_pageIndexRetriever.RetrieveAsync(baseQuery, ct));
        }

        // Default fallback if no queries
        if (tasks.Count == 0)
        {
            tasks.Add(ExecuteKeywordQueryAsync(baseQuery, ct));
            tasks.Add(ExecuteVectorQueryAsync(baseQuery, ct));
        }

        var results = await Task.WhenAll(tasks);
        
        // Aggregate and deduplicate
        var scoreByPage = new Dictionary<Guid, (float Score, RetrievalResult Result)>();
        foreach (var resultSet in results)
        {
            foreach (var r in resultSet)
            {
                if (!scoreByPage.TryGetValue(r.PageId, out var existing) || existing.Score < r.Score)
                {
                    scoreByPage[r.PageId] = (r.Score, r);
                }
            }
        }

        return scoreByPage.Values
            .OrderByDescending(x => x.Score)
            .Take(_options.MaxHitsPerIteration)
            .Select(x => x.Result)
            .ToList();
    }

    private async Task<IReadOnlyList<RetrievalResult>> ExecuteKeywordQueryAsync(
        RetrievalQuery query, CancellationToken ct)
    {
        try
        {
            return await _keywordRetriever.RetrieveAsync(query, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Keyword query failed: {Query}", query.Query);
            return Array.Empty<RetrievalResult>();
        }
    }

    private async Task<IReadOnlyList<RetrievalResult>> ExecuteVectorQueryAsync(
        RetrievalQuery query, CancellationToken ct)
    {
        try
        {
            var embedding = await _embedding.EmbedAsync(query.Query, ct);
            return await _vectorRetriever.RetrieveAsync(query with { QueryEmbedding = embedding }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vector query failed: {Query}", query.Query);
            return Array.Empty<RetrievalResult>();
        }
    }

    private async Task<(int NewPages, IntegratedResult Result)> IntegrateAsync(
        DeepResearchContext ctx,
        IReadOnlyList<RetrievalResult> results,
        CancellationToken ct)
    {
        // Get new pages that weren't already retrieved
        var newIds = results
            .Where(r => !ctx.RetrievedPageIds.Contains(r.PageId))
            .Select(r => r.PageId)
            .ToList();

        if (newIds.Count == 0)
        {
            return (0, ctx.IntegratedResult);
        }

        var pages = await _store.GetPagesAsync(newIds, ct);
        var newPages = new List<RetrievedPage>();

        foreach (var page in pages)
        {
            if (ctx.TotalTokens + page.TokenCount > _options.MaxContextTokens) break;

            var result = results.First(r => r.PageId == page.Id);
            var retrievedPage = new RetrievedPage
            {
                PageId = page.Id,
                Content = page.Content,
                TokenCount = page.TokenCount,
                RelevanceScore = result.Score,
                RetrievedBy = result.RetrieverName,
                CreatedAt = page.CreatedAt
            };
            
            newPages.Add(retrievedPage);
            ctx.Pages.Add(retrievedPage);
            ctx.RetrievedPageIds.Add(page.Id);
            ctx.TotalTokens += page.TokenCount;
        }

        if (newPages.Count == 0)
        {
            return (0, ctx.IntegratedResult);
        }

        // LLM Integration: synthesize new pages with existing context
        var messages = new List<LlmMessage>
        {
            new(LlmRole.System, DeepResearchPrompts.IntegrationSystemPrompt),
            new(LlmRole.User, DeepResearchPrompts.BuildIntegrationPrompt(
                ctx.Query.Query,
                newPages,
                ctx.IntegratedResult.Content.Length > 0 ? ctx.IntegratedResult : null))
        };

        var response = await _llm.CompleteAsync(messages,
            new LlmOptions { Temperature = 0.3f, MaxTokens = 2000 }, ct);

        var integrated = DeepResearchPrompts.ParseIntegrationResponse(response.Content);
        
        return (newPages.Count, integrated);
    }

    private async Task<ReflectionResult> ReflectAsync(DeepResearchContext ctx, CancellationToken ct)
    {
        // Safety checks
        if (ctx.TotalTokens >= _options.MaxContextTokens * 0.9)
        {
            _logger.LogDebug("Token limit reached, marking as sufficient");
            return ReflectionResult.Sufficient;
        }
        
        if (ctx.IntegratedResult.Content.Length == 0)
        {
            return ReflectionResult.NeedsMore(
                new[] { ctx.Query.Query },
                "No context gathered yet");
        }

        // Step 1: Check if sufficient
        var checkMessages = new List<LlmMessage>
        {
            new(LlmRole.System, DeepResearchPrompts.ReflectCheckSystemPrompt),
            new(LlmRole.User, DeepResearchPrompts.BuildReflectCheckPrompt(ctx.Query.Query, ctx.IntegratedResult))
        };

        var checkResponse = await _llm.CompleteAsync(checkMessages,
            new LlmOptions { Temperature = 0.1f, MaxTokens = 50 }, ct);

        var isSufficient = DeepResearchPrompts.ParseReflectCheckResponse(checkResponse.Content);
        
        if (isSufficient)
        {
            return ReflectionResult.Sufficient;
        }

        // Step 2: Generate follow-up queries
        var followUpMessages = new List<LlmMessage>
        {
            new(LlmRole.System, DeepResearchPrompts.ReflectFollowUpSystemPrompt),
            new(LlmRole.User, DeepResearchPrompts.BuildReflectFollowUpPrompt(ctx.Query.Query, ctx.IntegratedResult))
        };

        var followUpResponse = await _llm.CompleteAsync(followUpMessages,
            new LlmOptions { Temperature = 0.3f, MaxTokens = 300 }, ct);

        var (gapAnalysis, followUps) = DeepResearchPrompts.ParseReflectFollowUpResponse(followUpResponse.Content);
        
        return ReflectionResult.NeedsMore(
            followUps.Count > 0 ? followUps : new[] { ctx.Query.Query },
            gapAnalysis);
    }

    private ResearchStep CreateFinalStep(DeepResearchContext ctx, TimeSpan duration, int iteration)
    {
        return new ResearchStep
        {
            Iteration = iteration,
            Phase = ResearchPhase.Reflect,
            Summary = "Research complete",
            Duration = TimeSpan.Zero,
            ShouldContinue = false,
            CurrentContext = BuildContext(ctx, duration, iteration)
        };
    }

    private MemoryContext BuildContext(DeepResearchContext ctx, TimeSpan duration, int iteration)
    {
        return new MemoryContext
        {
            Pages = ctx.Pages.OrderByDescending(p => p.RelevanceScore).ToList(),
            TotalTokens = ctx.TotalTokens,
            IterationsPerformed = iteration,
            Duration = duration
        };
    }

    private class DeepResearchContext
    {
        public required ResearchQuery Query { get; init; }
        public required HashSet<Guid> RetrievedPageIds { get; init; }
        public required List<RetrievedPage> Pages { get; init; }
        public required IntegratedResult IntegratedResult { get; set; }
        public int TotalTokens { get; set; }
        public IReadOnlyList<string>? FollowUpQueries { get; set; }
    }
}
