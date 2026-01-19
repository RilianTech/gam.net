# Memory System Comparison: GAM vs EverMemOS vs AutoMem

**Date:** 2025-01-19  
**Purpose:** Evaluate competing memory approaches and identify enhancements for GAM.NET

## Executive Summary

| System | Benchmark | Infrastructure | Key Innovation |
|--------|-----------|----------------|----------------|
| **EverMemOS** | 92.3% LoCoMo | MongoDB + Elasticsearch + Milvus + Redis | Multi-type memory hierarchy with foresight |
| **AutoMem** | 90.53% LoCoMo | FalkorDB + Qdrant | Graph-vector hybrid with consolidation |
| **GAM** | Research-backed | PostgreSQL + pgvector | JIT memory compilation paradigm |

GAM's strength is its **simplicity and flexibility** - single database, JIT paradigm, and clean abstractions. The other systems achieve higher benchmarks through **richer memory structures** and **more sophisticated retrieval**, but at the cost of infrastructure complexity.

---

## Detailed Comparison

### 1. Memory Data Structures

#### EverMemOS - Multi-Type Hierarchy

```
MemCell (atomic unit)
  ├── EpisodeMemory (narrative summaries)
  ├── Foresight (predictive insights)
  ├── EventLog (atomic facts)
  └── ProfileMemory (user traits with evidence)
```

**Key Insight:** Different memory types serve different retrieval needs. Episodes answer "what happened?", Foresights answer "what might happen?", EventLogs answer "what specific fact?", Profiles answer "who is this person?".

#### AutoMem - Graph-Based with Relationships

```
Memory Node
  ├── 11 Relationship Types (RELATES_TO, LEADS_TO, PREFERS_OVER, etc.)
  ├── Entity Nodes (people, tools, projects, concepts)
  └── Pattern Nodes (detected behavioral patterns)
```

**Key Insight:** Explicit relationships enable multi-hop reasoning. A query about "PostgreSQL" can traverse to "boring tech preference" to "Kafka choice" through PREFERS_OVER relationships.

#### GAM - Page + Abstract Pairs

```
MemoryPage (raw content)
  └── MemoryAbstract (summary + searchable headers)
```

**Key Insight:** JIT paradigm preserves raw data without information loss. Headers serve as searchable indexes.

### 2. Retrieval Methods

| Method | EverMemOS | AutoMem | GAM |
|--------|-----------|---------|-----|
| Keyword/BM25 | Yes | Yes (15% weight) | Yes |
| Vector/Semantic | Yes | Yes (25% weight) | Yes |
| Hybrid Fusion | RRF | 9-component scoring | Page Index + iterative |
| Graph Traversal | No | Yes (25% weight) | No |
| Agentic/LLM-guided | Yes | No | Yes (Plan-Search-Integrate-Reflect) |
| Multi-hop | Via episodes | Via bridge discovery | Via iterative research |

### 3. Memory Lifecycle

| Phase | EverMemOS | AutoMem | GAM |
|-------|-----------|---------|-----|
| **Creation** | Boundary detection + LLM extraction | Auto-classification + entity extraction | Abstract generation |
| **Enrichment** | Episode/Foresight generation | Background enrichment pipeline | None |
| **Consolidation** | Profile updates | Decay + Creative + Cluster + Forget cycles | None |
| **Deletion** | Manual | Automatic based on relevance score | TTL-based cleanup |

### 4. Infrastructure Requirements

| System | Services | Complexity | Flexibility |
|--------|----------|------------|-------------|
| **EverMemOS** | MongoDB, Elasticsearch, Milvus, Redis | High | Low (requires all 4) |
| **AutoMem** | FalkorDB, Qdrant | Medium | Medium (graph+vector) |
| **GAM** | PostgreSQL + pgvector | Low | High (single service) |

---

## Potential GAM Enhancements

Based on the analysis of EverMemOS and AutoMem, here are features that could enhance GAM while preserving its simplicity:

### Tier 1: High Impact, Low Complexity

These enhancements align with GAM's philosophy and can use existing PostgreSQL infrastructure.

#### 1.1 Memory Importance Scoring

**Inspiration:** AutoMem's importance field (0.0-1.0) and decay scoring

