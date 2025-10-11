# PostgreSQL + pgvector Quick Reference

## 🚀 Getting Started

```bash
# 1. Test connection
./db-util.sh test

# 2. Initialize schema
./db-util.sh init

# 3. Verify pgvector extension
./db-util.sh check
```

## 📊 Monitoring Commands

```bash
# View statistics
./db-util.sh stats

# Show recently indexed documents
./db-util.sh recent

# Run health check
./db-util.sh health
```

## 🔧 Maintenance Commands

```bash
# Run VACUUM and ANALYZE
./db-util.sh maintain

# Create backup
./db-util.sh backup

# Open PostgreSQL shell
./db-util.sh shell
```

## 🔍 Common SQL Queries

### View All Documents
```sql
SELECT 
    filename,
    COUNT(*) AS chunk_count,
    MAX(created_at) AS last_indexed
FROM docs_chunks
GROUP BY filename
ORDER BY last_indexed DESC;
```

### Search Similar Chunks
```sql
-- Replace [...] with actual embedding vector
SELECT 
    filename,
    chunk_num,
    text,
    embedding <=> '[...]'::vector AS distance
FROM docs_chunks
ORDER BY embedding <=> '[...]'::vector
LIMIT 10;
```

### Check Index Health
```sql
SELECT 
    indexname,
    pg_size_pretty(pg_relation_size(indexrelid)) AS size,
    idx_scan AS scans
FROM pg_stat_user_indexes
WHERE tablename = 'docs_chunks';
```

### Find Chunks with Missing Embeddings
```sql
SELECT doc_id, filename, chunk_num
FROM docs_chunks
WHERE embedding IS NULL;
```

## 🎯 Performance Tuning

### Adjust Query Accuracy
```sql
-- Default (good balance)
SET ivfflat.probes = 10;

-- Higher accuracy (slower)
SET ivfflat.probes = 20;

-- Faster queries (may miss results)
SET ivfflat.probes = 5;
```

### Rebuild Vector Index
```sql
-- Calculate optimal lists parameter
SELECT GREATEST(10, ROUND(SQRT(COUNT(*)))) AS recommended_lists
FROM docs_chunks;

-- Drop and recreate with new parameter
DROP INDEX CONCURRENTLY docs_chunks_embedding_idx;
CREATE INDEX docs_chunks_embedding_idx 
ON docs_chunks USING ivfflat (embedding vector_cosine_ops) 
WITH (lists = 316);  -- Use calculated value
```

## 🧹 Cleanup Tasks

### Remove Old Documents
```sql
-- Preview documents older than 1 year
SELECT doc_id, filename, last_modified
FROM docs_files
WHERE last_modified < NOW() - INTERVAL '1 year';

-- Delete (DESTRUCTIVE!)
BEGIN;
DELETE FROM docs_chunks
WHERE doc_id IN (
    SELECT doc_id FROM docs_files
    WHERE last_modified < NOW() - INTERVAL '1 year'
);
DELETE FROM docs_files
WHERE last_modified < NOW() - INTERVAL '1 year';
COMMIT;
```

### Remove Orphaned Chunks
```sql
-- Find orphans (chunks without file tracking)
SELECT c.doc_id, c.filename, COUNT(*) AS orphan_count
FROM docs_chunks c
LEFT JOIN docs_files f ON c.doc_id = f.doc_id
WHERE f.doc_id IS NULL
GROUP BY c.doc_id, c.filename;

-- Delete orphans
DELETE FROM docs_chunks
WHERE doc_id IN (
    SELECT c.doc_id FROM docs_chunks c
    LEFT JOIN docs_files f ON c.doc_id = f.doc_id
    WHERE f.doc_id IS NULL
);
```

## 📈 Monitoring Queries

### Database Size
```sql
SELECT 
    pg_size_pretty(pg_database_size(current_database())) AS db_size,
    pg_size_pretty(pg_total_relation_size('docs_chunks')) AS table_size;
```

### Table Bloat
```sql
SELECT 
    n_live_tup AS live_rows,
    n_dead_tup AS dead_rows,
    ROUND(100 * n_dead_tup::NUMERIC / NULLIF(n_live_tup + n_dead_tup, 0), 2) AS dead_pct
FROM pg_stat_user_tables
WHERE tablename = 'docs_chunks';
```

### Index Usage
```sql
SELECT 
    indexrelname AS index_name,
    idx_scan AS scans,
    idx_tup_read AS tuples_read,
    idx_tup_fetch AS tuples_fetched
FROM pg_stat_user_indexes
WHERE tablename = 'docs_chunks'
ORDER BY idx_scan DESC;
```

## 🔧 Connection String Format

```bash
# Standard format
Host=hostname;Port=5432;Database=dbname;Username=user;Password=pass;MinPoolSize=1;MaxPoolSize=3

# For indexer (conservative)
MinPoolSize=1;MaxPoolSize=3

# For API server (higher throughput)
MinPoolSize=5;MaxPoolSize=20
```

## 🚨 Troubleshooting

### "Extension vector does not exist"
```sql
-- Run as superuser
CREATE EXTENSION vector;
```

### "Dimension mismatch"
```sql
-- Check current dimension
\d docs_chunks

-- Alter if needed (DESTRUCTIVE - rebuilds index!)
ALTER TABLE docs_chunks ALTER COLUMN embedding TYPE vector(3072);
REINDEX INDEX docs_chunks_embedding_idx;
```

### Slow Queries
```bash
# 1. Check if index is being used
EXPLAIN SELECT * FROM docs_chunks ORDER BY embedding <=> '[...]'::vector LIMIT 10;

# 2. Rebuild index if needed
./db-util.sh maintain
psql -c "REINDEX INDEX CONCURRENTLY docs_chunks_embedding_idx;"

# 3. Increase probes for better recall
psql -c "SET ivfflat.probes = 20;"
```

## 📚 More Information

- **Full Documentation**: See [PGVECTOR.md](PGVECTOR.md)
- **SQL Examples**: See [sql/02-queries.sql](sql/02-queries.sql)
- **Maintenance**: See [sql/03-maintenance.sql](sql/03-maintenance.sql)
- **Schema**: See [sql/01-init-schema.sql](sql/01-init-schema.sql)

## 🎯 Quick Health Check

```bash
# All-in-one health check
./db-util.sh health

# Or manually:
psql -c "
SELECT 
    COUNT(*) AS chunks,
    COUNT(DISTINCT doc_id) AS docs,
    pg_size_pretty(pg_total_relation_size('docs_chunks')) AS size
FROM docs_chunks;
"
```

## ⚡ Best Practices

1. **Run VACUUM ANALYZE weekly** - `./db-util.sh maintain`
2. **Backup before major changes** - `./db-util.sh backup`
3. **Monitor dead rows** - Should be < 20%
4. **Rebuild index after 10%+ data growth** - See maintenance SQL
5. **Test queries with EXPLAIN ANALYZE** - Ensure index usage
6. **Use appropriate probes setting** - Balance accuracy vs speed
7. **Check logs regularly** - Monitor indexing errors
8. **Set proper connection pool limits** - Avoid exhausting connections

---

*For detailed examples and advanced usage, see the full documentation in the `sql/` directory.*
