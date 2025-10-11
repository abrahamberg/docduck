# PostgreSQL + pgvector Implementation Guide

This document provides detailed information about the Postgres + pgvector storage layer in DocDuck.

## Overview

DocDuck uses **PostgreSQL with the pgvector extension** to store document chunks alongside their vector embeddings. This enables efficient similarity search for semantic document retrieval.

## Architecture

### Tables

#### `docs_chunks`
Stores individual text chunks with their embeddings and metadata.

```sql
CREATE TABLE docs_chunks (
    id BIGSERIAL PRIMARY KEY,              -- Auto-incrementing chunk ID
    doc_id TEXT NOT NULL,                  -- Source document identifier (OneDrive item ID)
    filename TEXT NOT NULL,                -- Original filename
    chunk_num INT NOT NULL,                -- Chunk sequence number within document
    text TEXT NOT NULL,                    -- Raw text content
    metadata JSONB,                        -- Flexible metadata (offsets, etag, etc.)
    embedding vector(1536),                -- OpenAI text-embedding-3-small vector
    created_at TIMESTAMPTZ DEFAULT now(),  -- Insertion timestamp
    CONSTRAINT unique_doc_chunk UNIQUE (doc_id, chunk_num)
);
```

**Key features:**
- **Unique constraint** on `(doc_id, chunk_num)` ensures idempotent upserts
- **JSONB metadata** stores flexible information (char_start, char_end, etag, last_modified)
- **Vector dimension** 1536 matches OpenAI's `text-embedding-3-small` model

#### `docs_files`
Tracks file state for delta indexing.

```sql
CREATE TABLE docs_files (
    doc_id TEXT PRIMARY KEY,               -- OneDrive item ID
    filename TEXT NOT NULL,                -- Current filename
    etag TEXT NOT NULL,                    -- OneDrive ETag for change detection
    last_modified TIMESTAMPTZ NOT NULL     -- Last modification timestamp
);
```

**Purpose:**
- Skip unchanged files during re-indexing
- Track document renames
- Enable incremental updates

### Indexes

#### Vector Similarity Index (IVFFlat)
```sql
CREATE INDEX docs_chunks_embedding_idx 
ON docs_chunks USING ivfflat (embedding vector_cosine_ops) 
WITH (lists = 100);
```

**Configuration:**
- **Algorithm**: IVFFlat (Inverted File with Flat Compression)
- **Distance metric**: Cosine similarity (`vector_cosine_ops`)
- **Lists parameter**: 100 (recommended: rows/1000, minimum 10)

**When to rebuild:**
- After indexing significant new documents (>10% growth)
- When query performance degrades
- Example: `REINDEX INDEX docs_chunks_embedding_idx;`

#### Supporting Indexes
```sql
CREATE INDEX docs_chunks_doc_id_idx ON docs_chunks(doc_id);
CREATE INDEX docs_chunks_filename_idx ON docs_chunks(filename);
```

These improve filtering and aggregation queries.

## Vector Operations

### Distance Metrics

pgvector supports three distance operators:

| Operator | Metric | Use Case |
|----------|--------|----------|
| `<->` | L2 (Euclidean) | General similarity, normalized vectors |
| `<#>` | Inner Product | Pre-normalized embeddings, faster |
| `<=>` | Cosine Distance | Default for OpenAI embeddings |

**DocDuck uses cosine distance** (`<=>`) which is standard for OpenAI embeddings.

### Query Examples

#### Basic Similarity Search
```sql
-- Find top 5 most similar chunks to a query embedding
SELECT 
    id, 
    filename, 
    chunk_num, 
    text,
    embedding <=> $1::vector AS distance
FROM docs_chunks
ORDER BY embedding <=> $1::vector
LIMIT 5;
```

#### Filtered Similarity Search
```sql
-- Search within specific documents
SELECT 
    id, 
    filename, 
    chunk_num, 
    text,
    embedding <=> $1::vector AS distance
FROM docs_chunks
WHERE filename ILIKE '%quarterly%'
ORDER BY embedding <=> $1::vector
LIMIT 10;
```

#### Metadata-based Filtering
```sql
-- Search only recent chunks
SELECT 
    id, 
    filename, 
    text,
    metadata->>'etag' AS etag,
    embedding <=> $1::vector AS distance
FROM docs_chunks
WHERE (metadata->>'last_modified')::timestamptz > NOW() - INTERVAL '30 days'
ORDER BY embedding <=> $1::vector
LIMIT 5;
```

