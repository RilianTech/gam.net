-- GAM.NET PostgreSQL Schema with Timescale pg_textsearch
-- Image: timescale/timescaledb-ha:pg17
-- Includes: pgvector, vectorscale, pg_textsearch, timescaledb

-- ============================================================================
-- ENABLE EXTENSIONS
-- ============================================================================

-- pgvector for semantic/vector search
CREATE EXTENSION IF NOT EXISTS vector;

-- vectorscale for improved vector performance (optional, includes pgvector)
-- CREATE EXTENSION IF NOT EXISTS vectorscale CASCADE;

-- pg_textsearch for BM25 ranking (PostgreSQL licensed)
CREATE EXTENSION IF NOT EXISTS pg_textsearch;

-- ============================================================================
-- TABLES
-- ============================================================================

-- Memory pages table - stores raw conversation content
CREATE TABLE IF NOT EXISTS memory_pages (
    id UUID PRIMARY KEY,
    owner_id VARCHAR(255) NOT NULL,
    content TEXT NOT NULL,
    token_count INTEGER NOT NULL,
    embedding vector(1536),
    metadata JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Memory abstracts table - stores summaries and searchable headers
CREATE TABLE IF NOT EXISTS memory_abstracts (
    page_id UUID PRIMARY KEY REFERENCES memory_pages(id) ON DELETE CASCADE,
    owner_id VARCHAR(255) NOT NULL,
    summary TEXT NOT NULL,
    headers TEXT[] NOT NULL,
    summary_embedding vector(1536),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ============================================================================
-- BASIC INDEXES
-- ============================================================================
CREATE INDEX IF NOT EXISTS idx_pages_owner ON memory_pages(owner_id);
CREATE INDEX IF NOT EXISTS idx_pages_created ON memory_pages(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_abstracts_owner ON memory_abstracts(owner_id);

-- ============================================================================
-- VECTOR SIMILARITY INDEXES (pgvector HNSW)
-- ============================================================================
CREATE INDEX IF NOT EXISTS idx_pages_embedding ON memory_pages 
    USING hnsw (embedding vector_cosine_ops);
CREATE INDEX IF NOT EXISTS idx_abstracts_embedding ON memory_abstracts 
    USING hnsw (summary_embedding vector_cosine_ops);

-- ============================================================================
-- FULL-TEXT SEARCH INDEXES (Native PostgreSQL fallback)
-- ============================================================================
CREATE INDEX IF NOT EXISTS idx_pages_content_fts ON memory_pages 
    USING gin(to_tsvector('english', content));
CREATE INDEX IF NOT EXISTS idx_abstracts_headers ON memory_abstracts USING gin(headers);

-- ============================================================================
-- BM25 INDEX (pg_textsearch)
-- Uses simple syntax: content <@> 'query'
-- ============================================================================
CREATE INDEX IF NOT EXISTS idx_pages_bm25 ON memory_pages 
    USING bm25(content) WITH (text_config='english');
