-- Migration: Initialize pgvector database schema
-- Run this once to set up the database for DocDuck

-- Enable pgvector extension (requires superuser or extension creation privilege)
CREATE EXTENSION IF NOT EXISTS vector;

-- Table for document chunks with embeddings
CREATE TABLE IF NOT EXISTS docs_chunks (
    id BIGSERIAL PRIMARY KEY,
    doc_id TEXT NOT NULL,
    filename TEXT NOT NULL,
    chunk_num INT NOT NULL,
    text TEXT NOT NULL,
    metadata JSONB,
    embedding vector(1536),
    created_at TIMESTAMPTZ DEFAULT now(),
    CONSTRAINT unique_doc_chunk UNIQUE (doc_id, chunk_num)
);

-- Table for tracking file state (ETag, last modified)
CREATE TABLE IF NOT EXISTS docs_files (
    doc_id TEXT PRIMARY KEY,
    filename TEXT NOT NULL,
    etag TEXT NOT NULL,
    last_modified TIMESTAMPTZ NOT NULL
);

-- Create index for vector similarity search (cosine distance)
-- Adjust lists parameter based on your dataset size (recommended: rows/1000)
CREATE INDEX IF NOT EXISTS docs_chunks_embedding_idx 
ON docs_chunks USING ivfflat (embedding vector_cosine_ops) 
WITH (lists = 100);

-- Create index on doc_id for faster lookups
CREATE INDEX IF NOT EXISTS docs_chunks_doc_id_idx ON docs_chunks(doc_id);

-- Create index on filename for search
CREATE INDEX IF NOT EXISTS docs_chunks_filename_idx ON docs_chunks(filename);

-- Create index on created_at for time-based queries
CREATE INDEX IF NOT EXISTS docs_chunks_created_at_idx ON docs_chunks(created_at);

-- Create GIN index on metadata JSONB for efficient JSON queries
CREATE INDEX IF NOT EXISTS docs_chunks_metadata_idx ON docs_chunks USING GIN (metadata);

-- Verify setup
DO $$
BEGIN
    RAISE NOTICE 'DocDuck database schema initialized successfully!';
    RAISE NOTICE 'Tables created: docs_chunks, docs_files';
    RAISE NOTICE 'Indexes created: embedding (ivfflat), doc_id, filename, created_at, metadata';
END $$;

-- Display current statistics
SELECT 
    'docs_chunks' AS table_name,
    COUNT(*) AS row_count,
    pg_size_pretty(pg_total_relation_size('docs_chunks')) AS total_size
FROM docs_chunks
UNION ALL
SELECT 
    'docs_files' AS table_name,
    COUNT(*) AS row_count,
    pg_size_pretty(pg_total_relation_size('docs_files')) AS total_size
FROM docs_files;
