-- Database schema for DocDuck Indexer
-- PostgreSQL with pgvector extension

-- Enable pgvector extension
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

-- Sample query to verify setup
-- SELECT count(*) FROM docs_chunks;
-- SELECT version(), * FROM pg_available_extensions WHERE name = 'vector';
