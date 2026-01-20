# ADR-0002: Memory Enhancements Inspired by EverMemOS and AutoMem

**Status:** Implemented  
**Date:** 2025-01-19 (proposed), 2025-01-20 (implemented)  
**Authors:** @dmaman  
**References:** 
- [Memory System Comparison](../research/memory-system-comparison.md)
- [EverMemOS](https://github.com/EverMemAI/EverMemOS) - 92.3% LoCoMo
- [AutoMem](https://github.com/topoteretes/automem) - 90.53% LoCoMo
- [ADR-0001: GAM Deep Research](./0001-jit-vs-aot-embedding-strategy.md)

## Implementation Summary

This ADR has been implemented and validated. Key results:

| Metric | ADR-1 Baseline | ADR-2 Result | Change |
|--------|---------------|--------------|--------|
| **Fact Recall** | 100% | 97.9% | -2.1% (acceptable) |
| **Avg Query Duration** | 14,629ms | 17,532ms | +20% (acceptable) |
| **Relationships Created** | 0 | 46 (for 8 memories) | New capability |
| **Relationship Types** | N/A | RelatesTo, PrecededBy, SimilarTo | 3 active types |

### What Was Implemented

1. **Memory Metadata** (Phase 1-3)
   - `MemoryType` enum (Decision, Preference, Fact, Insight, Task, Context, Conversation)
   - `importance` field (0.0-1.0) on pages
   - `tags` array on abstracts with entity/keyword prefixes
   - LLM extracts metadata during memorization via JSON prompt

2. **Memory Relationships** (Phase 4-5, enhanced with AutoMem patterns)
   - `RelatesTo` - tag overlap (≥2 shared tags, boosted for entity tags)
   - `PrecededBy` - temporal linking (each memory links to 3 most recent)
   - `SimilarTo` - semantic similarity (≥0.8 cosine, created by background service)
   - Relationship expansion during Deep Research retrieval

### Key Design Decision: AutoMem-Inspired Relationships

After analyzing AutoMem's relationship system, we adopted:
- **Absolute tag overlap** instead of Jaccard similarity (creates more relationships)
- **Temporal relationships** created during ingestion (not just background)
- **Semantic similarity** discovered by background service using pgvector
- **Multiple relationship types** for different discovery methods

This increased relationships from 2 to 46 for the same 8-memory dataset.

## Context

GAM.NET implements the General Agentic Memory framework with a focus on simplicity (single PostgreSQL database) and flexibility (JIT paradigm). Two competing memory systems - EverMemOS and AutoMem - achieve higher benchmark scores on LoCoMo through more sophisticated memory structures and retrieval mechanisms.

This ADR proposes adopting select features from these systems that can enhance GAM without sacrificing its architectural simplicity.

**Important:** This ADR must be read in conjunction with ADR-0001 (GAM Deep Research). The Deep Research implementation fundamentally changes how retrieval works - from fixed scoring formulas to LLM-driven synthesis. This impacts which enhancements provide genuine value.

## Relationship to ADR-0001 (Deep Research)

ADR-0001 implements the core GAM innovation: **LLM-driven research** with Plan → Search → Integrate → Reflect loops.

This ADR focuses on **memory enrichment** - giving the LLM better signals during research.

### How Deep Research Changes the Value of Enhancements

```
BEFORE Deep Research:
  Query → Retriever → Fixed Scoring → Return Top-K
  ↑ Scoring weights and relevance formulas matter

AFTER Deep Research:  
  Query → LLM Plans → Multi-Query Search → LLM Integrates → LLM Reflects
  ↑ LLM decides relevance dynamically, not scoring formulas
```

| Enhancement | Value WITHOUT Deep Research | Value WITH Deep Research |
|-------------|----------------------------|-------------------------|
| Importance Scoring | High (affects ranking) | **High** (LLM sees importance in abstracts) |
| Memory Type | High (enables filtering) | **Medium** (LLM can filter during planning) |
| Entity Tags | High (improves keyword search) | **High** (enables targeted keyword queries) |
| Retrieval Weights | High (tunes scoring) | **Redundant** (LLM integration replaces scoring) |
| Consolidation/Decay | Medium (lifecycle mgmt) | **Low** (LLM reflection handles relevance) |
| Relationships | High (multi-hop) | **Medium** (reflection + follow-ups achieve similar) |

### Enhancements That Genuinely Enhance Deep Research

The enhancements worth implementing are those that give the **LLM better information** during planning and integration:

1. **Importance** - Shown in abstracts: "Memory marked as HIGH importance by user"
2. **Memory Type** - Enables filtered planning: "Find only Decision-type memories"  
3. **Entity Tags** - Enables precise keyword queries: `entity:tool:postgresql`

### Enhancements Deferred or Removed

- **Retrieval Weights** - Removed; Deep Research's LLM integration replaces fixed scoring
- **Consolidation** - Deferred; reflection phase already handles relevance dynamically
- **Relationships** - Deferred; follow-up queries in reflection achieve multi-hop reasoning

## Problem Statement

1. **No importance differentiation:** All memories are treated equally regardless of significance
2. ~~**No memory lifecycle:** Memories don't decay, consolidate, or evolve over time~~ (Deferred - Deep Research handles dynamically)
3. ~~**Limited retrieval signals:** Only vector similarity, BM25, and header matching~~ (Addressed by ADR-0001 multi-query planning)
4. ~~**No memory relationships:** Cannot traverse from one memory to related memories~~ (Deferred - reflection achieves similar)
5. **No entity awareness:** Cannot query by entities (people, tools, concepts)

## Decision Drivers

- **Complement Deep Research:** Enhancements should give the LLM better signals, not replace LLM judgment
- **Preserve simplicity:** Single PostgreSQL database, no additional infrastructure
- **Incremental adoption:** Features can be enabled/disabled independently
- **Backward compatibility:** Existing deployments should continue working
- **Measurable impact:** Changes should be benchmarkable

## Proposed Enhancements

### Enhancement 1: Memory Importance & Access Tracking

Add importance scoring and access tracking to enable relevance-based retrieval.

**Schema Changes:**
```sql
ALTER TABLE memory_pages ADD COLUMN importance FLOAT DEFAULT 0.5;
ALTER TABLE memory_pages ADD COLUMN access_count INTEGER DEFAULT 0;
ALTER TABLE memory_pages ADD COLUMN last_accessed_at TIMESTAMPTZ;
ALTER TABLE memory_pages ADD COLUMN relevance_score FLOAT DEFAULT 0.5;

CREATE INDEX idx_pages_relevance ON memory_pages(owner_id, relevance_score DESC);
```

**Model Changes:**
```csharp
public record MemoryPage
{
    // Existing...
    public float Importance { get; init; } = 0.5f;
    public int AccessCount { get; init; } = 0;
    public DateTime? LastAccessedAt { get; init; }
    public float RelevanceScore { get; init; } = 0.5f;
}
```

**Behavior:**
- Importance can be set explicitly or inferred by LLM during memorization
- AccessCount and LastAccessedAt updated on each retrieval
- RelevanceScore computed by consolidation (see Enhancement 4)

### Enhancement 2: Memory Type Classification

Classify memories by type for filtered retrieval.

**Schema Changes:**
```sql
ALTER TABLE memory_abstracts ADD COLUMN memory_type VARCHAR(20) DEFAULT 'conversation';

CREATE INDEX idx_abstracts_type ON memory_abstracts(owner_id, memory_type);
```

**Model Changes:**
```csharp
public enum MemoryType
{
    Conversation,  // General discussion
    Decision,      // Choice or decision made
    Preference,    // Like/dislike expressed
    Fact,          // Factual information
    Insight,       // Inferred understanding
    Task,          // Action item
    Context        // Background info
}

public record MemoryAbstract
{
    // Existing...
    public MemoryType Type { get; init; } = MemoryType.Conversation;
}
```

**Prompt Addition:**
```
Also classify this memory as one of: Conversation, Decision, Preference, Fact, Insight, Task, Context
```

### Enhancement 3: Entity Tags

Extract and store entities/keywords for tag-based retrieval.

**Schema Changes:**
```sql
ALTER TABLE memory_abstracts ADD COLUMN tags TEXT[] DEFAULT '{}';

CREATE INDEX idx_abstracts_tags ON memory_abstracts USING GIN(tags);
```

**Model Changes:**
```csharp
public record MemoryAbstract
{
    // Existing...
    public string[] Tags { get; init; } = [];
}
```

**Tag Format:**
- `entity:person:john-smith`
- `entity:tool:postgresql`
- `entity:project:gam-dotnet`
- `keyword:database`
- `keyword:performance`

**Prompt Addition:**
```
Extract key entities and topics as tags. Format: entity:<type>:<name> or keyword:<topic>
Types: person, organization, tool, project, concept, location
```

### Enhancement 4: Memory Consolidation Service (DEFERRED)

> **Status:** Deferred pending Deep Research validation
> 
> **Rationale:** With Deep Research (ADR-0001), the LLM's reflection phase dynamically evaluates 
> "is this information sufficient?" at query time. Background consolidation adds complexity without 
> clear benefit when the LLM is already making relevance judgments.
>
> **Revisit when:** Benchmarks show that pre-filtering by relevance score improves Deep Research 
> quality or reduces LLM token usage significantly.

~~Background service for memory lifecycle management.~~

### Enhancement 5: Configurable Retrieval Weights (REMOVED)

> **Status:** Removed - superseded by Deep Research
>
> **Rationale:** Deep Research replaces fixed scoring formulas with LLM-driven integration. 
> The Integration phase prompt asks the LLM to synthesize relevant information - the LLM decides 
> what's relevant based on the query context, not a weighted formula.
>
> **Before (Simple Research):**
> ```
> score = 0.3*vector + 0.25*keyword + 0.15*header + 0.1*importance + ...
> return pages.OrderByDescending(score).Take(k)
> ```
>
> **After (Deep Research):**
> ```
> LLM Integration Prompt: "Synthesize these search hits into coherent context 
> for answering the question. Focus on what's relevant."
> ```
>
> Configurable weights become meaningless when an LLM is making the relevance judgment.

~~Allow tuning of retrieval scoring components.~~

### Enhancement 4: Memory Relationships

> **Status:** Proposed (reconsidered from deferred)
>
> **Rationale:** While Deep Research's reflection can achieve multi-hop reasoning through 
> follow-up queries, explicit relationships provide value that reflection cannot:
> 
> 1. **Contradictions** - LLM may not notice memory A contradicts memory B without explicit link
> 2. **Evidence chains** - "This decision was based on these prior conversations"
> 3. **Reduced iterations** - One relationship query vs multiple research iterations
> 4. **Temporal context** - FOLLOWS relationships show conversation evolution

**Key Question: How are relationships created?**

| Approach | Pros | Cons |
|----------|------|------|
| **Manual** | Precise, user-controlled | Tedious, rarely used |
| **LLM at memorization** | Automatic, contextual | Expensive (needs to compare with existing memories) |
| **Background similarity** | Cheap, catches obvious links | Misses semantic relationships |
| **LLM at research** | Only when needed | Adds latency to queries |

**Recommended: Hybrid approach**

1. **At memorization time:** LLM extracts `relates_to_topics` from the new memory
2. **Background job:** Periodically finds memories with overlapping topics/entities and creates RELATES_TO links
3. **At research time (optional):** If retrieved memories seem contradictory, LLM can flag CONTRADICTS

**Schema (PostgreSQL - no graph DB needed):**
```sql
CREATE TABLE memory_relationships (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    source_page_id UUID REFERENCES memory_pages(id) ON DELETE CASCADE,
    target_page_id UUID REFERENCES memory_pages(id) ON DELETE CASCADE,
    relationship_type VARCHAR(50) NOT NULL,
    confidence FLOAT DEFAULT 1.0,
    created_by VARCHAR(20) DEFAULT 'system',  -- 'system', 'llm', 'user'
    created_at TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(source_page_id, target_page_id, relationship_type)
);

CREATE INDEX idx_rel_source ON memory_relationships(source_page_id);
CREATE INDEX idx_rel_target ON memory_relationships(target_page_id);
CREATE INDEX idx_rel_type ON memory_relationships(relationship_type);
```

**Relationship Types (implemented):**
| Type | Purpose | Created By | Confidence |
|------|---------|------------|------------|
| `RELATES_TO` | Tag/entity overlap | During memorization | 0.3-1.0 (based on overlap) |
| `PRECEDED_BY` | Temporal proximity | During memorization | 1.0 (certain) |
| `SIMILAR_TO` | Semantic similarity | Background service | 0.8-1.0 (cosine score) |
| `FOLLOWS` | Same conversation sequence | System | 1.0 (certain) |
| `CONTRADICTS` | Conflicting information | LLM (future) | Variable |
| `REINFORCES` | Supporting evidence | LLM (future) | Variable |

**Active types:** RelatesTo, PrecededBy, SimilarTo
**Future types:** Follows, Contradicts, Reinforces (require LLM analysis)

**Integration with Deep Research:**

After initial retrieval, expand with related memories before LLM integration:

```csharp
// In DeepResearchAgent.SearchAsync()
var directHits = await ExecuteRetrieversAsync(plan, ct);

// Expand with 1-hop relationships
var relatedPageIds = await _store.GetRelatedPageIdsAsync(
    directHits.Select(h => h.PageId),
    relationshipTypes: ["RELATES_TO", "REINFORCES"],
    maxPerSource: 2,
    ct);

var expandedHits = await _store.GetPagesByIdsAsync(relatedPageIds, ct);
return directHits.Concat(expandedHits).DistinctBy(h => h.PageId).ToList();
```

**Why PostgreSQL is sufficient:**

AutoMem uses FalkorDB for:
- 11 relationship types (we have 4)
- Multi-hop traversal (we do 1-hop, Deep Research handles the rest)
- Complex graph queries (we just need simple JOINs)

A PostgreSQL join table handles our needs without adding infrastructure.

## Implementation Plan

> **Prerequisites:**
> 1. ADR-0001 (Deep Research) must be implemented and merged
> 2. Benchmark suite must exist (LoCoMo subset or equivalent)
> 3. Baseline metrics recorded for ADR-1-only implementation

### Branch Strategy

```
main
 └── feature/adr-1-deep-research
      ├── Implement Deep Research
      ├── Add benchmark suite
      ├── Record baseline metrics
      └── Merge to main (after validation)
          │
          └── feature/adr-2-memory-enhancements
               ├── Implement enhancements (all as default, not optional)
               ├── Run same benchmarks
               ├── Compare against baseline
               └── Merge to main ONLY if benchmarks improve
```

### No Optional Features

To avoid combinatorial complexity, these enhancements will be implemented as **default 
behavior, not toggles**. Either:
- Benchmarks show improvement → Merge as the new default
- Benchmarks don't improve → Don't merge, document learnings

This keeps the codebase simple with a single code path.

### Phase 1: Schema & Model Updates (Enhancements 1-3)

**Migration 002_MemoryEnhancements.sql:**
```sql
-- Enhancement 1: Importance & Access Tracking
ALTER TABLE memory_pages ADD COLUMN IF NOT EXISTS importance FLOAT DEFAULT 0.5;
ALTER TABLE memory_pages ADD COLUMN IF NOT EXISTS access_count INTEGER DEFAULT 0;
ALTER TABLE memory_pages ADD COLUMN IF NOT EXISTS last_accessed_at TIMESTAMPTZ;

-- Enhancement 2: Memory Type
ALTER TABLE memory_abstracts ADD COLUMN IF NOT EXISTS memory_type VARCHAR(20) DEFAULT 'conversation';

-- Enhancement 3: Tags
ALTER TABLE memory_abstracts ADD COLUMN IF NOT EXISTS tags TEXT[] DEFAULT '{}';

-- Indexes
CREATE INDEX IF NOT EXISTS idx_pages_importance ON memory_pages(owner_id, importance DESC);
CREATE INDEX IF NOT EXISTS idx_abstracts_type ON memory_abstracts(owner_id, memory_type);
CREATE INDEX IF NOT EXISTS idx_abstracts_tags ON memory_abstracts USING GIN(tags);
```

**Estimated Effort:** 1 day

### Phase 2: Enhanced Memorization Prompt

Update the MemoryAgent to extract type and tags during memorization:

**Enhanced Abstract Generation Prompt:**
```
Analyze this conversation and generate a memory abstract.

CONVERSATION:
{conversation_content}

OUTPUT (JSON):
{
  "summary": "2-3 sentence overview of what was discussed",
  "headers": ["3-7 searchable keywords or phrases"],
  "type": "One of: Conversation, Decision, Preference, Fact, Insight, Task, Context",
  "importance": 0.0-1.0 (how significant is this for future reference?),
  "tags": [
    "entity:person:name",      // People mentioned
    "entity:tool:name",        // Tools, technologies, products
    "entity:project:name",     // Projects, codebases
    "entity:org:name",         // Organizations, companies
    "keyword:topic"            // Key concepts, topics
  ]
}

TYPE GUIDELINES:
- Decision: A choice was made between alternatives
- Preference: User expressed like/dislike without deciding
- Fact: Factual information was shared or learned
- Insight: A realization or understanding was reached
- Task: An action item or todo was identified
- Context: Background information, no specific action
- Conversation: General discussion (default)

IMPORTANCE GUIDELINES:
- 0.8-1.0: Critical decisions, key facts, important preferences
- 0.5-0.7: Useful context, moderate relevance
- 0.2-0.4: Minor details, low future relevance
```

**Estimated Effort:** 1 day

### Phase 3: Deep Research Integration

Update Deep Research prompts to leverage new metadata:

**Planning Prompt Enhancement:**
```
MEMORY (with metadata):
[0] (Type: Decision, Importance: 0.9, Tags: entity:tool:postgresql, keyword:database)
    Summary: Discussed database options and chose PostgreSQL for the auth service...

[1] (Type: Conversation, Importance: 0.5, Tags: entity:person:alice)
    Summary: Alice mentioned concerns about scaling...
```

**Integration Prompt Enhancement:**
```
When synthesizing context from search results:
- Prioritize HIGH importance (>0.7) memories
- For factual questions, prefer Decision and Fact types
- For opinion questions, prefer Preference types
- Note any potential contradictions between memories
```

**Estimated Effort:** 1 day

### Phase 4: Memory Relationships (Enhancement 4)

**Migration 003_MemoryRelationships.sql:**
```sql
CREATE TABLE IF NOT EXISTS memory_relationships (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    source_page_id UUID NOT NULL REFERENCES memory_pages(id) ON DELETE CASCADE,
    target_page_id UUID NOT NULL REFERENCES memory_pages(id) ON DELETE CASCADE,
    relationship_type VARCHAR(50) NOT NULL,
    confidence FLOAT DEFAULT 1.0,
    created_by VARCHAR(20) DEFAULT 'system',
    created_at TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(source_page_id, target_page_id, relationship_type)
);

CREATE INDEX IF NOT EXISTS idx_rel_source ON memory_relationships(source_page_id);
CREATE INDEX IF NOT EXISTS idx_rel_target ON memory_relationships(target_page_id);
CREATE INDEX IF NOT EXISTS idx_rel_type ON memory_relationships(relationship_type);
```

**Relationship Creation:**

1. **FOLLOWS** - Created automatically when memorizing sequential turns in same conversation
2. **RELATES_TO** - Created by background job when memories share tags/entities
3. **CONTRADICTS/REINFORCES** - Created by LLM during research integration when detected

**Estimated Effort:** 2-3 days

### Phase 5: Relationship-Aware Retrieval

Expand Deep Research search results with related memories:

```csharp
public class RelationshipExpander
{
    public async Task<IReadOnlyList<SearchHit>> ExpandWithRelatedAsync(
        IReadOnlyList<SearchHit> directHits,
        string[] relationshipTypes = ["RELATES_TO", "REINFORCES"],
        int maxPerSource = 2,
        CancellationToken ct = default)
    {
        var relatedIds = await _store.GetRelatedPageIdsAsync(
            directHits.Select(h => h.PageId),
            relationshipTypes,
            maxPerSource,
            ct);
        
        var relatedPages = await _store.GetPagesByIdsAsync(relatedIds, ct);
        var relatedHits = relatedPages.Select(p => new SearchHit 
        { 
            PageId = p.Id, 
            Score = 0.8f,  // Slightly lower than direct hits
            Source = "relationship" 
        });
        
        return directHits
            .Concat(relatedHits)
            .DistinctBy(h => h.PageId)
            .ToList();
    }
}
```

**Estimated Effort:** 1-2 days

### Total Estimated Effort

| Phase | Effort | Cumulative |
|-------|--------|------------|
| Phase 1: Schema | 1 day | 1 day |
| Phase 2: Memorization Prompt | 1 day | 2 days |
| Phase 3: Deep Research Integration | 1 day | 3 days |
| Phase 4: Relationships Schema | 2-3 days | 5-6 days |
| Phase 5: Relationship Retrieval | 1-2 days | 6-8 days |

**Total: ~1.5 weeks**

### ~~Consolidation Service~~ DEFERRED

### ~~Retrieval Weights~~ REMOVED

---

## Validation Results

### Benchmark Results (Relationship-Focused Dataset)

The benchmark uses 8 conversations with overlapping entities (John, Sarah, Marcus, Alice, PostgreSQL, Kafka) and 8 queries designed to test multi-hop retrieval.

| Metric | Target | Actual | Status |
|--------|--------|--------|--------|
| **Fact Recall** | ≥80% | 97.9% | PASS |
| **Overall Accuracy** | ≥80% | 100% | PASS |
| **Query Latency** | <1.5x baseline | ~17.5s avg | PASS |
| **Relationships Created** | >0 | 46 | PASS |

### Per-Query Results

| Query | Difficulty | Fact Recall | Duration |
|-------|------------|-------------|----------|
| PostgreSQL issues and resolutions | Medium | 83% | 16.5s |
| John's expertise and projects | Hard | 100% | 13.9s |
| PostgreSQL + Kafka integration | Hard | 100% | 18.5s |
| Who worked with Kafka | Medium | 100% | 14.1s |
| PostgreSQL journey (multi-hop) | Hard | 100% | 17.2s |
| Sarah + Marcus collaboration | Medium | 100% | 14.2s |
| Technologies requiring expertise | Easy | 100% | 15.0s |
| Monitoring for PostgreSQL + Kafka | Medium | 100% | 30.7s |

### Relationship Statistics

For 8 ingested memories:
- **Tag overlap (RelatesTo):** ~28 relationships
- **Temporal (PrecededBy):** ~18 relationships  
- **Total:** 46 relationships

Tag overlap analysis:
- `entity:person:john` appears in 7/8 memories
- `entity:person:sarah` appears in 5/8 memories
- `entity:tool:postgresql` appears in 4/8 memories

### Decision: MERGE

Results show:
- High fact recall (97.9%) maintained from ADR-1
- Relationships successfully created and used during expansion
- Latency acceptable for async memorization and research use cases

## Consequences

### Positive

- **Better LLM signals:** Importance, type, and tags give Deep Research's LLM richer context
- **Filtered planning:** LLM can request specific memory types during planning
- **Precise keyword queries:** Entity tags enable targeted retrieval (`entity:tool:postgresql`)
- **Explicit relationships:** Contradictions and supporting evidence are pre-computed, not discovered at query time
- **Reduced iterations:** Relationship expansion finds related context in one query vs multiple research iterations
- **Backward compatible:** All enhancements are opt-in
- **No new infrastructure:** PostgreSQL handles relationships without needing a graph database

### Negative

- Increased schema complexity (4 new columns, 3 new indexes, 1 new table)
- LLM prompts become more complex (type + tag + importance extraction during memorization)
- Memorization latency slightly increased (~100-200ms for additional extraction)
- Background job needed for RELATES_TO relationship creation

### Neutral

- Single PostgreSQL database preserved
- Deep Research implementation (ADR-0001) is prerequisite
- Relationship table is optional - system works without it

## What We're NOT Doing (and Why)

### Retrieval Weights (Removed)

Fixed scoring formulas become meaningless when Deep Research's LLM integration phase 
dynamically decides relevance based on the query context.

### Memory Consolidation/Decay (Deferred)

Background decay/consolidation adds complexity without clear benefit when:
1. Deep Research's reflection phase already evaluates relevance dynamically
2. TTL-based cleanup (existing) handles storage limits
3. Access tracking provides the raw data if consolidation is needed later

**Revisit when:** Memory corpus grows large enough that pre-filtering improves performance.

### Graph Database (Rejected)

AutoMem uses FalkorDB for relationships. We rejected this because:
1. We only need 4 relationship types (vs AutoMem's 11)
2. We only do 1-hop expansion (Deep Research handles deeper traversal)
3. PostgreSQL JOIN handles our query patterns efficiently
4. Avoids infrastructure complexity

### Full EverMemOS Memory Types (Rejected)

EverMemOS has Episodes, Foresights, EventLogs, Profiles - separate data structures with 
different schemas. We achieve similar benefits with:
- **Memory Type enum** instead of separate tables
- **Entity tags** for structured entity extraction
- **Deep Research synthesis** instead of pre-computed episodes
- **Relationships** instead of explicit episode→memcell links

## Open Questions

1. **Tag extraction method:** LLM-based (simpler, consistent with memorization) vs NLP library (faster, cheaper)?
   - **Recommendation:** LLM-based for consistency; extraction happens during memorization, not query time
   
2. **Type classification accuracy:** Should we validate LLM classification?
   - **Recommendation:** Trust but verify via logging; add validation if accuracy issues emerge
   
3. **Importance inference:** User-provided vs LLM-inferred?
   - **Recommendation:** Default to 0.5, allow explicit override, consider LLM inference as optional enhancement

## References

- [Memory System Comparison](../research/memory-system-comparison.md)
- [ADR-0001: GAM Deep Research](./0001-jit-vs-aot-embedding-strategy.md)
- [EverMemOS](https://github.com/EverMemAI/EverMemOS)
- [AutoMem](https://github.com/topoteretes/automem)
