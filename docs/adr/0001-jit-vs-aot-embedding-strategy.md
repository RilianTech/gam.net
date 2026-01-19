# ADR-0001: Implementing GAM Deep Research (JIT Memory Optimization)

**Status:** Accepted  
**Date:** 2025-01-19  
**Authors:** @dmaman  
**References:** [GAM Paper (arXiv:2511.18423)](https://arxiv.org/abs/2511.18423)

## Context

The General Agentic Memory (GAM) paper's key innovation is **Just-In-Time (JIT) Memory Optimization** - described as:

> "Unlike conventional Ahead-of-Time (AOT) systems, GAM performs intensive Memory Deep Research at runtime, dynamically retrieving and synthesizing high-utility context to meet real-time agent needs."

After reviewing the original Python implementation, we identified that **JIT does not primarily refer to when embeddings are computed** - it refers to the **intensive LLM-driven research process performed at query time**.

## Problem Statement

Our .NET SDK has excellent production infrastructure:
- PostgreSQL with pgvector for persistent, scalable storage
- Multi-tenant support with `OwnerId` scoping
- Multiple BM25 backends (ParadeDB, pg_textsearch, native FTS)
- Pre-computed embeddings for low-latency queries

**However, we missed the core GAM intelligence:**

| GAM Core Feature | Python (Original) | .NET SDK (Current) | Impact of Gap |
|------------------|-------------------|-------------------|---------------|
| **Multi-query planning** | LLM generates `keyword_collection[]` + `vector_queries[]` | Single `SearchQuery` string | Lower recall - miss relevant pages |
| **LLM Integration** | LLM synthesizes hits into coherent `Result.content` | Raw page concatenation | Lower quality context |
| **Two-step reflection** | InfoCheck → GenerateRequests → iterate | Simple continue/stop | Stop too early or search blindly |
| **Query refinement** | Reflection generates targeted follow-up queries | Same query repeated | Less adaptive research |

**In summary:**
```
Python GAM = Simple Storage + Sophisticated Research (the GAM innovation)
.NET SDK   = Sophisticated Storage + Simple Research (missed the core value)
```

## Decision

Implement the full **GAM Deep Research** pipeline as a core tenant of the GAM.NET implementation. This is not optional - it is the defining feature of GAM.

Our production-ready storage (PostgreSQL, multi-tenant, pre-computed embeddings) is an **enhancement** to GAM, not a replacement for the research intelligence.

## Detailed Design

### Architecture Overview

```
                            GAM Deep Research Loop
                            ═════════════════════
                                     │
                                     ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  1. PLANNING PHASE                                                       │
│     ┌──────────────────────────────────────────────────────────────┐    │
│     │  Input: Query + Memory Abstracts                              │    │
│     │  LLM generates SearchPlan:                                    │    │
│     │    • info_needs: ["sub-question 1", "sub-question 2"]        │    │
│     │    • tools: ["keyword", "vector", "page_index"]              │    │
│     │    • keyword_queries: ["entity1", "function_name", "config"] │    │
│     │    • vector_queries: ["How does X work?", "Why is Y used?"]  │    │
│     │    • page_indices: [0, 3, 7]                                 │    │
│     └──────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────┘
                                     │
                                     ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  2. SEARCH PHASE                                                         │
│     ┌──────────────────────────────────────────────────────────────┐    │
│     │  Execute retrievers based on plan:                            │    │
│     │    • Keyword: Run ALL keyword_queries against BM25            │    │
│     │    • Vector: Run ALL vector_queries against pgvector          │    │
│     │    • PageIndex: Fetch specific pages by index                 │    │
│     │  Aggregate scores across queries, deduplicate by page_id      │    │
│     └──────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────┘
                                     │
                                     ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  3. INTEGRATION PHASE (NEW - Currently Missing)                          │
│     ┌──────────────────────────────────────────────────────────────┐    │
│     │  Input: Search hits + Previous result + Original question     │    │
│     │  LLM synthesizes coherent context:                            │    │
│     │    • Combines evidence from multiple sources                  │    │
│     │    • Resolves contradictions                                  │    │
│     │    • Builds narrative that answers the question               │    │
│     │  Output: IntegratedResult { content, sources }                │    │
│     └──────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────┘
                                     │
                                     ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  4. REFLECTION PHASE (NEW - Two-Step)                                    │
│     ┌──────────────────────────────────────────────────────────────┐    │
│     │  Step 1: INFO CHECK                                           │    │
│     │    LLM evaluates: "Is this sufficient to answer the query?"   │    │
│     │    Output: { enough: true/false }                             │    │
│     │                                                               │    │
│     │  Step 2: GENERATE FOLLOW-UP (if not enough)                   │    │
│     │    LLM generates: "What specific information is still needed?"│    │
│     │    Output: { new_requests: ["follow-up 1", "follow-up 2"] }   │    │
│     └──────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────┘
                                     │
                          ┌──────────┴──────────┐
                          │                     │
                     enough=true           enough=false
                          │                     │
                          ▼                     ▼
                   ┌──────────┐         ┌──────────────┐
                   │  RETURN  │         │ Loop with    │
                   │  Result  │         │ new_requests │
                   └──────────┘         └──────────────┘
```

### Component Details

#### 1. Multi-Query Search Plan

**Current Implementation:**
```csharp
public class ResearchPlan
{
    public string Strategy { get; set; }
    public string SearchQuery { get; set; }  // Single query!
    public bool UseKeywordSearch { get; set; }
    public bool UseVectorSearch { get; set; }
    // ...
}
```

**New Implementation:**
```csharp
public class DeepResearchPlan
{
    /// <summary>Sub-questions that need to be answered</summary>
    public required IReadOnlyList<string> InfoNeeds { get; init; }
    
    /// <summary>Which retrieval tools to use</summary>
    public required IReadOnlyList<string> Tools { get; init; }
    
    /// <summary>Multiple keyword queries for BM25 search</summary>
    public required IReadOnlyList<string> KeywordQueries { get; init; }
    
    /// <summary>Multiple semantic queries for vector search</summary>
    public required IReadOnlyList<string> VectorQueries { get; init; }
    
    /// <summary>Specific page indices to retrieve directly</summary>
    public required IReadOnlyList<int> PageIndices { get; init; }
}
```

**Planning Prompt (from Python):**
```
You are the PlanningAgent. Generate a retrieval plan for the QUESTION using MEMORY.

QUESTION: {request}

MEMORY:
{memory_abstracts}

PROCEDURE:
1. Identify what information is needed to answer the QUESTION
2. Break into concrete "info needs" - specific sub-questions
3. For each info need, decide which tools help:
   - "keyword": exact entities, function names, specific terms
   - "vector": conceptual/semantic queries, how/why questions  
   - "page_index": when MEMORY clearly points to relevant pages
4. Generate MULTIPLE queries per tool for better coverage

OUTPUT JSON:
{
    "info_needs": ["What is X?", "How does Y relate to Z?"],
    "tools": ["keyword", "vector"],
    "keyword_queries": ["EntityName", "function_name", "config_key"],
    "vector_queries": ["How does the system handle X?", "What is the purpose of Y?"],
    "page_indices": [0, 3]
}
```

#### 2. LLM Integration Phase

**Current Implementation:** Direct page content concatenation

**New Implementation:**
```csharp
public class IntegratedResult
{
    /// <summary>LLM-synthesized coherent context</summary>
    public required string Content { get; init; }
    
    /// <summary>Page IDs that contributed to this result</summary>
    public required IReadOnlyList<string> Sources { get; init; }
}

public interface IResultIntegrator
{
    Task<IntegratedResult> IntegrateAsync(
        IReadOnlyList<SearchHit> hits,
        IntegratedResult? previousResult,
        string originalQuestion,
        CancellationToken ct = default);
}
```

**Integration Prompt (from Python):**
```
QUESTION: {question}

NEW EVIDENCE:
{evidence_list}

EXISTING CONTEXT:
{previous_result}

TASK:
Integrate NEW EVIDENCE with EXISTING CONTEXT to build comprehensive information 
about the QUESTION. Synthesize a coherent summary that:
- Combines relevant information from multiple sources
- Resolves any contradictions
- Focuses on what's needed to answer the QUESTION
- Cites sources by page ID

OUTPUT JSON:
{
    "content": "Synthesized context that answers the question...",
    "sources": ["0", "2", "5"]
}
```

#### 3. Two-Step Reflection

**Current Implementation:** Single LLM call returning `CONTINUE` or `SUFFICIENT`

**New Implementation:**
```csharp
public class ReflectionResult
{
    /// <summary>Is the current information sufficient?</summary>
    public required bool IsSufficient { get; init; }
    
    /// <summary>If not sufficient, what follow-up queries would help?</summary>
    public IReadOnlyList<string>? FollowUpQueries { get; init; }
    
    /// <summary>Explanation of what's missing (for debugging)</summary>
    public string? GapAnalysis { get; init; }
}

public interface IResearchReflector
{
    Task<ReflectionResult> ReflectAsync(
        string originalRequest,
        IntegratedResult currentResult,
        CancellationToken ct = default);
}
```

**Step 1 - Info Check Prompt:**
```
ORIGINAL REQUEST: {request}

CURRENT INFORMATION:
{result_content}

Evaluate whether this information is SUFFICIENT to fully answer the request.
Consider:
- Are there unanswered aspects of the question?
- Are there gaps in the explanation?
- Is critical information missing?

OUTPUT JSON:
{ "enough": true/false }
```

**Step 2 - Generate Follow-up Prompt (if not enough):**
```
ORIGINAL REQUEST: {request}

CURRENT INFORMATION:
{result_content}

The information is NOT sufficient. Identify what's missing and generate 
specific follow-up queries that would fill the gaps.

OUTPUT JSON:
{
    "gap_analysis": "The current information covers X but is missing Y and Z...",
    "new_requests": ["specific query 1", "specific query 2"]
}
```

#### 4. Updated Research Agent

```csharp
public class DeepResearchAgent : IResearchAgent
{
    private readonly ILlmProvider _llm;
    private readonly IMemoryStore _memoryStore;
    private readonly IKeywordRetriever _keywordRetriever;
    private readonly IVectorRetriever _vectorRetriever;
    private readonly IPageIndexRetriever _pageIndexRetriever;
    private readonly IResultIntegrator _integrator;
    private readonly IResearchReflector _reflector;
    private readonly GamOptions _options;

    public async Task<ResearchOutput> ResearchAsync(
        string ownerId,
        string query,
        CancellationToken ct = default)
    {
        var result = new IntegratedResult { Content = "", Sources = new List<string>() };
        var iterations = new List<ResearchIteration>();
        var currentQuery = query;
        
        for (int step = 0; step < _options.MaxResearchIterations; step++)
        {
            // 1. PLANNING - Generate multi-query plan
            var abstracts = await _memoryStore.GetAbstractsAsync(ownerId, ct);
            var plan = await PlanAsync(currentQuery, abstracts, ct);
            
            // 2. SEARCH - Execute all queries across retrievers
            var hits = await SearchAsync(ownerId, plan, ct);
            
            // 3. INTEGRATE - LLM synthesizes hits into coherent context
            result = await _integrator.IntegrateAsync(hits, result, query, ct);
            
            // 4. REFLECT - Two-step: check sufficiency, generate follow-ups
            var reflection = await _reflector.ReflectAsync(query, result, ct);
            
            // Record iteration for debugging
            iterations.Add(new ResearchIteration
            {
                Step = step,
                Query = currentQuery,
                Plan = plan,
                HitCount = hits.Count,
                ResultLength = result.Content.Length,
                Reflection = reflection
            });
            
            if (reflection.IsSufficient)
                break;
            
            // Refine query for next iteration
            currentQuery = reflection.FollowUpQueries?.Any() == true
                ? string.Join(" ", reflection.FollowUpQueries)
                : query;
        }
        
        return new ResearchOutput
        {
            Context = result.Content,
            Sources = result.Sources,
            Iterations = iterations
        };
    }
    
    private async Task<IReadOnlyList<SearchHit>> SearchAsync(
        string ownerId,
        DeepResearchPlan plan,
        CancellationToken ct)
    {
        var allHits = new List<SearchHit>();
        
        // Execute keyword queries (ALL of them)
        if (plan.Tools.Contains("keyword") && plan.KeywordQueries.Any())
        {
            foreach (var kw in plan.KeywordQueries)
            {
                var hits = await _keywordRetriever.RetrieveAsync(
                    new RetrievalQuery { OwnerId = ownerId, Query = kw }, ct);
                allHits.AddRange(hits.Select(h => h with { Source = "keyword" }));
            }
        }
        
        // Execute vector queries (ALL of them)
        if (plan.Tools.Contains("vector") && plan.VectorQueries.Any())
        {
            foreach (var vq in plan.VectorQueries)
            {
                var embedding = await _embedding.EmbedAsync(vq, ct);
                var hits = await _vectorRetriever.RetrieveAsync(
                    new RetrievalQuery { OwnerId = ownerId, Query = vq, QueryEmbedding = embedding }, ct);
                allHits.AddRange(hits.Select(h => h with { Source = "vector" }));
            }
        }
        
        // Fetch specific pages
        if (plan.Tools.Contains("page_index") && plan.PageIndices.Any())
        {
            var hits = await _pageIndexRetriever.RetrieveByIndicesAsync(
                ownerId, plan.PageIndices, ct);
            allHits.AddRange(hits.Select(h => h with { Source = "page_index" }));
        }
        
        // Deduplicate by PageId, keeping highest score
        return allHits
            .GroupBy(h => h.PageId)
            .Select(g => g.OrderByDescending(h => h.Score).First())
            .OrderByDescending(h => h.Score)
            .Take(_options.MaxHitsPerIteration)
            .ToList();
    }
}
```

## Implementation Plan

### Phase 1: Multi-Query Planning ✅ COMPLETE
- [x] Create `DeepResearchPlan` model with multiple query lists
  - **Implemented:** `src/Gam.Core/Models/DeepResearch.cs`
- [x] Update `IPromptProvider` with new planning prompt
  - **Implemented:** `src/Gam.Core/Prompts/DeepResearchPrompts.cs` (standalone, not via IPromptProvider)
- [x] Implement JSON schema parsing for plan response
  - **Implemented:** `DeepResearchPrompts.ParsePlanResponse()` with fallback parsing
- [x] Update `ResearchAgent.PlanAsync()` to generate multi-query plans
  - **Implemented:** `DeepResearchAgent.PlanAsync()` in `src/Gam.Core/Agents/DeepResearchAgent.cs`
- [ ] Add unit tests for plan generation

**Effort:** 4-5 hours

### Phase 2: Multi-Query Search Execution ✅ COMPLETE
- [x] Update `SearchAsync()` to execute ALL queries in plan
  - **Implemented:** `DeepResearchAgent.SearchAsync()` executes all keyword + vector queries
- [x] Implement score aggregation across queries (same page from multiple queries)
  - **Implemented:** Keeps highest score per page via `Dictionary<Guid, (float Score, RetrievalResult)>`
- [x] Add deduplication logic
  - **Implemented:** Deduplicates by PageId before returning
- [ ] Update retrievers to support batch queries efficiently (optional optimization)
- [ ] Add integration tests

**Effort:** 3-4 hours

### Phase 3: LLM Integration Phase ✅ COMPLETE
- [x] Create `IResultIntegrator` interface
  - **Note:** Implemented inline in `DeepResearchAgent` rather than separate interface
- [x] Create `IntegratedResult` model
  - **Implemented:** `src/Gam.Core/Models/DeepResearch.cs`
- [x] Implement `LlmResultIntegrator` with integration prompt
  - **Implemented:** `DeepResearchAgent.IntegrateAsync()` with `DeepResearchPrompts.IntegrationSystemPrompt`
- [x] Wire into research loop between Search and Reflect
  - **Implemented:** In main `ResearchStreamAsync()` loop
- [ ] Add unit tests

**Effort:** 3-4 hours

### Phase 4: Two-Step Reflection ✅ COMPLETE
- [x] Create `ReflectionResult` model with `FollowUpQueries`
  - **Implemented:** `src/Gam.Core/Models/DeepResearch.cs`
- [x] Update `IResearchReflector` interface
  - **Note:** Implemented inline in `DeepResearchAgent` rather than separate interface
- [x] Implement two-step reflection (InfoCheck → GenerateRequests)
  - **Implemented:** `DeepResearchAgent.ReflectAsync()` with two LLM calls
- [x] Update research loop to use follow-up queries
  - **Implemented:** `context.FollowUpQueries` stored for next iteration
- [ ] Add unit tests

**Effort:** 3-4 hours

### Phase 5: Integration & Testing 🔄 IN PROGRESS
- [x] End-to-end integration testing setup
  - **Implemented:** `tests/Gam.Benchmarks/` with `BenchmarkRunner` and sample dataset
- [ ] Update samples/demos
- [x] Performance benchmarking (LLM calls per query)
  - **Implemented:** Benchmark framework tracks iterations, pages, tokens, duration
- [ ] Documentation updates
- [x] DI wiring
  - **Implemented:** `AddGamCoreWithDeepResearch()` and `UseDeepResearch` config option

**Effort:** 3-4 hours

### Total Estimated Effort: 16-21 hours

### Implementation Status Summary

| Component | ADR Spec | Implementation | File |
|-----------|----------|----------------|------|
| `DeepResearchPlan` | ✅ | ✅ Matches spec | `Models/DeepResearch.cs` |
| `IntegratedResult` | ✅ | ✅ Matches spec | `Models/DeepResearch.cs` |
| `ReflectionResult` | ✅ | ✅ Matches spec | `Models/DeepResearch.cs` |
| `IResultIntegrator` | Interface | Inline in agent | `Agents/DeepResearchAgent.cs` |
| `IResearchReflector` | Interface | Inline in agent | `Agents/DeepResearchAgent.cs` |
| Planning prompt | JSON output | ✅ JSON with fallback | `Prompts/DeepResearchPrompts.cs` |
| Integration prompt | JSON output | ✅ JSON with fallback | `Prompts/DeepResearchPrompts.cs` |
| Reflection prompts | Two-step | ✅ InfoCheck + FollowUp | `Prompts/DeepResearchPrompts.cs` |
| Feature flag | `UseDeepResearch` | ✅ Config option | `Configuration/GamOptions.cs` |

### Deviations from ADR

1. **No separate `IResultIntegrator`/`IResearchReflector` interfaces** - Implemented inline in `DeepResearchAgent` for simplicity. Can be extracted if needed for testing/mocking.

2. **Prompts in separate file** - Created `DeepResearchPrompts.cs` instead of extending `IPromptProvider`. This keeps Deep Research prompts self-contained.

## Configuration

```csharp
public class GamOptions
{
    /// <summary>Maximum iterations of the Plan→Search→Integrate→Reflect loop</summary>
    public int MaxResearchIterations { get; set; } = 3;
    
    /// <summary>Maximum hits to process per iteration</summary>
    public int MaxHitsPerIteration { get; set; } = 10;
    
    /// <summary>Maximum keyword queries per plan</summary>
    public int MaxKeywordQueries { get; set; } = 5;
    
    /// <summary>Maximum vector queries per plan</summary>
    public int MaxVectorQueries { get; set; } = 5;
    
    /// <summary>Top-K results per individual query</summary>
    public int TopKPerQuery { get; set; } = 5;
}
```

## Consequences

### Positive
- **Full GAM implementation** - Delivers the core value proposition of the paper
- **Better retrieval quality** - Multiple queries provide better coverage
- **Higher quality context** - LLM synthesis vs raw concatenation
- **Adaptive research** - Follow-up queries fill gaps automatically
- **Keeps production infrastructure** - PostgreSQL, multi-tenant, pre-computed embeddings unchanged

### Negative
- **More LLM calls per query** - Planning + Integration + 2x Reflection per iteration
- **Higher latency** - Multiple LLM round-trips
- **Higher cost** - More tokens processed per research query

### Cost Analysis

| Phase | LLM Calls | Estimated Tokens |
|-------|-----------|------------------|
| Planning | 1 | ~1,500 (abstracts + prompt) |
| Integration | 1 | ~2,000 (hits + previous + prompt) |
| Reflection Step 1 | 1 | ~1,500 (result + prompt) |
| Reflection Step 2 | 0-1 | ~1,000 (if not sufficient) |
| **Per Iteration** | 3-4 | ~5,000-6,000 |
| **3 Iterations** | 9-12 | ~15,000-18,000 |

For comparison, current implementation uses ~2-3 LLM calls total.

**Mitigation:** The `MaxResearchIterations` setting allows tuning cost vs quality.

## Migration Path

1. **Implement behind feature flag** - `GamOptions.UseDeepResearch = true`
2. **Default to new behavior** - After validation
3. **Deprecate simple research** - Mark old path as legacy

## Open Questions

1. **Parallel query execution** - Should we run keyword and vector queries in parallel?
   - **Answer:** YES - Implemented via `Task.WhenAll()` in `SearchAsync()`. All queries run in parallel.

2. **Caching** - Should we cache integration results for repeated similar queries?
   - **Status:** Not implemented yet. Consider for future optimization.

3. **Streaming** - Should integration/reflection stream results for better UX?
   - **Status:** `ResearchStreamAsync()` yields `ResearchStep` after each phase, enabling real-time progress updates. LLM responses themselves are not streamed yet.

## References

- [GAM Paper: General Agentic Memory](https://arxiv.org/abs/2511.18423)
- [Original Python Implementation](https://github.com/Elfsong/general-agentic-memory)
