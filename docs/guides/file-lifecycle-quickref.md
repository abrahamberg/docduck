# File Lifecycle - Quick Reference

## How Files Are Tracked

```
┌─────────────────────────────────────────────────────────────┐
│                    INDEXING RUN STARTS                       │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│  Step 1: List All Documents from Provider                   │
│  • OneDrive → Graph API call                                │
│  • S3 → ListObjectsV2 API                                   │
│  • Local → Filesystem scan                                  │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│  Step 2: Compare with Database (docs_files table)           │
│  • Find NEW files (in provider, not in DB)                  │
│  • Find UPDATED files (ETag changed)                        │
│  • Find ORPHANED files (in DB, not in provider)             │
└─────────────────────────────────────────────────────────────┘
                              ↓
                    ┌─────────┴─────────┐
                    ↓                   ↓
        ┌───────────────────┐  ┌─────────────────────┐
        │   NEW FILES       │  │  UPDATED FILES      │
        │                   │  │                     │
        │ 1. Download       │  │ 1. Download         │
        │ 2. Extract text   │  │ 2. Extract text     │
        │ 3. Chunk          │  │ 3. Chunk            │
        │ 4. Embed          │  │ 4. Embed            │
        │ 5. INSERT chunks  │  │ 5. UPSERT chunks    │
        │ 6. Track in DB    │  │ 6. Update tracking  │
        └───────────────────┘  └─────────────────────┘
                    ↓                   ↓
        ┌───────────────────────────────────────┐
        │   UNCHANGED FILES (same ETag)         │
        │   → SKIP (logged, not processed)      │
        └───────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│  Step 3: Cleanup Orphaned Documents                         │
│  (if CleanupOrphanedDocuments: true)                        │
│                                                              │
│  DELETE FROM docs_chunks WHERE doc_id IN (orphans)          │
│  DELETE FROM docs_files WHERE doc_id IN (orphans)           │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│  Step 4: Update Provider Sync Timestamp                     │
│  UPDATE providers SET last_sync_at = now()                  │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│                    INDEXING RUN COMPLETE                     │
│  Logged: processed count, skipped count, chunks count       │
└─────────────────────────────────────────────────────────────┘
```

---

## Change Detection Matrix

| Provider State | Database State | ETag Match? | Action |
|----------------|----------------|-------------|--------|
| ✅ Exists | ❌ Not found | N/A | **INDEX** (new file) |
| ✅ Exists | ✅ Exists | ✅ Same | **SKIP** (unchanged) |
| ✅ Exists | ✅ Exists | ❌ Different | **RE-INDEX** (updated) |
| ❌ Missing | ✅ Exists | N/A | **DELETE** (orphaned)* |

\* Only if `CleanupOrphanedDocuments: true`

---

## Configuration Impact

### Scenario 1: Normal Operation (Recommended)

```yaml
Chunking:
  CleanupOrphanedDocuments: true
  ForceFullReindex: false
```

**Behavior:**
- ✅ Skip unchanged files (fast)
- ✅ Update modified files
- ✅ Index new files
- ✅ Remove deleted files
- **Result:** Incremental sync, database stays current

---

### Scenario 2: Historical Archive

```yaml
Chunking:
  CleanupOrphanedDocuments: false  # Keep everything
  ForceFullReindex: false
```

**Behavior:**
- ✅ Skip unchanged files
- ✅ Update modified files
- ✅ Index new files
- ❌ Keep deleted file data
- **Result:** Database accumulates all historical data

---

### Scenario 3: Force Complete Refresh

```yaml
Chunking:
  CleanupOrphanedDocuments: true
  ForceFullReindex: true  # Temporary setting!
```

**Behavior:**
1. **DELETE** all existing data for provider
2. **INDEX** all files (ignore ETags)
3. Generate fresh embeddings
- **Result:** Clean slate, useful for testing or schema changes

⚠️ **Warning:** Set `ForceFullReindex: false` after completion!

---

## Example Log Output

### Incremental Run (Files Already Indexed)

```
INFO: Starting multi-provider indexer pipeline
INFO: Found 1 enabled provider(s): local/LocalFiles
INFO: Processing provider: local/LocalFiles
INFO: Processing 15 documents from local/LocalFiles (total available: 15)
INFO: No orphaned documents found for local/LocalFiles
INFO: Skipping unchanged file: report.docx from LocalFiles (ETag: sha256:abc123...)
INFO: Skipping unchanged file: memo.docx from LocalFiles (ETag: sha256:def456...)
INFO: Processing file: new-doc.docx from LocalFiles
INFO: Processed new-doc.docx from LocalFiles: 12 chunks in 3.45s
INFO: Indexing complete. Providers: 1, Processed: 1, Skipped: 14, Total chunks: 12, Elapsed: 4.12s
```

