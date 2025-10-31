-- =============================================================================
-- Backfill Document-Level Embeddings (One-Time Migration)
-- =============================================================================
-- This migration backfills avg_embedding for existing documents in the database.
-- 
-- NOTE: For new deployments, the schema (00-init-schema.sql) already includes
-- the avg_embedding column and index. This migration is ONLY needed for existing
-- databases that were created before this feature was added.
--
-- The indexer automatically maintains avg_embedding going forward.
-- =============================================================================

-- Add column if it doesn't exist (for databases created before this feature)
DO $$ 
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name='docs_files' AND column_name='avg_embedding'
    ) THEN
        ALTER TABLE docs_files ADD COLUMN avg_embedding vector(1536);
        RAISE NOTICE 'Added avg_embedding column to docs_files table';
    ELSE
        RAISE NOTICE 'Column avg_embedding already exists, skipping ALTER TABLE';
    END IF;
END $$;

-- Create index if it doesn't exist
CREATE INDEX IF NOT EXISTS docs_files_avg_embedding_idx 
    ON docs_files USING ivfflat (avg_embedding vector_cosine_ops) 
    WITH (lists = 100);

-- Backfill: Compute average embeddings for all existing documents
DO $$
DECLARE
    total_docs INTEGER;
    processed INTEGER := 0;
    updated INTEGER := 0;
BEGIN
    SELECT COUNT(*) INTO total_docs 
    FROM docs_files 
    WHERE avg_embedding IS NULL;
    
    IF total_docs = 0 THEN
        RAISE NOTICE 'All documents already have avg_embedding. Nothing to backfill.';
        RETURN;
    END IF;
    
    RAISE NOTICE 'Backfilling document-level embeddings for % documents...', total_docs;
    
    -- Update avg_embedding for all documents that don't have it yet
    UPDATE docs_files f
    SET avg_embedding = (
        SELECT avg(c.embedding)::vector(1536)
        FROM docs_chunks c
        WHERE c.doc_id = f.doc_id 
          AND c.provider_type = f.provider_type 
          AND c.provider_name = f.provider_name
          AND c.embedding IS NOT NULL
    )
    WHERE f.avg_embedding IS NULL;
    
    GET DIAGNOSTICS updated = ROW_COUNT;
    
    RAISE NOTICE 'Backfill complete: Updated % documents with avg_embedding', updated;
END $$;

-- Verify the migration
DO $$
DECLARE
    docs_with_avg INTEGER;
    total_docs INTEGER;
BEGIN
    SELECT COUNT(*) INTO docs_with_avg FROM docs_files WHERE avg_embedding IS NOT NULL;
    SELECT COUNT(*) INTO total_docs FROM docs_files;
    
    RAISE NOTICE '=============================================================================';
    RAISE NOTICE 'Document-level embedding migration complete!';
    RAISE NOTICE '=============================================================================';
    RAISE NOTICE 'Documents with avg_embedding: % / %', docs_with_avg, total_docs;
    
    IF total_docs > 0 AND docs_with_avg = 0 THEN
        RAISE WARNING 'No documents have embeddings. This may indicate chunks are missing embeddings.';
    END IF;
END $$;