**Proposed Implementation:**
```csharp
public record MemoryPage
{
    // Existing fields...
    
    /// <summary>
    /// User-assigned or LLM-inferred importance (0.0-1.0).
    /// Higher importance memories are prioritized in retrieval.
    /// </summary>
    public float Importance { get; init; } = 0.5f;
    
    /// <summary>
    /// Number of times this memory has been retrieved.
    /// Used for access-based relevance scoring.
    /// </summary>
    public int AccessCount { get; init; } = 0;
    
    /// <summary>
    /// Last time this memory was retrieved.
    /// Used for recency-based scoring.
    /// </summary>
    public DateTime? LastAccessedAt { get; init; }
}
```

**Retrieval Scoring:**
```csharp
float ComputeRelevanceScore(MemoryPage page, float baseScore)
{
    var recencyFactor = ComputeRecencyDecay(page.LastAccessedAt);
    var accessFactor = Math.Min(1.0f, page.AccessCount / 10.0f);
    
    return baseScore 
         * (0.5f + page.Importance)  // Importance boost
         * recencyFactor              // Time decay
         * (0.7f + 0.3f * accessFactor); // Access frequency
}
```

**Database Change:** Add 3 columns to `memory_pages` table.

---

#### 1.2 Memory Type Classification

**Inspiration:** EverMemOS's memory types, AutoMem's type field

**Proposed Implementation:**
```csharp
public enum MemoryType
{
    Conversation,  // Default - general conversation
    Decision,      // User made a choice or decision
    Preference,    // User expressed a like/dislike
    Fact,          // Factual information
    Insight,       // Inferred understanding
    Task,          // Action item or todo
    Context        // Background/situational info
}

public record MemoryAbstract
{
    // Existing fields...
    
    /// <summary>
    /// LLM-classified memory type for filtered retrieval.
    /// </summary>
    public MemoryType Type { get; init; } = MemoryType.Conversation;
}
```

**Benefits:**
- Retrieval can filter by type (e.g., "only decisions")
- Different types can have different importance defaults
- Enables type-specific retrieval strategies

**LLM Prompt Addition:**
```
Classify this memory as one of: Conversation, Decision, Preference, Fact, Insight, Task, Context
```

---

#### 1.3 Keyword/Entity Extraction

**Inspiration:** AutoMem's spaCy-based entity extraction, EverMemOS's keywords

**Proposed Implementation:**
```csharp
public record MemoryAbstract
{
    // Existing fields...
    
    /// <summary>
    /// Extracted entities and keywords for enhanced retrieval.
    /// Format: ["entity:person:john", "entity:tool:postgresql", "keyword:database"]
    /// </summary>
    public string[] Tags { get; init; } = [];
}
```

**Extraction Options:**
1. **LLM-based:** Add to abstract generation prompt (simplest)
2. **NLP-based:** Use a .NET NLP library for entity recognition
3. **Regex-based:** Pattern matching for common entities (URLs, emails, code refs)

**Benefits:**
- Enables tag-based filtering in retrieval
- Improves keyword search precision
- Supports entity-centric queries ("what did we discuss about PostgreSQL?")

---

### Tier 2: Medium Impact, Medium Complexity

These add significant capability but require more implementation effort.

#### 2.1 Memory Relationships (Lightweight Graph)

**Inspiration:** AutoMem's 11 relationship types

**Proposed Implementation:**

Instead of a full graph database, use a simple relationship table in PostgreSQL:

```sql
CREATE TABLE memory_relationships (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    source_page_id UUID NOT NULL REFERENCES memory_pages(id) ON DELETE CASCADE,
    target_page_id UUID NOT NULL REFERENCES memory_pages(id) ON DELETE CASCADE,
    relationship_type VARCHAR(50) NOT NULL,
    confidence FLOAT DEFAULT 1.0,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    
    UNIQUE(source_page_id, target_page_id, relationship_type)
);

CREATE INDEX idx_relationships_source ON memory_relationships(source_page_id);
CREATE INDEX idx_relationships_target ON memory_relationships(target_page_id);
CREATE INDEX idx_relationships_type ON memory_relationships(relationship_type);
```

