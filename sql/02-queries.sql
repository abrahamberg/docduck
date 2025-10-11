-- Query library for analyzing and searching vector data
-- Use these queries for monitoring, debugging, and analysis

-- =============================================================================
-- BASIC STATISTICS
-- =============================================================================

-- Overall database statistics
SELECT 
    COUNT(*) AS total_chunks,
    COUNT(DISTINCT doc_id) AS total_documents,
    COUNT(DISTINCT filename) AS unique_filenames,
    AVG(LENGTH(text)) AS avg_chunk_length,
    MIN(created_at) AS first_indexed,
    MAX(created_at) AS last_indexed,
    pg_size_pretty(pg_total_relation_size('docs_chunks')) AS total_size
FROM docs_chunks;

-- Document distribution
SELECT 
    filename,
    COUNT(*) AS chunk_count,
    AVG(LENGTH(text))::INT AS avg_chunk_length,
    MIN(chunk_num) AS min_chunk,
    MAX(chunk_num) AS max_chunk,
    MIN(created_at) AS first_indexed,
    MAX(created_at) AS last_indexed
FROM docs_chunks
GROUP BY filename
ORDER BY chunk_count DESC;

-- Recent indexing activity (last 7 days)
SELECT 
    DATE(created_at) AS date,
    COUNT(*) AS chunks_indexed,
    COUNT(DISTINCT doc_id) AS documents_indexed
FROM docs_chunks
WHERE created_at > NOW() - INTERVAL '7 days'
GROUP BY DATE(created_at)
ORDER BY date DESC;

-- =============================================================================
-- INDEX HEALTH AND PERFORMANCE
-- =============================================================================

-- Check index sizes
SELECT 
    schemaname,
    tablename,
    indexname,
    pg_size_pretty(pg_relation_size(indexrelid)) AS index_size,
    idx_scan AS index_scans,
    idx_tup_read AS tuples_read,
    idx_tup_fetch AS tuples_fetched
FROM pg_stat_user_indexes
WHERE tablename IN ('docs_chunks', 'docs_files')
ORDER BY pg_relation_size(indexrelid) DESC;

-- Table bloat check (approximate)
SELECT 
    schemaname,
    tablename,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) AS total_size,
    pg_size_pretty(pg_relation_size(schemaname||'.'||tablename)) AS table_size,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename) - pg_relation_size(schemaname||'.'||tablename)) AS indexes_size,
    n_live_tup AS live_rows,
    n_dead_tup AS dead_rows,
    ROUND(100 * n_dead_tup / NULLIF(n_live_tup + n_dead_tup, 0), 2) AS dead_row_pct
FROM pg_stat_user_tables
WHERE tablename IN ('docs_chunks', 'docs_files');

-- =============================================================================
-- SIMILARITY SEARCH EXAMPLES
-- =============================================================================

-- Example: Find similar chunks (replace with actual embedding vector)
-- Generate query embedding externally, then:
/*
WITH query_embedding AS (
    SELECT '[0.1, 0.2, 0.3, ...]'::vector(1536) AS vec
)
SELECT 
    c.id,
    c.filename,
    c.chunk_num,
    LEFT(c.text, 100) AS text_preview,
    c.embedding <=> q.vec AS distance,
    1 - (c.embedding <=> q.vec) AS similarity_score
FROM docs_chunks c, query_embedding q
ORDER BY c.embedding <=> q.vec
LIMIT 10;
*/

-- Example: Search within specific documents
/*
WITH query_embedding AS (
    SELECT '[0.1, 0.2, 0.3, ...]'::vector(1536) AS vec
)
SELECT 
    c.id,
    c.filename,
    c.chunk_num,
    c.text,
    c.embedding <=> q.vec AS distance
FROM docs_chunks c, query_embedding q
WHERE c.filename ILIKE '%quarterly%'
ORDER BY c.embedding <=> q.vec
LIMIT 5;
*/

-- Example: Filter by metadata and search
/*
WITH query_embedding AS (
    SELECT '[0.1, 0.2, 0.3, ...]'::vector(1536) AS vec
)
SELECT 
    c.id,
    c.filename,
    c.metadata->>'etag' AS etag,
    (c.metadata->>'last_modified')::timestamptz AS last_modified,
    c.text,
    c.embedding <=> q.vec AS distance
FROM docs_chunks c, query_embedding q
WHERE (c.metadata->>'last_modified')::timestamptz > NOW() - INTERVAL '30 days'
ORDER BY c.embedding <=> q.vec
LIMIT 10;
*/

-- =============================================================================
-- DATA QUALITY CHECKS
-- =============================================================================

-- Find chunks with missing embeddings
SELECT 
    id,
    doc_id,
    filename,
    chunk_num
FROM docs_chunks
WHERE embedding IS NULL;

