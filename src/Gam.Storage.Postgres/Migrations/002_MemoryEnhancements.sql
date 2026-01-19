-- GAM.NET Memory Enhancements (ADR-0002)
-- Adds importance scoring, access tracking, memory types, and entity tags

-- ============================================================================
-- ENHANCEMENT 1: Importance & Access Tracking (memory_pages)
-- ============================================================================

-- Importance score: 0.0-1.0, higher = more significant
-- Default 0.5 = neutral importance, can be set by LLM or user
ALTER TABLE memory_pages ADD COLUMN IF NOT EXISTS importance FLOAT DEFAULT 0.5;

-- Access tracking for usage-based relevance
ALTER TABLE memory_pages ADD COLUMN IF NOT EXISTS access_count INTEGER DEFAULT 0;
ALTER TABLE memory_pages ADD COLUMN IF NOT EXISTS last_accessed_at TIMESTAMPTZ;

-- ============================================================================
-- ENHANCEMENT 2: Memory Type Classification (memory_abstracts)
-- ============================================================================

-- Memory type for filtered retrieval
-- Types: conversation, decision, preference, fact, insight, task, context
ALTER TABLE memory_abstracts ADD COLUMN IF NOT EXISTS memory_type VARCHAR(20) DEFAULT 'conversation';

-- ============================================================================
-- ENHANCEMENT 3: Entity Tags (memory_abstracts)
-- ============================================================================

-- Tags for entity-based retrieval
-- Format: entity:person:name, entity:tool:name, keyword:topic
ALTER TABLE memory_abstracts ADD COLUMN IF NOT EXISTS tags TEXT[] DEFAULT '{}';

-- ============================================================================
-- INDEXES
-- ============================================================================

-- Index for importance-based queries (e.g., "get high importance memories")
CREATE INDEX IF NOT EXISTS idx_pages_importance ON memory_pages(owner_id, importance DESC);

-- Index for memory type filtering
CREATE INDEX IF NOT EXISTS idx_abstracts_type ON memory_abstracts(owner_id, memory_type);

-- GIN index for tag-based queries (e.g., "entity:person:john")
CREATE INDEX IF NOT EXISTS idx_abstracts_tags ON memory_abstracts USING GIN(tags);
