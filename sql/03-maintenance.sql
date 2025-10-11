-- Maintenance tasks for pgvector database
-- Run these periodically to keep the database healthy

-- =============================================================================
-- ROUTINE MAINTENANCE (Run daily/weekly)
-- =============================================================================

-- Update table statistics (helps query planner)
ANALYZE docs_chunks;
ANALYZE docs_files;

-- Vacuum to reclaim space and update stats (non-blocking)
VACUUM ANALYZE docs_chunks;
VACUUM ANALYZE docs_files;

-- =============================================================================
-- INDEX MAINTENANCE (Run after bulk changes)
-- =============================================================================

-- Rebuild vector index (run after adding >10% new data)
-- Use CONCURRENTLY to avoid blocking queries (requires >= PostgreSQL 12)
REINDEX INDEX CONCURRENTLY docs_chunks_embedding_idx;

-- Rebuild all indexes for docs_chunks
REINDEX TABLE CONCURRENTLY docs_chunks;

-- Update index statistics
ANALYZE docs_chunks;

-- =============================================================================
-- ADJUST IVFFLAT INDEX (Run when dataset size changes significantly)
-- =============================================================================

-- Calculate recommended lists parameter
-- Formula: SQRT(row_count) or row_count/1000, minimum 10
DO $$
DECLARE
    row_count INT;
    recommended_lists INT;
BEGIN
    SELECT COUNT(*) INTO row_count FROM docs_chunks;
    recommended_lists := GREATEST(10, ROUND(SQRT(row_count)));
    
    RAISE NOTICE 'Current row count: %', row_count;
    RAISE NOTICE 'Recommended lists parameter: %', recommended_lists;
    RAISE NOTICE 'To rebuild index: DROP INDEX docs_chunks_embedding_idx; then run:';
    RAISE NOTICE 'CREATE INDEX docs_chunks_embedding_idx ON docs_chunks USING ivfflat (embedding vector_cosine_ops) WITH (lists = %);', recommended_lists;
END $$;

-- Example: Rebuild with optimal lists parameter
-- Step 1: Drop old index
-- DROP INDEX CONCURRENTLY docs_chunks_embedding_idx;

-- Step 2: Create new index with updated lists (adjust number based on output above)
-- CREATE INDEX docs_chunks_embedding_idx ON docs_chunks USING ivfflat (embedding vector_cosine_ops) WITH (lists = 316);

-- =============================================================================
-- CLEANUP OLD DATA (Run periodically)
-- =============================================================================

-- Preview: Show documents older than 1 year
SELECT 
    f.doc_id,
    f.filename,
    f.last_modified,
    NOW() - f.last_modified AS age,
    COUNT(c.id) AS chunk_count
FROM docs_files f
LEFT JOIN docs_chunks c ON c.doc_id = f.doc_id
WHERE f.last_modified < NOW() - INTERVAL '1 year'
GROUP BY f.doc_id, f.filename, f.last_modified
ORDER BY f.last_modified;

-- Delete chunks for documents older than 1 year
-- WARNING: This is destructive! Verify with the preview query first.
/*
BEGIN;

DELETE FROM docs_chunks
WHERE doc_id IN (
    SELECT doc_id FROM docs_files
    WHERE last_modified < NOW() - INTERVAL '1 year'
);

DELETE FROM docs_files
WHERE last_modified < NOW() - INTERVAL '1 year';

COMMIT;
*/

-- =============================================================================
-- DISK SPACE RECLAMATION (Run after large deletes)
-- =============================================================================

-- VACUUM FULL reclaims disk space but requires exclusive lock
-- Run during maintenance window when no queries are running
/*
VACUUM FULL docs_chunks;
VACUUM FULL docs_files;
*/

-- Rebuild indexes after VACUUM FULL
/*
REINDEX TABLE docs_chunks;
REINDEX TABLE docs_files;
*/

-- =============================================================================
-- ORPHAN CLEANUP
-- =============================================================================

-- Find and remove orphaned chunks (no corresponding entry in docs_files)
-- This can happen if file tracking update fails
BEGIN;

-- Preview orphaned chunks
SELECT 
    c.doc_id,
    c.filename,
    COUNT(*) AS orphaned_chunks
FROM docs_chunks c
LEFT JOIN docs_files f ON c.doc_id = f.doc_id
WHERE f.doc_id IS NULL
GROUP BY c.doc_id, c.filename;

-- Uncomment to delete orphaned chunks
/*
DELETE FROM docs_chunks
WHERE doc_id IN (
    SELECT c.doc_id
    FROM docs_chunks c
    LEFT JOIN docs_files f ON c.doc_id = f.doc_id
    WHERE f.doc_id IS NULL
);
*/

ROLLBACK; -- Change to COMMIT if you want to apply changes

-- =============================================================================
-- DUPLICATE DETECTION AND CLEANUP
-- =============================================================================