## Performance Tuning

### Index Parameters

#### Lists (IVFFlat)
The `lists` parameter controls the speed/accuracy tradeoff:

```sql
-- For small datasets (< 100K rows)
CREATE INDEX ... WITH (lists = 100);

-- For medium datasets (100K - 1M rows)
CREATE INDEX ... WITH (lists = 1000);

-- For large datasets (> 1M rows)
CREATE INDEX ... WITH (lists = SQRT(row_count));
```

**Rule of thumb**: `lists ≈ rows / 1000`, with minimum 10

#### Probes (Query-time)
Control how many lists to scan during queries:

```sql
-- Higher probes = better recall, slower queries
SET ivfflat.probes = 10;  -- Default: good balance

-- For highest accuracy (slower)
SET ivfflat.probes = 20;

-- For fastest queries (may miss results)
SET ivfflat.probes = 1;
```

### Connection Pooling

Optimize Npgsql connection string:

```
Host=localhost;Database=vectors;Username=user;Password=pass;
MinPoolSize=1;MaxPoolSize=3;
Command Timeout=30;
Pooling=true;
```

**For indexer jobs:**
- `MinPoolSize=1` (conserve resources)
- `MaxPoolSize=3` (limit concurrent connections)

**For API servers:**
- `MinPoolSize=5` (reduce connection latency)
- `MaxPoolSize=20` (handle concurrent queries)

### Query Optimization

#### Use EXPLAIN ANALYZE
```sql
EXPLAIN ANALYZE
SELECT id, filename, text
FROM docs_chunks
ORDER BY embedding <=> '[0.1, 0.2, ...]'::vector
LIMIT 5;
```

Look for:
- `Index Scan using docs_chunks_embedding_idx` ✅
- `Seq Scan on docs_chunks` ❌ (rebuild index!)

#### Pre-compute Filters
```sql
-- Create materialized view for filtered searches
CREATE MATERIALIZED VIEW recent_chunks AS
SELECT * FROM docs_chunks
WHERE created_at > NOW() - INTERVAL '90 days';

CREATE INDEX recent_chunks_embedding_idx 
ON recent_chunks USING ivfflat (embedding vector_cosine_ops) 
WITH (lists = 50);
```

## Maintenance

### Regular Tasks

#### Vacuum and Analyze
```sql
-- After bulk inserts/deletes
VACUUM ANALYZE docs_chunks;
VACUUM ANALYZE docs_files;

-- Full vacuum (reclaim disk space)
VACUUM FULL docs_chunks;
```

#### Index Rebuilding
```sql
-- After significant data growth
REINDEX INDEX CONCURRENTLY docs_chunks_embedding_idx;
```

#### Statistics Update
```sql
-- Keep query planner informed
ANALYZE docs_chunks;
```

### Monitoring Queries

#### Index Health
```sql
SELECT 
    schemaname,
    tablename,
    indexname,
    pg_size_pretty(pg_relation_size(indexrelid)) AS index_size
FROM pg_stat_user_indexes
WHERE tablename = 'docs_chunks';
```

#### Table Statistics
```sql
SELECT 
    COUNT(*) AS total_chunks,
    COUNT(DISTINCT doc_id) AS total_documents,
    AVG(LENGTH(text)) AS avg_chunk_length,
    pg_size_pretty(pg_total_relation_size('docs_chunks')) AS total_size
FROM docs_chunks;
```

#### Storage Distribution
```sql
SELECT 
    filename,
    COUNT(*) AS chunk_count,
    AVG(LENGTH(text)) AS avg_length,
    MIN(created_at) AS first_indexed,
    MAX(created_at) AS last_indexed
FROM docs_chunks
GROUP BY filename
ORDER BY chunk_count DESC;
```

## Data Management

### Cleanup Old Documents
```sql
-- Remove chunks for deleted files
DELETE FROM docs_chunks
WHERE doc_id IN (
    SELECT doc_id FROM docs_files
    WHERE last_modified < NOW() - INTERVAL '1 year'
);

-- Clean up tracking table
DELETE FROM docs_files
WHERE last_modified < NOW() - INTERVAL '1 year';
```

### Reset Everything
```sql
-- Start fresh (destructive!)
TRUNCATE docs_chunks RESTART IDENTITY CASCADE;
TRUNCATE docs_files RESTART IDENTITY CASCADE;
VACUUM FULL docs_chunks;
VACUUM FULL docs_files;
```