-- Find duplicate chunks (same doc_id and chunk_num shouldn't exist due to constraint)
SELECT 
    doc_id,
    chunk_num,
    COUNT(*) AS duplicate_count
FROM docs_chunks
GROUP BY doc_id, chunk_num
HAVING COUNT(*) > 1;

-- Find chunks with empty or very short text
SELECT 
    id,
    doc_id,
    filename,
    chunk_num,
    LENGTH(text) AS text_length,
    LEFT(text, 50) AS text_preview
FROM docs_chunks
WHERE LENGTH(text) < 50
ORDER BY text_length;

-- Find orphaned chunks (chunks without file tracking entry)
SELECT 
    c.doc_id,
    c.filename,
    COUNT(*) AS chunk_count
FROM docs_chunks c
LEFT JOIN docs_files f ON c.doc_id = f.doc_id
WHERE f.doc_id IS NULL
GROUP BY c.doc_id, c.filename;

-- =============================================================================
-- METADATA ANALYSIS
-- =============================================================================

-- Extract common metadata fields
SELECT 
    filename,
    metadata->>'etag' AS etag,
    (metadata->>'last_modified')::timestamptz AS last_modified,
    (metadata->>'char_start')::int AS char_start,
    (metadata->>'char_end')::int AS char_end
FROM docs_chunks
LIMIT 10;

-- Count chunks by metadata properties
SELECT 
    jsonb_object_keys(metadata) AS metadata_key,
    COUNT(*) AS occurrences
FROM docs_chunks
WHERE metadata IS NOT NULL
GROUP BY metadata_key
ORDER BY occurrences DESC;

-- =============================================================================
-- DOCUMENT EXPLORATION
-- =============================================================================

-- View all chunks for a specific document (ordered)
/*
SELECT 
    chunk_num,
    LENGTH(text) AS length,
    LEFT(text, 100) AS preview,
    created_at
FROM docs_chunks
WHERE doc_id = 'your-doc-id-here'
ORDER BY chunk_num;
*/

-- Find documents with the most chunks
SELECT 
    doc_id,
    filename,
    COUNT(*) AS chunk_count,
    SUM(LENGTH(text)) AS total_chars
FROM docs_chunks
GROUP BY doc_id, filename
ORDER BY chunk_count DESC
LIMIT 20;

-- Find documents with unusual chunk counts (potential extraction issues)
WITH chunk_stats AS (
    SELECT 
        AVG(chunk_count) AS avg_chunks,
        STDDEV(chunk_count) AS stddev_chunks
    FROM (
        SELECT COUNT(*) AS chunk_count
        FROM docs_chunks
        GROUP BY doc_id
    ) t
)
SELECT 
    c.doc_id,
    c.filename,
    COUNT(*) AS chunk_count,
    s.avg_chunks::INT AS avg_chunks,
    CASE 
        WHEN COUNT(*) < s.avg_chunks - 2 * s.stddev_chunks THEN 'Too few'
        WHEN COUNT(*) > s.avg_chunks + 2 * s.stddev_chunks THEN 'Too many'
        ELSE 'Normal'
    END AS status
FROM docs_chunks c, chunk_stats s
GROUP BY c.doc_id, c.filename, s.avg_chunks, s.stddev_chunks
HAVING COUNT(*) < s.avg_chunks - 2 * s.stddev_chunks 
    OR COUNT(*) > s.avg_chunks + 2 * s.stddev_chunks
ORDER BY chunk_count DESC;

-- =============================================================================
-- CLEANUP AND MAINTENANCE
-- =============================================================================

-- Find stale documents (not updated in last 6 months)
SELECT 
    doc_id,
    filename,
    etag,
    last_modified,
    NOW() - last_modified AS age
FROM docs_files
WHERE last_modified < NOW() - INTERVAL '6 months'
ORDER BY last_modified;

-- Count chunks to be deleted if removing old documents
SELECT COUNT(*) AS chunks_to_delete
FROM docs_chunks
WHERE doc_id IN (
    SELECT doc_id 
    FROM docs_files
    WHERE last_modified < NOW() - INTERVAL '1 year'
);

-- =============================================================================
-- PERFORMANCE TESTING
-- =============================================================================

-- Test index performance (requires actual embedding vector)
/*
EXPLAIN ANALYZE
SELECT id, filename, text
FROM docs_chunks
ORDER BY embedding <=> '[0.1, 0.2, ...]'::vector(1536)
LIMIT 10;
*/

-- Check query planner statistics
SELECT 
    tablename,
    attname AS column_name,
    n_distinct AS distinct_values,
    correlation
FROM pg_stats
WHERE tablename = 'docs_chunks'
    AND attname IN ('doc_id', 'filename', 'chunk_num');

-- =============================================================================
-- QUICK HEALTH CHECK
-- =============================================================================

-- Run this for a quick health overview
SELECT 
    'Total Documents' AS metric,
    COUNT(DISTINCT doc_id)::TEXT AS value
FROM docs_chunks
UNION ALL
SELECT 
    'Total Chunks',
    COUNT(*)::TEXT
FROM docs_chunks
UNION ALL
SELECT 
    'Avg Chunks/Doc',
    ROUND(COUNT(*)::NUMERIC / NULLIF(COUNT(DISTINCT doc_id), 0), 1)::TEXT
FROM docs_chunks
UNION ALL
SELECT 
    'Chunks Missing Embeddings',
    COUNT(*)::TEXT
FROM docs_chunks
WHERE embedding IS NULL
UNION ALL
SELECT 
    'Database Size',
    pg_size_pretty(pg_database_size(current_database()))
UNION ALL
SELECT 
    'Table Size',
    pg_size_pretty(pg_total_relation_size('docs_chunks'));
