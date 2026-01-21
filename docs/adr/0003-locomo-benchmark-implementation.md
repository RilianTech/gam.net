# ADR-0003: LoCoMo Benchmark Implementation

**Status:** Proposed  
**Date:** 2025-01-20  
**Authors:** @dmaman  
**References:** 
- [LoCoMo Paper (ACL 2024)](https://arxiv.org/abs/2402.17753)
- [LoCoMo Repository](https://github.com/snap-research/locomo)
- [SimpleMem](https://github.com/aiming-lab/SimpleMem) - 43.24% F1 on LoCoMo-10
- [ADR-0001: Deep Research](./0001-jit-vs-aot-embedding-strategy.md)
- [ADR-0002: Memory Enhancements](./0002-memory-enhancements-from-competing-systems.md)

## Context

LoCoMo (Long-term Conversational Memory) is the standard benchmark for evaluating LLM memory systems, published at ACL 2024. It tests a system's ability to recall information from very long-term conversations spanning weeks/months.

### Why LoCoMo?

1. **Industry Standard** - Used by SimpleMem, Mem0, A-Mem, LightMem to report results
2. **Comprehensive** - Tests SingleHop, MultiHop, Temporal, and Adversarial queries
3. **Realistic** - Multi-session conversations with timestamps, similar to real agent use cases
4. **Comparable** - Published baselines allow us to compare GAM against other systems

### Current State

We have custom benchmarks that measure fact recall on synthetic data. While useful for development, they don't allow comparison with other memory systems.

## LoCoMo Dataset Structure

The LoCoMo-10 dataset contains 10 conversations, each with:

```json
{
  "sample_id": "conversation_1",
  "conversation": {
    "speaker_a": "Caroline",
    "speaker_b": "Melanie",
    "session_1_date_time": "1:56 pm on 8 May, 2023",
    "session_1": [
      {"speaker": "Caroline", "dia_id": "D1:1", "text": "Hey Mel! Good to see you!"},
      {"speaker": "Melanie", "dia_id": "D1:2", "text": "Hey Caroline! ..."}
    ],
    "session_2_date_time": "1:14 pm on 25 May, 2023",
    "session_2": [...],
    // ... up to 19 sessions spanning months
  },
  "qa": [
    {
      "question": "When did Caroline go to the LGBTQ support group?",
      "answer": "7 May 2023",
      "evidence": ["D1:3"],
      "category": 2  // Temporal
    },
    {
      "question": "What fields would Caroline likely pursue in her education?",
      "answer": "Psychology, counseling certification",
      "evidence": ["D1:9", "D1:11"],
      "category": 3  // MultiHop reasoning
    }
  ]
}
```

### QA Categories

| Category | ID | Description | Example |
|----------|-----|-------------|---------|
| **SingleHop** | 1 | Single turn lookup | "What is Caroline's identity?" |
| **Temporal** | 2 | Time-based reasoning | "When did Melanie run a charity race?" |
| **MultiHop** | 3 | Connect multiple turns | "What career would Caroline pursue?" |
| **Knowledge** | 4 | General knowledge + memory | "What does sunflowers represent?" |
| **Adversarial** | 5 | Tests for hallucination | Swapped speaker names |

### Baseline Results (GPT-4.1-mini on LoCoMo-10)

| System | SingleHop F1 | MultiHop F1 | Temporal F1 | Avg F1 | Total Time |
|--------|-------------|-------------|-------------|--------|------------|
| **SimpleMem** | 51.12% | 43.46% | 58.62% | **43.24%** | 480.9s |
| Mem0 | 41.3% | 30.14% | 48.91% | 34.20% | 1934.3s |
| A-Mem | - | - | - | 32.58% | 5937.2s |
| LightMem | - | - | - | 24.63% | 675.9s |

## Proposed Implementation

### Phase 1: Dataset Loader & Evaluation Framework

Create a LoCoMo benchmark runner that:

1. **Loads LoCoMo dataset** from JSON
2. **Ingests conversations** via `GamService.MemorizeAsync()`
3. **Runs QA evaluation** via `GamService.ResearchAsync()` + LLM answer generation
4. **Computes F1 scores** per category and overall

```csharp
public class LoCoMoBenchmarkRunner
{
    public async Task<LoCoMoResults> RunAsync(
        IGamService gam,
        ILanguageModel llm,
        LoCoMoDataset dataset,
        CancellationToken ct);
}

public record LoCoMoResults
{
    public float SingleHopF1 { get; init; }
    public float MultiHopF1 { get; init; }
    public float TemporalF1 { get; init; }
    public float AdversarialF1 { get; init; }
    public float AverageF1 { get; init; }
    public TimeSpan ConstructionTime { get; init; }
    public TimeSpan RetrievalTime { get; init; }
    public TimeSpan TotalTime { get; init; }
    public List<LoCoMoQueryResult> QueryResults { get; init; }
}
```

### Phase 2: Conversation Ingestion

Map LoCoMo sessions to GAM conversation turns:

```csharp
// For each conversation in dataset
foreach (var conversation in dataset.Conversations)
{
    // For each session (D1, D2, ... D19)
    foreach (var session in conversation.Sessions)
    {
        var sessionTimestamp = ParseDateTime(session.DateTime);
        
        // Combine all turns in session into a single conversation turn
        var content = FormatSessionContent(session);
        
        await gam.MemorizeAsync(new MemorizeRequest
        {
            Turn = new ConversationTurn
            {
                OwnerId = $"locomo-{conversation.SampleId}",
                Content = content,
                Timestamp = sessionTimestamp,
                Metadata = new Dictionary<string, object>
                {
                    ["session_id"] = session.Id,
                    ["speaker_a"] = conversation.SpeakerA,
                    ["speaker_b"] = conversation.SpeakerB
                }
            }
        }, ct);
    }
}
```

### Phase 3: QA Evaluation with F1 Scoring

For each question:
1. Research relevant memories
2. Generate answer using LLM with retrieved context
3. Compute token-level F1 against ground truth

```csharp
public async Task<float> EvaluateQuestionAsync(
    LoCoMoQuestion qa,
    string ownerId,
    CancellationToken ct)
{
    // 1. Research
    var context = await _gam.ResearchAsync(new ResearchRequest
    {
        OwnerId = ownerId,
        Query = qa.Question
    }, ct);
    
    // 2. Generate answer
    var prompt = $"""
        Based on the following conversation history, answer the question.
        
        Context:
        {context.FormatForPrompt()}
        
        Question: {qa.Question}
        
        Answer concisely with only the relevant information.
        """;
    
    var generatedAnswer = await _llm.GenerateAsync(prompt, ct);
    
    // 3. Compute F1
    return ComputeF1Score(generatedAnswer, qa.Answer);
}

private float ComputeF1Score(string predicted, string groundTruth)
{
    var predTokens = Tokenize(predicted.ToLowerInvariant());
    var truthTokens = Tokenize(groundTruth.ToLowerInvariant());
    
    var common = predTokens.Intersect(truthTokens).Count();
    
    if (common == 0) return 0f;
    
    var precision = (float)common / predTokens.Count;
    var recall = (float)common / truthTokens.Count;
    
    return 2 * (precision * recall) / (precision + recall);
}
```

### Phase 4: Category-wise Reporting

Track results by category:

```csharp
public record LoCoMoCategoryResults
{
    public LoCoMoCategory Category { get; init; }
    public int TotalQuestions { get; init; }
    public float AverageF1 { get; init; }
    public float MedianF1 { get; init; }
    public TimeSpan AverageQueryTime { get; init; }
}
```

Output format matching SimpleMem's reporting:

```
=== LoCoMo-10 Benchmark Results (GPT-4o-mini) ===

Category Results:
  SingleHop:    F1=XX.XX%  (N questions, avg XXms)
  MultiHop:     F1=XX.XX%  (N questions, avg XXms)
  Temporal:     F1=XX.XX%  (N questions, avg XXms)
  Adversarial:  F1=XX.XX%  (N questions, avg XXms)
  
Overall:
  Average F1:        XX.XX%
  Construction Time: XXX.Xs
  Retrieval Time:    XXX.Xs
  Total Time:        XXX.Xs

Comparison with Baselines:
  SimpleMem: 43.24% (target)
  Mem0:      34.20%
  GAM:       XX.XX% (this run)
```

## File Structure

```
tests/Gam.Benchmarks/
├── Data/
│   └── locomo10.json              # Downloaded from LoCoMo repo
├── Framework/
│   ├── LoCoMoDataset.cs           # Dataset models
│   ├── LoCoMoLoader.cs            # JSON loader
│   ├── LoCoMoEvaluator.cs         # F1 scoring logic
│   └── LoCoMoResults.cs           # Result models
└── Benchmarks/
    └── LoCoMoBenchmarkTests.cs    # xUnit test class
```

## Configuration

```csharp
public class LoCoMoBenchmarkOptions
{
    /// <summary>Number of conversations to test (1-10, or null for all)</summary>
    public int? ConversationLimit { get; set; }
    
    /// <summary>Categories to test (null for all)</summary>
    public LoCoMoCategory[]? Categories { get; set; }
    
    /// <summary>Enable verbose per-question logging</summary>
    public bool VerboseOutput { get; set; } = false;
    
    /// <summary>Maximum questions per conversation (for quick testing)</summary>
    public int? QuestionLimit { get; set; }
}
```

## Running the Benchmark

```bash
# Full LoCoMo-10 benchmark (expensive - ~500+ LLM calls)
dotnet test tests/Gam.Benchmarks --filter "LoCoMoBenchmark.Full" -l "console;verbosity=detailed"

# Quick test with 1 conversation
dotnet test tests/Gam.Benchmarks --filter "LoCoMoBenchmark.Quick" -l "console;verbosity=detailed"

# Specific category
dotnet test tests/Gam.Benchmarks --filter "LoCoMoBenchmark.MultiHop" -l "console;verbosity=detailed"
```

## Success Criteria

### Minimum Viable
- [ ] Successfully ingest all 10 LoCoMo conversations
- [ ] Run QA evaluation for all categories
- [ ] Report F1 scores matching published methodology
- [ ] Achieve ≥30% average F1 (above LightMem baseline)

### Target Goals
- [ ] Achieve ≥35% average F1 (competitive with Mem0)
- [ ] Achieve ≥40% average F1 (near SimpleMem performance)
- [ ] Total time ≤1000s (faster than Mem0)

### Stretch Goals
- [ ] Match or exceed SimpleMem's 43.24% F1
- [ ] Identify and fix GAM-specific weaknesses per category
- [ ] Document optimizations discovered during benchmarking

## Implementation Plan

1. **Phase 1** (2-3 hours): Dataset loader and models
2. **Phase 2** (2-3 hours): Ingestion and evaluation framework
3. **Phase 3** (1-2 hours): F1 scoring and reporting
4. **Phase 4** (4+ hours): Run benchmarks, analyze results, iterate

## Risks and Mitigations

| Risk | Mitigation |
|------|------------|
| API costs (500+ LLM calls) | Start with 1 conversation, use gpt-4o-mini |
| Long runtime (~30+ min) | Run overnight, cache intermediate results |
| F1 scoring differences | Use exact methodology from LoCoMo paper |
| Temporal parsing complexity | Use flexible date parsing, log failures |

## References

- [LoCoMo Paper](https://arxiv.org/abs/2402.17753) - Evaluation methodology
- [SimpleMem Paper](https://arxiv.org/abs/2601.02553) - Best-in-class implementation
- [locomo10.json](https://github.com/snap-research/locomo/blob/main/data/locomo10.json) - Dataset
