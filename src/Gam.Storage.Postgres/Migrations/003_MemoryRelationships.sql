-- GAM.NET Memory Relationships (ADR-0002 Phase 4)
-- Enables relationship-aware retrieval for multi-hop reasoning

-- ============================================================================
-- RELATIONSHIPS TABLE
-- ============================================================================

CREATE TABLE IF NOT EXISTS memory_relationships (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    source_page_id UUID NOT NULL REFERENCES memory_pages(id) ON DELETE CASCADE,
    target_page_id UUID NOT NULL REFERENCES memory_pages(id) ON DELETE CASCADE,
    relationship_type VARCHAR(50) NOT NULL,
    confidence FLOAT DEFAULT 1.0,
    created_by VARCHAR(20) DEFAULT 'system',  -- 'system', 'llm', 'user'
    created_at TIMESTAMPTZ DEFAULT NOW(),
    
    -- Prevent duplicate relationships
    UNIQUE(source_page_id, target_page_id, relationship_type)
);

-- ============================================================================
-- RELATIONSHIP TYPES
-- ============================================================================
-- RELATES_TO  : Topically related (created by background job, tag/entity overlap)
-- FOLLOWS     : Temporal sequence (same conversation, adjacent turns)
-- CONTRADICTS : Conflicting information (detected by LLM during research)
-- REINFORCES  : Supporting evidence (detected by LLM during research)

-- ============================================================================
-- INDEXES
-- ============================================================================

-- Find relationships FROM a page (outbound)
CREATE INDEX IF NOT EXISTS idx_rel_source ON memory_relationships(source_page_id);

-- Find relationships TO a page (inbound)
CREATE INDEX IF NOT EXISTS idx_rel_target ON memory_relationships(target_page_id);

-- Filter by relationship type
CREATE INDEX IF NOT EXISTS idx_rel_type ON memory_relationships(relationship_type);

-- Find all relationships for an owner (via join)
-- This composite index helps when expanding search results with related pages
CREATE INDEX IF NOT EXISTS idx_rel_source_type ON memory_relationships(source_page_id, relationship_type);
