# Database Schema

Primary store: PostgreSQL with `pgvector` extension for similarity search.

## Tables (Conceptual)

### docs_files
Tracks file-level metadata for idempotency.
| Column | Type | Notes |
|--------|------|-------|
| doc_id | text | Provider-scoped unique ID |
| filename | text | Display/file name |
| provider_type | text | e.g. `onedrive` |
| provider_name | text | Instance label |
| etag | text | Change token |
| last_modified | timestamptz | Source timestamp |
| relative_path | text | For local provider |
| updated_utc | timestamptz | Tracking |

Unique key often `(doc_id, provider_type, provider_name)`.

### docs_chunks
Stores chunk embeddings.
| Column | Type | Notes |
|--------|------|-------|
| doc_id | text | FK to docs_files.doc_id (logical) |
| chunk_num | int | Sequential chunk index |
| filename | text | Original filename |
| provider_type | text | Provider type |
| provider_name | text | Provider name |
| embedding | vector(1536) | OpenAI embedding |
| text | text | Chunk content |
| metadata | jsonb | Additional attributes |
| updated_utc | timestamptz | Upsert time |

Index: IVFFlat (or HNSW future) on `embedding` with cosine distance.

### providers
(If present) Tracks enabled providers and last sync time.

### ai_provider_settings
Stores AI provider configurations (chat models and embedding models). Each provider gets its own row.

| Column | Type | Notes |
|--------|------|-------|
| provider_id | text | Primary key - unique model identifier |
| provider_type | text | 'global', 'chat', or 'embedding' |
| settings | jsonb | Provider-specific configuration |
| test_status | text | 'Untested', 'Passed', or 'Failed' |
| last_tested_at | timestamptz | Timestamp of last connectivity test |
| last_test_message | text | Result message from last test |
| updated_at | timestamptz | Last configuration update |

**Row types:**
- `provider_type = 'global'`: Global AI settings (tier assignments, defaults)
- `provider_type = 'chat'`: Individual chat model configurations
- `provider_type = 'embedding'`: Individual embedding model configurations

The API preloads only assigned providers (those referenced in tier assignments or active embedding).

## Sample Vector Index Creation
```sql
CREATE INDEX IF NOT EXISTS docs_chunks_embedding_idx ON docs_chunks USING ivfflat (embedding vector_cosine_ops) WITH (lists=100);
```
Tune `lists` based on dataset size.

## Similarity Query (Example)
```sql
SELECT doc_id, filename, provider_type, provider_name, text, embedding <=> $1 AS distance
FROM docs_chunks
WHERE provider_type = COALESCE($2, provider_type)
ORDER BY embedding <=> $1
LIMIT $3;
```

## Maintenance
| Task | Command |
|------|---------|
| Analyze | `ANALYZE docs_chunks;` |
| Vacuum | `VACUUM (VERBOSE, ANALYZE) docs_chunks;` |
| Reindex vector | Recreate index after large data shifts |

## Size Estimates
| Factor | Impact |
|--------|--------|
| Chunk size smaller | More rows → more embeddings cost |
| Higher overlap | More redundancy |
| Larger models | More vector bytes |

## Next
- Vector search logic: [Search & RAG](../developer/search-rag.md)
- Pipeline: [Pipeline](../developer/pipeline.md)
