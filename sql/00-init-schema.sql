-- =============================================================================
-- DocDuck Database Schema Initialization
-- =============================================================================
-- This script creates all required tables, indexes, and extensions for DocDuck.
-- It is designed to be idempotent and can be run multiple times safely.
--
-- Required PostgreSQL extensions: pgvector, pg_trgm
--
-- Tables created:
--   - docs_chunks: Document chunks with vector embeddings
--   - docs_files: File metadata and tracking (etag, last_modified)
--   - providers: Document provider registration and status
--   - provider_settings: Provider configuration (JSONB)
--   - ai_provider_settings: AI provider configuration (JSONB)
--   - admin_users: Admin users for API access
-- =============================================================================

-- Enable required extensions
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- =============================================================================
-- Provider Management Tables
-- =============================================================================

-- Provider registration and metadata
CREATE TABLE IF NOT EXISTS providers (
    provider_type TEXT NOT NULL,
    provider_name TEXT NOT NULL,
    is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    registered_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_sync_at TIMESTAMPTZ,
    metadata JSONB,
    PRIMARY KEY (provider_type, provider_name)
);

-- Provider settings (configuration)
CREATE TABLE IF NOT EXISTS provider_settings (
    provider_type TEXT NOT NULL,
    provider_name TEXT NOT NULL,
    settings JSONB NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (provider_type, provider_name)
);