**Relationship Types (subset of AutoMem's):**
- `RELATES_TO` - General connection
- `FOLLOWS` - Temporal sequence
- `CONTRADICTS` - Conflicting information
- `REINFORCES` - Supporting evidence
- `DERIVED_FROM` - Source tracking

**Retrieval Enhancement:**
```csharp
// After initial retrieval, expand with related memories
var relatedPages = await _store.GetRelatedPagesAsync(
    retrievedPageIds, 
    maxHops: 1,
    relationshipTypes: ["RELATES_TO", "REINFORCES"],
    ct);
```

**Benefits:**
- Enables multi-hop reasoning without graph database
- Connects related conversations across time
- Surfaces supporting/contradicting evidence

---

#### 2.2 Memory Consolidation (Background)

**Inspiration:** AutoMem's consolidation cycles (Decay, Creative, Cluster, Forget)

**Proposed Implementation:**

Add a background service that periodically:

1. **Relevance Decay:** Update relevance scores based on age and access
2. **Similarity Linking:** Find and link semantically similar memories
3. **Cleanup:** Archive or delete low-relevance memories

```csharp
public class MemoryConsolidationService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // Run decay every hour
            await RunDecayCycleAsync(ct);
            
            // Run similarity linking every 6 hours
            if (ShouldRunSimilarityLinking())
                await RunSimilarityLinkingAsync(ct);
            
            // Run cleanup daily
            if (ShouldRunCleanup())
                await RunCleanupCycleAsync(ct);
            
            await Task.Delay(TimeSpan.FromHours(1), ct);
        }
    }
    
    private async Task RunDecayCycleAsync(CancellationToken ct)
    {
        // Update relevance scores: older + less accessed = lower score
        await _store.ExecuteAsync(@"
            UPDATE memory_pages SET
                relevance_score = importance 
                    * EXP(-0.01 * EXTRACT(EPOCH FROM (NOW() - last_accessed_at)) / 86400)
                    * (0.7 + 0.3 * LEAST(1.0, access_count / 10.0))
            WHERE owner_id = @ownerId
        ", ct);
    }
}
```

**Configuration:**
```csharp
public class ConsolidationOptions
{
    public bool Enabled { get; set; } = false;
    public TimeSpan DecayInterval { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromDays(1);
    public float ArchiveThreshold { get; set; } = 0.2f;
    public float DeleteThreshold { get; set; } = 0.05f;
    public int GracePeriodDays { get; set; } = 30;
}
```

---

#### 2.3 Retrieval Scoring Weights (Configurable)

**Inspiration:** AutoMem's 9-component scoring with explicit weights

**Proposed Implementation:**
```csharp
public class RetrievalOptions
{
    /// <summary>Weight for vector/semantic similarity (default 30%)</summary>
    public float VectorWeight { get; set; } = 0.30f;
    
    /// <summary>Weight for BM25 keyword matching (default 25%)</summary>
    public float KeywordWeight { get; set; } = 0.25f;
    
    /// <summary>Weight for header/index matching (default 15%)</summary>
    public float HeaderWeight { get; set; } = 0.15f;
    
    /// <summary>Weight for importance score (default 10%)</summary>
    public float ImportanceWeight { get; set; } = 0.10f;
    
    /// <summary>Weight for recency (default 10%)</summary>
    public float RecencyWeight { get; set; } = 0.10f;
    
    /// <summary>Weight for relationship connections (default 10%)</summary>
    public float RelationshipWeight { get; set; } = 0.10f;
}
```

**Scoring Function:**
```csharp
float ComputeFinalScore(RetrievalCandidate candidate, RetrievalOptions opts)
{
    return opts.VectorWeight * candidate.VectorScore
         + opts.KeywordWeight * candidate.KeywordScore
         + opts.HeaderWeight * candidate.HeaderScore
         + opts.ImportanceWeight * candidate.Importance
         + opts.RecencyWeight * candidate.RecencyScore
         + opts.RelationshipWeight * candidate.RelationshipScore;
}
```

---

### Tier 3: High Impact, High Complexity

These are significant features that may require architectural changes.

#### 3.1 User Profile Extraction

**Inspiration:** EverMemOS's ProfileMemory with evidence trails

**Concept:** Periodically analyze memories to build/update user profiles.

```csharp
public record UserProfile
{
    public string OwnerId { get; init; }
    public Dictionary<string, ProfileAttribute> Preferences { get; init; }
    public Dictionary<string, ProfileAttribute> Skills { get; init; }
    public Dictionary<string, ProfileAttribute> Traits { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record ProfileAttribute
{
    public string Value { get; init; }
    public float Confidence { get; init; }
    public string[] EvidencePageIds { get; init; }  // Traceability
}
```

**Implementation Complexity:** High - requires periodic LLM analysis of memory corpus.

---

#### 3.2 Foresight/Prediction Generation

**Inspiration:** EverMemOS's Foresight memories

**Concept:** Generate predictive insights about future user needs.

```csharp
public record Foresight
{
    public string Prediction { get; init; }
    public string Evidence { get; init; }
    public string[] SourcePageIds { get; init; }
    public DateTime ValidFrom { get; init; }
    public DateTime ValidUntil { get; init; }
}
```

**Example:** After memorizing "User is preparing for a job interview at Google next week", generate a foresight: "User may need information about Google's interview process, company culture, or technical interview preparation."

**Implementation Complexity:** High - requires sophisticated LLM prompting and temporal reasoning.

---

#### 3.3 Agentic Retrieval Enhancement

**Inspiration:** EverMemOS's agentic retrieval with query expansion

**Current GAM:** Uses Plan-Search-Integrate-Reflect loop.

**Enhancement:** Add query expansion before search.

```csharp
public class EnhancedResearchAgent
{
    private async Task<List<string>> ExpandQueryAsync(string query, CancellationToken ct)
    {
        var prompt = $"""
            Given this query: "{query}"
            
            Generate 2-3 alternative phrasings or related queries that might help find relevant memories.
            Consider:
            - Synonyms and paraphrases
            - Related concepts
            - Specific vs general formulations
            
            Return as JSON array of strings.
            """;
        
        var expandedQueries = await _llm.GenerateAsync(prompt, ct);
        return [query, ..expandedQueries]; // Original + expansions
    }
}
```

---

## Implementation Roadmap

### Phase 1: Quick Wins (1-2 weeks)

| Enhancement | Effort | Impact |
|-------------|--------|--------|
| Memory Importance Scoring | 4h | Medium |
| Memory Type Classification | 4h | Medium |
| Keyword/Entity Tags | 4h | Medium |
| Configurable Retrieval Weights | 2h | Low |

**Total:** ~14 hours

### Phase 2: Core Improvements (2-4 weeks)

| Enhancement | Effort | Impact |
|-------------|--------|--------|
| Memory Relationships Table | 8h | High |
| Relationship-aware Retrieval | 8h | High |
| Memory Consolidation Service | 12h | Medium |
| Query Expansion | 4h | Medium |

**Total:** ~32 hours

### Phase 3: Advanced Features (4-8 weeks)

| Enhancement | Effort | Impact |
|-------------|--------|--------|
| User Profile Extraction | 20h | High |
| Foresight Generation | 16h | Medium |
| Multi-hop Bridge Discovery | 12h | High |

**Total:** ~48 hours

---

## Recommendation

**Start with Tier 1 enhancements.** They provide meaningful improvements while:
- Preserving GAM's single-database simplicity
- Not requiring infrastructure changes
- Being incrementally adoptable
- Laying groundwork for Tier 2/3

**Priority order:**
1. **Memory Importance + Access Tracking** - Immediate retrieval quality improvement
2. **Memory Type Classification** - Enables filtered retrieval
3. **Keyword/Entity Tags** - Improves keyword search precision
4. **Configurable Retrieval Weights** - Allows tuning for specific use cases

After Tier 1, evaluate if the benchmark improvements justify the complexity of Tier 2 (relationships) and Tier 3 (profiles/foresight).

---

## References

- [EverMemOS GitHub](https://github.com/EverMemAI/EverMemOS)
- [AutoMem GitHub](https://github.com/topoteretes/automem)
- [GAM Paper (arXiv:2511.18423)](https://arxiv.org/abs/2511.18423)
- [LoCoMo Benchmark](https://github.com/snap-stanford/locomo)
