-- GAM.NET PostgreSQL Schema with VectorChord-bm25
-- Image: tensorchord/vchord-suite:pg17-latest
-- Includes: pgvector, pg_tokenizer, vchord_bm25

-- ============================================================================
-- ENABLE EXTENSIONS
-- ============================================================================
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_tokenizer CASCADE;
CREATE EXTENSION IF NOT EXISTS vchord_bm25 CASCADE;

-- ============================================================================
-- CREATE TOKENIZER
-- Using bert_base_uncased pre-trained model for English text
-- ============================================================================
SELECT create_tokenizer('gam_tokenizer', $$
model = "bert_base_uncased"
$$);

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
    bm25_content bm25vector,  -- VectorChord BM25 column
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
-- BM25 INDEX (VectorChord-bm25)
-- Note: BM25 index created after data load for better performance
-- Run this after inserting data:
--   UPDATE memory_pages SET bm25_content = tokenize(content, 'gam_tokenizer');
--   CREATE INDEX pages_bm25_idx ON memory_pages USING bm25 (bm25_content bm25_ops);
-- ============================================================================

-- Create trigger to auto-populate bm25_content on insert/update
CREATE OR REPLACE FUNCTION update_bm25_content()
RETURNS TRIGGER AS $$
BEGIN
    NEW.bm25_content := tokenize(NEW.content, 'gam_tokenizer');
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_update_bm25_content
    BEFORE INSERT OR UPDATE OF content ON memory_pages
    FOR EACH ROW
    EXECUTE FUNCTION update_bm25_content();

-- Create the BM25 index (will work after first data is inserted)
-- Note: Index is created but will be empty until data is added
CREATE INDEX IF NOT EXISTS pages_bm25_idx ON memory_pages USING bm25 (bm25_content bm25_ops);