-- AI provider settings (OpenAI, etc.)
-- Each AI model/embedding gets its own row with provider_id as primary key
-- Supports flexible configuration for any OpenAI-compatible API
CREATE TABLE IF NOT EXISTS ai_provider_settings (
    provider_id TEXT PRIMARY KEY,
    provider_type TEXT NOT NULL, -- 'chat' or 'embedding'
    settings JSONB NOT NULL,

    -- Flexible model configuration (added in v2 for multi-provider support)
    url TEXT, -- Full API endpoint URL (e.g., https://api.openai.com/v1/chat/completions)
    headers JSONB DEFAULT '{"Content-Type": "application/json"}'::jsonb, -- HTTP headers
    request_template JSONB, -- Request body template with variable placeholders
    response_mapping JSONB, -- JSONPath expressions for extracting response data
    default_params JSONB DEFAULT '{}'::jsonb, -- Model-specific defaults (temperature, etc.)

    test_status TEXT NOT NULL DEFAULT 'Untested', -- 'Untested', 'Passed', 'Failed'
    last_tested_at TIMESTAMPTZ,
    last_test_message TEXT,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- =============================================================================
-- Document Storage Tables
-- =============================================================================

-- Document chunks with embeddings for vector search
CREATE TABLE IF NOT EXISTS docs_chunks (
    id BIGSERIAL PRIMARY KEY,
    doc_id TEXT NOT NULL,
    filename TEXT NOT NULL,
    provider_type TEXT NOT NULL,
    provider_name TEXT NOT NULL,
    chunk_num INT NOT NULL,
    text TEXT NOT NULL,
    metadata JSONB,
    embedding vector(1536),
    search_lexeme tsvector GENERATED ALWAYS AS (to_tsvector('simple', coalesce(text, ''))) STORED,
    created_at TIMESTAMPTZ DEFAULT now(),

    -- Unique constraint: same doc_id+chunk_num can exist across different providers
    CONSTRAINT unique_doc_chunk_provider UNIQUE (doc_id, chunk_num, provider_type, provider_name)
);

-- File tracking for deduplication and change detection
CREATE TABLE IF NOT EXISTS docs_files (
    doc_id TEXT NOT NULL,
    provider_type TEXT NOT NULL,
    provider_name TEXT NOT NULL,
    filename TEXT NOT NULL,
    etag TEXT NOT NULL,
    last_modified TIMESTAMPTZ NOT NULL,
    relative_path TEXT,
    avg_embedding vector(1536), -- Document-level average embedding for two-stage retrieval
    PRIMARY KEY (doc_id, provider_type, provider_name)
);

-- =============================================================================
-- Admin and Authentication Tables
-- =============================================================================

-- Admin users for API access
CREATE TABLE IF NOT EXISTS admin_users (
    id UUID PRIMARY KEY,
    username TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    is_admin BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- =============================================================================
-- Indexes for Performance
-- =============================================================================

-- Vector similarity search index (cosine distance)
-- Lists parameter should be ~rows/1000 for optimal performance
CREATE INDEX IF NOT EXISTS docs_chunks_embedding_idx
    ON docs_chunks USING ivfflat (embedding vector_cosine_ops)
    WITH (lists = 100);

-- Lexical search index on generated tsvector column
CREATE INDEX IF NOT EXISTS docs_chunks_search_lexeme_idx
    ON docs_chunks USING GIN (search_lexeme);

-- Common query indexes
CREATE INDEX IF NOT EXISTS docs_chunks_doc_id_idx
    ON docs_chunks(doc_id);

CREATE INDEX IF NOT EXISTS docs_chunks_filename_idx
    ON docs_chunks(filename);

CREATE INDEX IF NOT EXISTS docs_chunks_provider_idx
    ON docs_chunks(provider_type, provider_name);

CREATE INDEX IF NOT EXISTS docs_chunks_created_at_idx
    ON docs_chunks(created_at);

-- JSONB metadata index for flexible queries
CREATE INDEX IF NOT EXISTS docs_chunks_metadata_idx
    ON docs_chunks USING GIN (metadata);

-- File tracking indexes
CREATE INDEX IF NOT EXISTS docs_files_provider_idx
    ON docs_files(provider_type, provider_name);

CREATE INDEX IF NOT EXISTS docs_files_filename_idx
    ON docs_files(filename);

-- Document-level vector similarity search index (for two-stage retrieval)
CREATE INDEX IF NOT EXISTS docs_files_avg_embedding_idx
    ON docs_files USING ivfflat (avg_embedding vector_cosine_ops)
    WITH (lists = 100);

-- Admin user indexes
CREATE UNIQUE INDEX IF NOT EXISTS admin_users_username_lower_idx
    ON admin_users ((LOWER(username)));

-- AI provider settings indexes (for flexible model configuration)
CREATE INDEX IF NOT EXISTS ai_provider_settings_url_idx
    ON ai_provider_settings(url);

CREATE INDEX IF NOT EXISTS ai_provider_settings_headers_idx
    ON ai_provider_settings USING GIN (headers);

CREATE INDEX IF NOT EXISTS ai_provider_settings_default_params_idx
    ON ai_provider_settings USING GIN (default_params);

-- =============================================================================
-- Verification and Statistics
-- =============================================================================

DO $$
BEGIN
    RAISE NOTICE '=============================================================================';
    RAISE NOTICE 'DocDuck database schema initialized successfully!';
    RAISE NOTICE '=============================================================================';
    RAISE NOTICE 'Extensions: vector, pg_trgm';
    RAISE NOTICE 'Tables: docs_chunks, docs_files, providers, provider_settings, ai_provider_settings, admin_users';
    RAISE NOTICE 'Indexes: embedding (ivfflat), search_lexeme (GIN), metadata (GIN), and supporting indexes';
    RAISE NOTICE '=============================================================================';
END $$;

-- Display current table sizes and row counts
SELECT
    'docs_chunks' AS table_name,
    COUNT(*) AS row_count,
    COUNT(DISTINCT doc_id) AS unique_docs,
    pg_size_pretty(pg_total_relation_size('docs_chunks')) AS total_size
FROM docs_chunks
UNION ALL
SELECT
    'docs_files' AS table_name,
    COUNT(*) AS row_count,
    NULL as unique_docs,
    pg_size_pretty(pg_total_relation_size('docs_files')) AS total_size
FROM docs_files
UNION ALL
SELECT
    'providers' AS table_name,
    COUNT(*) AS row_count,
    NULL as unique_docs,
    pg_size_pretty(pg_total_relation_size('providers')) AS total_size
FROM providers
UNION ALL
SELECT
    'admin_users' AS table_name,
    COUNT(*) AS row_count,
    NULL as unique_docs,
    pg_size_pretty(pg_total_relation_size('admin_users')) AS total_size
FROM admin_users;
