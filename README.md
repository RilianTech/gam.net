# GAM.NET - General Agentic Memory for .NET

A C# implementation of the General Agentic Memory (GAM) framework for building AI agents with long-term memory capabilities.

## Attribution

This project is a .NET implementation based on the research and concepts from:

- **Paper**: [General Agentic Memory for Large Language Model Agents](https://arxiv.org/html/2511.18423v1) - The foundational research describing the GAM framework and JIT memory retrieval paradigm.
- **Original Implementation**: [VectorSpaceLab/general-agentic-memory](https://github.com/VectorSpaceLab/general-agentic-memory) - The original Python implementation by VectorSpaceLab.

## What is GAM?

GAM (General Agentic Memory) is a memory system for AI agents that uses a JIT (Just-in-Time) compilation paradigm. Instead of pre-processing all memories into a fixed format, GAM stores raw conversation data and dynamically retrieves relevant context at query time.

### Key Concepts

| Concept | Description |
|---------|-------------|
| **Page** | A unit of memory storage containing raw conversation content |
| **Abstract** | A structured summary of a page with searchable headers |
| **MemoryAgent** | Offline agent that processes conversations into pages + abstracts |
| **ResearchAgent** | Online agent that retrieves relevant memories via iterative research |
| **JIT Retrieval** | Dynamic, query-time memory retrieval (vs. pre-compiled summaries) |

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        MEMORIZE (Offline)                        │
├─────────────────────────────────────────────────────────────────┤
│  Conversation    ┌──────────────┐    ┌─────────┐   ┌─────────┐  │
│  Turn ──────────▶│ MemoryAgent  │───▶│ Abstract│ + │  Page   │  │
│                  └──────────────┘    └─────────┘   └─────────┘  │
│                         │                 │             │        │
│                    (LLM call)        (headers)    (raw content)  │
│                                          ▼             ▼        │
│                                    ┌─────────────────────┐      │
│                                    │   Memory Store      │      │
│                                    │   (PostgreSQL)      │      │
│                                    └─────────────────────┘      │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                        RESEARCH (Online)                         │
├─────────────────────────────────────────────────────────────────┤
│  Query ──────────▶┌───────────────┐                             │
│                   │ ResearchAgent │◀─────────────────┐          │
│                   └───────┬───────┘                  │          │
│                           │                          │          │
│            ┌──────────────┼──────────────┐           │          │
│            ▼              ▼              ▼           │          │
│       ┌────────┐    ┌──────────┐   ┌──────────┐     │          │
│       │  Plan  │───▶│  Search  │──▶│Integrate │─────┘          │
│       └────────┘    └──────────┘   └──────────┘                 │
│                           │              │                       │
│                           ▼              ▼                       │
│                    ┌───────────┐   ┌───────────┐                │
│                    │ Retrievers│   │  Context  │                │
│                    │(BM25/Vec) │   │  Builder  │                │
│                    └───────────┘   └───────────┘                │
└─────────────────────────────────────────────────────────────────┘
```

## Installation

```bash
# Core library (no external dependencies)
dotnet add package Gam.Core

# PostgreSQL storage with pgvector
dotnet add package Gam.Storage.Postgres

# OpenAI/Azure OpenAI provider
dotnet add package Gam.Providers.OpenAI

# Ollama provider (local inference)
dotnet add package Gam.Providers.Ollama
```

## Quick Start

```csharp
using Gam.Core;
using Gam.Core.Models;
using Gam.Providers.OpenAI;
using Gam.Storage.Postgres;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddLogging();
services.AddGamCore();
services.AddGamPostgresStorage("Host=localhost;Database=gam");
services.AddGamOpenAI("sk-your-api-key");

var provider = services.BuildServiceProvider();
var gam = provider.GetRequiredService<IGamService>();

// Store a memory
await gam.MemorizeAsync(new MemorizeRequest
{
    Turn = new ConversationTurn
    {
        OwnerId = "user-123",
        UserMessage = "How do I configure Kubernetes health checks?",
        AssistantMessage = "You can configure liveness and readiness probes...",
        Timestamp = DateTimeOffset.UtcNow
    }
});

// Research memories
var context = await gam.ResearchAsync(new ResearchRequest
{
    OwnerId = "user-123",
    Query = "What did we discuss about Kubernetes?"
});

Console.WriteLine($"Found {context.Pages.Count} relevant memories");
Console.WriteLine(context.FormatForPrompt());
```

## Database Setup

GAM.NET requires PostgreSQL with the [pgvector](https://github.com/pgvector/pgvector) extension for vector similarity search.

### Optional: BM25 Full-Text Search

For better keyword search (true BM25 ranking), you can install one of these extensions:

| Extension | License | Status | Notes |
|-----------|---------|--------|-------|
| [pg_textsearch](https://github.com/timescale/pg_textsearch) | PostgreSQL | Pre-release | Most permissive license |
| [ParadeDB pg_search](https://github.com/paradedb/paradedb) | AGPLv3 | Production | Most mature, Elasticsearch alternative |
| [VectorChord-bm25](https://github.com/tensorchord/VectorChord-bm25) | AGPLv3/ELv2 | v0.3.0 | Requires separate tokenizer |

If no BM25 extension is installed, GAM.NET falls back to native PostgreSQL full-text search (`ts_rank`).

### Schema Setup

```sql
-- Enable required extension
CREATE EXTENSION IF NOT EXISTS vector;

-- Create tables
CREATE TABLE memory_pages (
    id UUID PRIMARY KEY,
    owner_id VARCHAR(255) NOT NULL,
    content TEXT NOT NULL,
    token_count INTEGER NOT NULL,
    embedding vector(1536),
    metadata JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE memory_abstracts (
    page_id UUID PRIMARY KEY REFERENCES memory_pages(id) ON DELETE CASCADE,
    owner_id VARCHAR(255) NOT NULL,
    summary TEXT NOT NULL,
    headers TEXT[] NOT NULL,
    summary_embedding vector(1536),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Create indexes
CREATE INDEX idx_pages_owner ON memory_pages(owner_id);
CREATE INDEX idx_pages_embedding ON memory_pages USING hnsw (embedding vector_cosine_ops);
CREATE INDEX idx_pages_content_fts ON memory_pages USING gin(to_tsvector('english', content));
```

See `src/Gam.Storage.Postgres/Migrations/001_InitialSchema.sql` for the complete schema.

## Providers

### OpenAI

```csharp
services.AddGamOpenAI(
    apiKey: "sk-...",
    model: "gpt-4o",
    embeddingModel: "text-embedding-3-small"
);
```

### Azure OpenAI

```csharp
services.AddGamAzureOpenAI(
    endpoint: "https://your-resource.openai.azure.com/",
    apiKey: "your-key",
    chatDeployment: "gpt-4o",
    embeddingDeployment: "text-embedding-3-small"
);
```

### Ollama (Local)

```csharp
services.AddGamOllama(
    baseUrl: "http://localhost:11434",
    llmModel: "llama3.2",
    embeddingModel: "nomic-embed-text"
);
```

## Project Structure

```
gam-dotnet/
├── src/
│   ├── Gam.Core/                    # Core abstractions and agents
│   ├── Gam.Storage.Postgres/        # PostgreSQL + pgvector
│   ├── Gam.Providers.OpenAI/        # OpenAI / Azure OpenAI
│   └── Gam.Providers.Ollama/        # Local Ollama
├── tests/
│   ├── Gam.Core.Tests/
│   └── Gam.Integration.Tests/
├── samples/
│   ├── Gam.Sample.Console/
│   └── Gam.Sample.WebApi/
└── Gam.sln
```

## Memory TTL (Time-To-Live)

For long-running applications, you can enable automatic cleanup of old memories:

```csharp
// Add TTL with 30-day expiration
services.AddGamMemoryTtl(TimeSpan.FromDays(30));

// Or with full configuration
services.AddGamMemoryTtl(options =>
{
    options.Enabled = true;
    options.MaxAge = TimeSpan.FromDays(30);
    options.CleanupInterval = TimeSpan.FromHours(1);
    options.OwnerIds = null; // Cleanup all owners, or specify specific ones
});
```

You can also manually clean up expired memories:

```csharp
var store = provider.GetRequiredService<IMemoryStore>();

// Delete memories older than 30 days
var deleted = await store.CleanupExpiredAsync(TimeSpan.FromDays(30));

// Delete memories before a specific date
await store.DeleteBeforeAsync(DateTimeOffset.UtcNow.AddDays(-30));

// Delete only for a specific owner
await store.CleanupExpiredAsync(TimeSpan.FromDays(30), ownerId: "user-123");
```

## AI SDK / Tool Calling Integration

GAM.NET provides OpenAI-compatible tool schemas for integration with AI frameworks like Vercel AI SDK, LangChain, or direct OpenAI function calling.

### Get Tool Schemas

```csharp
using Gam.Core.Tools;

// Get tool definitions in OpenAI format
var tools = GamToolSchemas.GetAllTools();

// Or as JSON
var json = GamToolSchemas.ToJson(indented: true);
```

### Available Tools

| Tool | Description |
|------|-------------|
| `gam_memorize` | Store a conversation turn in long-term memory |
| `gam_research` | Search memories for relevant context |
| `gam_forget` | Delete memories for a user |

### Execute Tool Calls

```csharp
using Gam.Core.Tools;

var handler = new GamToolHandler(gamService);

// Execute a tool call from an AI model
var result = await handler.ExecuteAsync(
    "gam_research",
    """{"owner_id": "user-123", "query": "What did we discuss about Kubernetes?"}"""
);

if (result.Success)
{
    Console.WriteLine(result.Content); // Memory context for the model
}
```

### REST API Endpoints

The WebApi sample exposes these endpoints:

```bash
# Get tool schemas
GET /tools

# Execute any tool
POST /tools/execute
{ "name": "gam_research", "arguments": "{\"owner_id\": \"user-123\", \"query\": \"...\"}" }

# Vercel AI SDK compatible
POST /v1/tools/research
{ "owner_id": "user-123", "query": "..." }
```

### Example: Vercel AI SDK Integration

```typescript
import { generateText, tool } from 'ai';

const result = await generateText({
  model: openai('gpt-4o'),
  tools: {
    gam_research: tool({
      description: 'Search long-term memory for relevant context',
      parameters: z.object({
        owner_id: z.string(),
        query: z.string(),
      }),
      execute: async ({ owner_id, query }) => {
        const res = await fetch('http://localhost:5000/v1/tools/research', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ owner_id, query }),
        });
        return res.json();
      },
    }),
  },
  prompt: 'What did the user previously ask about Kubernetes?',
});
```

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## References

- [GAM Paper](https://arxiv.org/abs/2511.18423) - General Agentic Memory for Large Language Model Agents
- [Original Implementation](https://github.com/VectorSpaceLab/general-agentic-memory) - Python implementation by VectorSpaceLab
- [pgvector](https://github.com/pgvector/pgvector) - Vector similarity search for PostgreSQL