### Backup and Restore
```bash
# Backup
pg_dump -h localhost -U postgres -d vectors \
    -t docs_chunks -t docs_files \
    --no-owner --no-acl -f docduck_backup.sql

# Restore
psql -h localhost -U postgres -d vectors < docduck_backup.sql
```

## Troubleshooting

### Slow Queries

**Symptom**: Queries take >1 second

**Solutions:**
1. Check if index is being used: `EXPLAIN SELECT ... ORDER BY embedding <=> ...`
2. Increase `ivfflat.probes` if recall is too low
3. Rebuild index if data has grown: `REINDEX INDEX docs_chunks_embedding_idx`
4. Update statistics: `ANALYZE docs_chunks`

### Out of Memory

**Symptom**: Connection errors or crashes during indexing

**Solutions:**
1. Reduce `MaxPoolSize` in connection string
2. Batch inserts (already implemented in VectorRepository)
3. Increase PostgreSQL `work_mem` setting
4. Add `max_parallel_workers` limit in postgres.conf

### Vector Dimension Mismatch

**Symptom**: `ERROR: dimension of vector does not match`

**Solutions:**
1. Verify embedding model matches table definition (1536 for text-embedding-3-small)
2. If using different model, alter table:
   ```sql
   ALTER TABLE docs_chunks ALTER COLUMN embedding TYPE vector(3072);
   REINDEX INDEX docs_chunks_embedding_idx;
   ```

### Poor Recall

**Symptom**: Relevant documents not appearing in top results

**Solutions:**
1. Increase `ivfflat.probes`: `SET ivfflat.probes = 20`
2. Rebuild index with more lists
3. Use exact nearest-neighbor search for comparison:
   ```sql
   -- Disable index temporarily
   SET enable_indexscan = off;
   -- Run query
   -- Re-enable
   SET enable_indexscan = on;
   ```

## Advanced Features

### Multi-vector Search (Hybrid)

Combine vector similarity with full-text search:

```sql
-- Create GIN index for full-text
CREATE INDEX docs_chunks_text_idx ON docs_chunks USING GIN (to_tsvector('english', text));

-- Hybrid query
WITH vector_results AS (
    SELECT id, embedding <=> $1::vector AS vec_score
    FROM docs_chunks
    ORDER BY embedding <=> $1::vector
    LIMIT 100
),
text_results AS (
    SELECT id, ts_rank(to_tsvector('english', text), query) AS text_score
    FROM docs_chunks, plainto_tsquery('english', $2) query
    WHERE to_tsvector('english', text) @@ query
)
SELECT 
    c.id,
    c.filename,
    c.text,
    COALESCE(v.vec_score, 999) AS vec_score,
    COALESCE(t.text_score, 0) AS text_score,
    (COALESCE(t.text_score, 0) * 0.3 + (1 - COALESCE(v.vec_score, 999)) * 0.7) AS combined_score
FROM docs_chunks c
LEFT JOIN vector_results v ON v.id = c.id
LEFT JOIN text_results t ON t.id = c.id
WHERE v.id IS NOT NULL OR t.id IS NOT NULL
ORDER BY combined_score DESC
LIMIT 10;
```

### Document Clustering

Find related documents using vector similarity:

```sql
-- Find documents similar to a specific document
WITH doc_embedding AS (
    SELECT AVG(embedding) AS avg_vec
    FROM docs_chunks
    WHERE doc_id = $1
)
SELECT 
    c.doc_id,
    c.filename,
    AVG(c.embedding <=> d.avg_vec) AS avg_distance,
    COUNT(*) AS chunk_count
FROM docs_chunks c, doc_embedding d
WHERE c.doc_id != $1
GROUP BY c.doc_id, c.filename
ORDER BY avg_distance
LIMIT 10;
```

## References

- [pgvector Documentation](https://github.com/pgvector/pgvector)
- [PostgreSQL JSONB](https://www.postgresql.org/docs/current/datatype-json.html)
- [OpenAI Embeddings Guide](https://platform.openai.com/docs/guides/embeddings)
- [IVFFlat Index Tuning](https://github.com/pgvector/pgvector#ivfflat)

## See Also

- [sql/01-init-schema.sql](../../sql/01-init-schema.sql) - Database initialization script
- [Indexer/Services/VectorRepository.cs](../../Indexer/Services/VectorRepository.cs) - C# implementation
- [Quick Start](../guides/quickstart.md) - Getting started guide