### With Deleted Files

```
INFO: Processing provider: local/LocalFiles
INFO: Processing 13 documents from local/LocalFiles (total available: 13)
INFO: Found 2 orphaned document(s) for local/LocalFiles. Cleaning up...
INFO: Deleted orphaned document: old-report.docx (DocId: local_abc123)
INFO: Deleted orphaned document: archived.docx (DocId: local_def456)
INFO: Cleanup complete for local/LocalFiles: 2 documents removed, 24 chunks deleted
INFO: Skipping unchanged file: current-doc.docx from LocalFiles
INFO: Indexing complete. Providers: 1, Processed: 0, Skipped: 13, Total chunks: 0, Elapsed: 1.23s
```

### Force Full Reindex

```
WARN: Force full reindex enabled. Deleting all existing data for local/LocalFiles
INFO: Deleted all documents for local/LocalFiles: 15 documents, 180 chunks
INFO: Processing provider: local/LocalFiles
INFO: Processing 15 documents from local/LocalFiles (total available: 15)
INFO: Processing file: report.docx from LocalFiles
INFO: Processing file: memo.docx from LocalFiles
...
INFO: Indexing complete. Providers: 1, Processed: 15, Skipped: 0, Total chunks: 180, Elapsed: 45.67s
```

---

## Database Tables

### docs_files (File Tracking)

| Column | Purpose |
|--------|---------|
| `doc_id` | Unique ID (stable across renames for OneDrive/S3) |
| `provider_type` | e.g., "local", "onedrive", "s3" |
| `provider_name` | e.g., "LocalFiles", "OneDrive", "S3" |
| `filename` | Current filename |
| `etag` | Change detection hash/version |
| `last_modified` | Timestamp from provider |
| `relative_path` | Path within provider |

**Primary Key:** `(doc_id, provider_type, provider_name)`

### docs_chunks (Vector Data)

| Column | Purpose |
|--------|---------|
| `id` | Auto-increment |
| `doc_id` | Links to source document |
| `provider_type` | Source provider type |
| `provider_name` | Source provider name |
| `chunk_num` | Chunk sequence (0, 1, 2...) |
| `text` | Chunk text content |
| `embedding` | Vector (1536 dimensions) |
| `metadata` | JSONB with additional info |

**Unique Constraint:** `(doc_id, chunk_num)` → Enables upsert on updates

---

## Troubleshooting

### Files not re-indexing despite changes

**Check ETag values:**
```sql
SELECT doc_id, filename, etag, last_modified 
FROM docs_files 
WHERE provider_type = 'local' 
ORDER BY last_modified DESC;
```

**Force reindex for testing:**
```yaml
Chunking:
  ForceFullReindex: true  # Temporary!
```

### Orphaned data accumulating

**Check orphan count:**
```sql
SELECT df.doc_id, df.filename, df.last_modified
FROM docs_files df
WHERE NOT EXISTS (
  -- This is conceptual; actual cleanup is done by indexer
  SELECT 1 FROM current_provider_docs WHERE doc_id = df.doc_id
);
```

**Enable cleanup:**
```yaml
Chunking:
  CleanupOrphanedDocuments: true
```

### Reset specific provider

**SQL cleanup:**
```sql
DELETE FROM docs_chunks WHERE provider_type = 'local' AND provider_name = 'LocalFiles';
DELETE FROM docs_files WHERE provider_type = 'local' AND provider_name = 'LocalFiles';
```

Or use `ForceFullReindex: true` for next run.

---

## Best Practices

✅ **Use incremental indexing** (default) for efficiency  
✅ **Enable cleanup** to prevent database bloat  
✅ **Monitor logs** for unexpected orphan counts (may indicate provider issues)  
✅ **Stable document IDs** preferred (OneDrive/S3 have stable IDs, local uses path hash)  
✅ **Set `ForceFullReindex: false`** after any forced reindex  
❌ **Don't disable cleanup** in production unless you need historical data  

---

For detailed information, see [File Lifecycle Management](file-lifecycle.md).
