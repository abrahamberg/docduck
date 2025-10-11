# File Lifecycle Management

How the DocDuck indexer handles file changes, updates, deletions, and renames.

## Overview

The indexer intelligently manages document vectors across the full file lifecycle:

- ✅ **New files** → Indexed with embeddings
- ✅ **Updated files** → Re-indexed (old chunks replaced)
- ✅ **Deleted files** → Vectors removed from database
- ✅ **Renamed files** → Treated as delete + add
- ✅ **Unchanged files** → Skipped (ETag-based detection)

---

## File Change Detection

### ETag-Based Change Detection

Each file is tracked with an **ETag** (entity tag) - a hash or version identifier:

**OneDrive:** Uses Microsoft Graph's `cTag` property  
**S3:** Uses S3 object `ETag` (MD5 hash)  
**Local:** Uses SHA256 hash of `{path}:{lastModified}:{size}`

#### How It Works

1. **First run:** File has no ETag in database → Indexed
2. **Subsequent runs:** Compare provider ETag with database ETag
   - **Same ETag** → File unchanged, skip processing
   - **Different ETag** → File modified, re-index

**Code:** `MultiProviderIndexerService.cs`

```csharp
if (!string.IsNullOrEmpty(doc.ETag) &&
    await _vectorRepository.IsDocumentIndexedAsync(
        doc.DocumentId, 
        doc.ETag, 
        provider.ProviderType, 
        provider.ProviderName, 
        ct))
{
    _logger.LogInformation("Skipping unchanged file: {Filename}", doc.Filename);
    skippedCount++;
    continue;
}
```

---

## File States & Handling

### 1. New Files ➕

**Scenario:** File exists in provider but not in database

**Action:**
1. Download document
2. Extract text
3. Chunk text
4. Generate embeddings
5. Insert into `docs_chunks` table
6. Track in `docs_files` table with ETag

**Result:** File indexed, searchable

---

### 2. Updated Files 🔄

**Scenario:** File exists in both, but ETag changed

**Action:**
1. Detect ETag mismatch (file modified)
2. Re-download document
3. Re-extract and chunk text
4. Generate new embeddings
5. **Upsert** chunks (replaces old chunks for same doc_id)
6. Update `docs_files` with new ETag and timestamp

**SQL:** Uses `ON CONFLICT (doc_id, chunk_num) DO UPDATE`

```sql
INSERT INTO docs_chunks (doc_id, filename, provider_type, provider_name, chunk_num, text, embedding, ...)
VALUES (...)
ON CONFLICT (doc_id, chunk_num)
DO UPDATE SET
    filename = EXCLUDED.filename,
    text = EXCLUDED.text,
    embedding = EXCLUDED.embedding,
    created_at = now()
```

**Result:** Old chunks replaced with fresh data, no duplicates

---

### 3. Deleted Files 🗑️

**Scenario:** File was in database but no longer exists in provider

**Action (if `CleanupOrphanedDocuments: true`):**
1. After listing all current documents, compare with database
2. Find orphaned doc_ids (in DB but not in provider list)
3. Delete all chunks for orphaned documents
4. Delete file tracking records

**Code:** `VectorRepository.CleanupOrphanedDocumentsAsync()`

```csharp
// Find docs in database but not in current provider list
SELECT doc_id, filename
FROM docs_files
WHERE provider_type = @provider_type
  AND provider_name = @provider_name
  AND doc_id != ALL(@current_doc_ids)
  
// Delete chunks and tracking records
DELETE FROM docs_chunks WHERE doc_id = @doc_id ...
DELETE FROM docs_files WHERE doc_id = @doc_id ...
```

**Configuration:**

```yaml
Chunking:
  CleanupOrphanedDocuments: true  # Enable automatic cleanup
```

**Result:** Database stays in sync with provider, no orphaned vectors

---

### 4. Renamed Files 📝

**Scenario:** File renamed/moved in provider

**Behavior depends on provider:**

**OneDrive/S3:** Document ID remains stable (uses file ID, not path)
- Old filename → Chunks update with new filename
- Treated as **update** (ETag changes)

**Local:** Document ID based on path hash
- Old path → Different doc_id → Treated as **delete + add**
- Old chunks deleted (cleanup), new chunks created

**Best Practice:** Use stable IDs when possible to preserve history

---

### 5. Unchanged Files ⏭️

**Scenario:** File exists, ETag matches database

**Action:**
- Skip download
- Skip processing
- Log as "skipped"

**Result:** Fast incremental indexing, no wasted API calls or compute

---

## Configuration Options

### Cleanup Behavior

```yaml
Chunking:
  # Automatic cleanup of deleted/moved files
  CleanupOrphanedDocuments: true  # Recommended: true
  
  # Force complete re-index (ignore ETags)
  ForceFullReindex: false  # Use only for troubleshooting
```

### Use Cases

| Option | `CleanupOrphanedDocuments` | `ForceFullReindex` | Use Case |
|--------|---------------------------|-------------------|----------|
| **Normal operation** | `true` | `false` | ✅ Default: incremental, with cleanup |
| **Preserve history** | `false` | `false` | Keep deleted file data (audit trail) |
| **Complete refresh** | `true` | `true` | Delete everything and re-index from scratch |
| **Test/dev** | `false` | `true` | Re-index without cleanup (testing) |

---

## Database Schema

### `docs_files` Table

