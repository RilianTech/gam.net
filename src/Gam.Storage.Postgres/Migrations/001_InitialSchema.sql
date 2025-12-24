-- GAM.NET PostgreSQL Schema
-- Tested with: PostgreSQL 17 + pgvector
-- Requires: pgvector extension for vector similarity search
-- Optional: BM25 extension for better keyword search (see options below)

-- ============================================================================
-- REQUIRED: Enable pgvector for semantic/vector search
-- ============================================================================
CREATE EXTENSION IF NOT EXISTS vector;

-- ============================================================================
-- OPTIONAL: Choose ONE BM25 extension for better keyword search
-- If none installed, GAM.NET falls back to native PostgreSQL full-text search
-- ============================================================================

-- OPTION A: pg_textsearch (Timescale) - PostgreSQL licensed (most permissive)
-- https://github.com/timescale/pg_textsearch
-- Status: Pre-release (v0.1.1-dev)
-- Syntax: content <@> 'query'
-- CREATE EXTENSION IF NOT EXISTS pg_textsearch;

-- OPTION B: ParadeDB pg_search - AGPLv3 (most mature, Elasticsearch alternative)
-- https://github.com/paradedb/paradedb
-- Status: Production (v0.20+)
-- Syntax: content @@@ 'query'
-- Note: ParadeDB is a full Postgres distribution with Tantivy embedded
-- CREATE EXTENSION IF NOT EXISTS pg_search;

-- OPTION C: VectorChord-bm25 (TensorChord) - AGPLv3/ELv2
-- https://github.com/tensorchord/VectorChord-bm25
-- Status: v0.3.0
-- Syntax: bm25_content <&> to_bm25query('index', query)
-- Requires: pg_tokenizer extension and additional column setup
-- CREATE EXTENSION IF NOT EXISTS pg_tokenizer CASCADE;
-- CREATE EXTENSION IF NOT EXISTS vchord_bm25 CASCADE;

-- ============================================================================
-- TABLES
-- ============================================================================

-- Memory pages table - stores raw conversation content
CREATE TABLE IF NOT EXISTS memory_pages (
    id UUID PRIMARY KEY,
    owner_id VARCHAR(255) NOT NULL,
    content TEXT NOT NULL,
    token_count INTEGER NOT NULL,
    embedding vector(1536),  -- Adjust dimension for your embedding model
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
-- VECTOR SIMILARITY INDEXES (pgvector)
-- ============================================================================

-- HNSW index - good for any dataset size, no training needed
CREATE INDEX IF NOT EXISTS idx_pages_embedding ON memory_pages 
    USING hnsw (embedding vector_cosine_ops);
CREATE INDEX IF NOT EXISTS idx_abstracts_embedding ON memory_abstracts 
    USING hnsw (summary_embedding vector_cosine_ops);

-- Alternative: IVFFlat for very large datasets (>1M rows)
-- Requires: VACUUM ANALYZE after loading data before creating
-- CREATE INDEX idx_pages_embedding_ivf ON memory_pages 
--     USING ivfflat (embedding vector_cosine_ops) WITH (lists = 100);

-- ============================================================================
-- FULL-TEXT SEARCH INDEXES (Native PostgreSQL - always works)
-- ============================================================================

-- GIN index for native full-text search (fallback when no BM25 extension)
CREATE INDEX IF NOT EXISTS idx_pages_content_fts ON memory_pages 
    USING gin(to_tsvector('english', content));

-- Headers array search for page index retriever
CREATE INDEX IF NOT EXISTS idx_abstracts_headers ON memory_abstracts USING gin(headers);

-- ============================================================================
-- BM25 INDEXES (extension-specific, uncomment based on your choice)
-- ============================================================================

-- OPTION A: pg_textsearch index
-- CREATE INDEX IF NOT EXISTS idx_pages_bm25 ON memory_pages 
--     USING bm25(content) WITH (text_config='english');

-- OPTION B: ParadeDB pg_search index
-- Note: ParadeDB auto-creates BM25 index, but you can customize:
-- CREATE INDEX IF NOT EXISTS idx_pages_search ON memory_pages
--     USING bm25 (id, content)
--     WITH (key_field='id', text_fields='{"content": {"tokenizer": {"type": "en_stem"}}}');

-- OPTION C: VectorChord-bm25 setup (more complex)
-- Step 1: Add bm25vector column
-- ALTER TABLE memory_pages ADD COLUMN bm25_content bm25vector;
-- Step 2: Create tokenizer
-- SELECT create_tokenizer('gam_tokenizer', $$model = "bert_base_uncased"$$);
-- Step 3: Populate bm25 vectors (run after data load)
-- UPDATE memory_pages SET bm25_content = tokenize(content, 'gam_tokenizer');
-- Step 4: Create index
-- CREATE INDEX pages_bm25_idx ON memory_pages USING bm25 (bm25_content bm25_ops);