-- Find duplicate chunks (shouldn't exist due to unique constraint)
SELECT 
    doc_id,
    chunk_num,
    COUNT(*) AS duplicate_count,
    ARRAY_AGG(id) AS chunk_ids
FROM docs_chunks
GROUP BY doc_id, chunk_num
HAVING COUNT(*) > 1;

-- If duplicates found, keep the most recent one
-- This query shows what would be deleted
/*
WITH ranked_chunks AS (
    SELECT 
        id,
        doc_id,
        chunk_num,
        ROW_NUMBER() OVER (PARTITION BY doc_id, chunk_num ORDER BY created_at DESC) AS rn
    FROM docs_chunks
)
SELECT id, doc_id, chunk_num
FROM ranked_chunks
WHERE rn > 1;
*/

-- Delete duplicates (keep newest)
/*
DELETE FROM docs_chunks
WHERE id IN (
    SELECT id
    FROM (
        SELECT 
            id,
            ROW_NUMBER() OVER (PARTITION BY doc_id, chunk_num ORDER BY created_at DESC) AS rn
        FROM docs_chunks
    ) t
    WHERE rn > 1
);
*/

-- =============================================================================
-- STATISTICS REPORTING
-- =============================================================================

-- Generate maintenance report
WITH stats AS (
    SELECT 
        COUNT(*) AS total_chunks,
        COUNT(DISTINCT doc_id) AS total_docs,
        pg_size_pretty(pg_total_relation_size('docs_chunks')) AS table_size,
        pg_size_pretty(pg_relation_size('docs_chunks')) AS data_size,
        pg_size_pretty(pg_total_relation_size('docs_chunks') - pg_relation_size('docs_chunks')) AS index_size
    FROM docs_chunks
),
bloat AS (
    SELECT 
        n_live_tup AS live_rows,
        n_dead_tup AS dead_rows,
        ROUND(100.0 * n_dead_tup / NULLIF(n_live_tup + n_dead_tup, 0), 2) AS dead_pct
    FROM pg_stat_user_tables
    WHERE tablename = 'docs_chunks'
),
index_usage AS (
    SELECT 
        idx_scan AS scans,
        idx_tup_read AS tuples_read
    FROM pg_stat_user_indexes
    WHERE indexrelname = 'docs_chunks_embedding_idx'
)
SELECT 
    'Maintenance Report' AS section,
    json_build_object(
        'total_chunks', s.total_chunks,
        'total_documents', s.total_docs,
        'table_size', s.table_size,
        'data_size', s.data_size,
        'index_size', s.index_size,
        'live_rows', b.live_rows,
        'dead_rows', b.dead_rows,
        'dead_row_pct', b.dead_pct,
        'index_scans', i.scans,
        'tuples_read', i.tuples_read
    ) AS details
FROM stats s, bloat b, index_usage i;

-- =============================================================================
-- PERFORMANCE TUNING
-- =============================================================================

-- Check current ivfflat.probes setting
SHOW ivfflat.probes;

-- Adjust probes for query-time accuracy/speed tradeoff
-- Higher = more accurate but slower, lower = faster but may miss results
-- SET ivfflat.probes = 10;  -- Default, good balance
-- SET ivfflat.probes = 20;  -- Higher accuracy
-- SET ivfflat.probes = 5;   -- Faster queries

-- Set at session level for testing
-- SET ivfflat.probes = 15;

-- Set globally (requires superuser)
-- ALTER DATABASE your_database SET ivfflat.probes = 10;

-- =============================================================================
-- HEALTH CHECK SCRIPT
-- =============================================================================

-- Quick health check (run anytime)
DO $$
DECLARE
    chunk_count INT;
    dead_pct NUMERIC;
    index_scans BIGINT;
BEGIN
    -- Check chunk count
    SELECT COUNT(*) INTO chunk_count FROM docs_chunks;
    
    -- Check table bloat
    SELECT ROUND(100.0 * n_dead_tup / NULLIF(n_live_tup + n_dead_tup, 0), 2)
    INTO dead_pct
    FROM pg_stat_user_tables
    WHERE tablename = 'docs_chunks';
    
    -- Check index usage
    SELECT idx_scan INTO index_scans
    FROM pg_stat_user_indexes
    WHERE indexrelname = 'docs_chunks_embedding_idx';
    
    RAISE NOTICE '=== Health Check ===';
    RAISE NOTICE 'Total chunks: %', chunk_count;
    RAISE NOTICE 'Dead row percentage: %', COALESCE(dead_pct, 0);
    RAISE NOTICE 'Vector index scans: %', COALESCE(index_scans, 0);
    
    -- Recommendations
    IF dead_pct > 20 THEN
        RAISE NOTICE 'RECOMMENDATION: Run VACUUM ANALYZE (dead rows > 20%%)';
    END IF;
    
    IF index_scans IS NULL OR index_scans = 0 THEN
        RAISE NOTICE 'WARNING: Vector index has never been used!';
    END IF;
    
    RAISE NOTICE '===================';
END $$;