Tracks file metadata for change detection:

```sql
CREATE TABLE docs_files (
    doc_id TEXT NOT NULL,
    provider_type TEXT NOT NULL,
    provider_name TEXT NOT NULL,
    filename TEXT NOT NULL,
    etag TEXT NOT NULL,              -- Change detection
    last_modified TIMESTAMPTZ NOT NULL,
    relative_path TEXT,
    PRIMARY KEY (doc_id, provider_type, provider_name)
);
```

### `docs_chunks` Table

Stores text chunks with embeddings:

```sql
CREATE TABLE docs_chunks (
    id BIGSERIAL PRIMARY KEY,
    doc_id TEXT NOT NULL,
    filename TEXT NOT NULL,
    provider_type TEXT NOT NULL,
    provider_name TEXT NOT NULL,
    chunk_num INT NOT NULL,
    text TEXT NOT NULL,
    embedding vector(1536),
    created_at TIMESTAMPTZ DEFAULT now(),
    CONSTRAINT unique_doc_chunk UNIQUE (doc_id, chunk_num)
);
```

**Key constraint:** `UNIQUE (doc_id, chunk_num)` enables upsert on update

---

## Lifecycle Examples

### Example 1: Daily Sync

**Day 1:**
- File A (new) → Indexed ✅
- File B (new) → Indexed ✅

**Day 2:**
- File A (unchanged, same ETag) → Skipped ⏭️
- File B (modified, new ETag) → Re-indexed 🔄
- File C (new) → Indexed ✅

**Day 3:**
- File A (deleted) → Cleanup removes vectors 🗑️
- File B (unchanged) → Skipped ⏭️
- File C (unchanged) → Skipped ⏭️

---

### Example 2: Force Full Reindex

**Scenario:** Changed chunking algorithm, need fresh embeddings

**Action:**

```yaml
Chunking:
  ForceFullReindex: true  # Temporary setting
```

**Result:**
1. All existing chunks deleted for each provider
2. All files re-indexed regardless of ETag
3. Fresh embeddings generated with new algorithm

**Important:** Set back to `false` after reindex completes

---

### Example 3: Historical Archive Mode

**Scenario:** Keep embeddings even if source files deleted (compliance)

**Configuration:**

```yaml
Chunking:
  CleanupOrphanedDocuments: false  # Preserve deleted file data
```

**Result:**
- Deleted files remain searchable
- Database grows over time (no cleanup)
- Useful for audit trails, compliance, legal holds

---

## Best Practices

### ✅ Recommended Settings

**Production:**
```yaml
Chunking:
  CleanupOrphanedDocuments: true   # Keep DB in sync
  ForceFullReindex: false          # Incremental only
```

**Development/Testing:**
```yaml
Chunking:
  CleanupOrphanedDocuments: false  # Keep data for testing
  ForceFullReindex: false          # Test incremental logic
  MaxFiles: 10                     # Limit scope
```

### Performance Optimization

**For large document sets:**

1. **Enable cleanup** (`CleanupOrphanedDocuments: true`)
   - Prevents unbounded database growth
   - Keeps indexes efficient

2. **Use incremental indexing** (`ForceFullReindex: false`)
   - Skip unchanged files
   - Reduces API calls (OneDrive, S3)
   - Faster runs

3. **Monitor orphaned documents**
   - Check logs for cleanup counts
   - Large cleanups may indicate provider issues

### Troubleshooting

**Problem: Files not updating despite changes**

Solution:
```bash
# Check ETag tracking
SELECT doc_id, filename, etag, last_modified 
FROM docs_files 
WHERE provider_type = 'local' AND provider_name = 'LocalFiles';

# Force reindex temporarily
```

**Problem: Orphaned data accumulating**

Solution:
```yaml
Chunking:
  CleanupOrphanedDocuments: true  # Ensure enabled
```

**Problem: Need to reset everything**

Solution:
```yaml
Chunking:
  ForceFullReindex: true  # One-time setting
```

Or manually:
```sql
-- Delete all data for a specific provider
DELETE FROM docs_chunks WHERE provider_type = 'local' AND provider_name = 'LocalFiles';
DELETE FROM docs_files WHERE provider_type = 'local' AND provider_name = 'LocalFiles';
```

---

## API Methods

### VectorRepository Methods

**Check if indexed:**
```csharp
await _vectorRepository.IsDocumentIndexedAsync(docId, etag, providerType, providerName, ct);
```

**Cleanup orphaned documents:**
```csharp
await _vectorRepository.CleanupOrphanedDocumentsAsync(
    providerType, providerName, currentDocumentIds, ct);
```

**Force delete all provider data:**
```csharp
await _vectorRepository.DeleteAllProviderDocumentsAsync(
    providerType, providerName, ct);
```

**Update file tracking:**
```csharp
await _vectorRepository.UpdateFileTrackingAsync(
    docId, filename, etag, lastModified, providerType, providerName, relativePath, ct);
```

---

## Summary

The indexer provides **complete file lifecycle management**:

✅ **Automatic change detection** via ETags  
✅ **Efficient updates** with upsert logic  
✅ **Orphan cleanup** for deleted files  
✅ **Configurable behavior** for different use cases  
✅ **No duplicate data** with unique constraints  
✅ **Force reindex** option for schema changes  

Default configuration keeps your vector database **in sync with document sources** automatically.
