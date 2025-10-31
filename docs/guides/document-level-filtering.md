# Document-Level Filtering

## Overview

Document-level filtering is a two-stage retrieval optimization that improves search relevance when you have many documents with relatively few chunks each. Instead of searching all chunks directly, it:

1. **Stage 1**: Compares the query embedding to document-level average embeddings to find the most relevant documents
2. **Stage 2**: Searches chunks only within those top-N documents

This is particularly useful when you have scenarios like:
- 7 candidate documents with 1 chunk each - you want to find which document is most relevant
- Many small documents where document-level similarity is a strong signal
- Large document collections where narrowing by document first improves precision

## How It Works

### Database Storage

Each document in the `docs_files` table has an `avg_embedding` column that stores the average of all chunk embeddings for that document. This is computed automatically by the indexer when chunks are inserted.

### Search Flow

When `EnableDocumentLevelFiltering` is enabled:

```
Query → Embed Question
  ↓
  ├─ Stage 1: Document-Level Filter
  │   └─ Compare query to document avg_embeddings
  │   └─ Select top-N most similar documents (DocumentLevelTopK)
  ↓
  ├─ Stage 2: Chunk-Level Search
  │   └─ Search chunks ONLY within selected documents
  │   └─ Apply hybrid ranking (vector + lexical)
  ↓
Response
```

### Averaging Strategy

The document-level embedding is computed as the **arithmetic mean** of all chunk embeddings:

```
avg_embedding = (chunk_1 + chunk_2 + ... + chunk_n) / n
```

This provides a centroid representation of the document's semantic content in the embedding space.

## Configuration

### Environment Variables

Document-level filtering is **enabled by default**. To customize or disable:

```bash
# Disable if needed (enabled by default)
ENABLE_DOCUMENT_LEVEL_FILTERING="false"

# Adjust max documents to consider (default: 20)
DOCUMENT_LEVEL_TOP_K="30"
```

### appsettings.yaml

Or configure via `appsettings.yaml`:

```yaml
Search:
  EnableDocumentLevelFiltering: true  # Default: true
  DocumentLevelTopK: 20               # Default: 20
```

### Default Behavior

- **EnableDocumentLevelFiltering**: `true` (enabled by default)
- **DocumentLevelTopK**: `20` (consider top 20 documents)

## When to Enable

### ✅ Good Use Cases

- **Small documents**: Each document has 1-5 chunks
- **Document-centric queries**: User is looking for which document contains the answer
- **Large collections**: Thousands of small documents where pre-filtering improves efficiency
- **High precision needed**: Want to ensure results come from the most relevant documents

### ❌ Not Recommended

- **Large documents with many chunks**: Document average may wash out specific chunk relevance
- **Chunk-specific queries**: When the answer is in a specific section of a large document
- **Already using provider filtering**: When you're already filtering to a small set via `providerType`/`providerName`

## Example Scenario

You have **7 API documentation files** with **1 chunk each**:
- `get-users.md`
- `create-user.md`
- `update-user.md`
- `delete-user.md`
- `list-posts.md`
- `create-post.md`
- `analytics-dashboard.md`

**Query**: "How do I create a new user account?"

**Without document-level filtering**:
- Searches all 7 chunks
- Ranks by individual chunk similarity
- May return chunks from multiple documents

**With document-level filtering** (`DocumentLevelTopK=3`):
1. Compares query to 7 document averages
2. Selects top 3 documents: `create-user.md`, `update-user.md`, `get-users.md`
3. Searches chunks only within those 3 documents
4. Returns most relevant chunks from the best documents

**Result**: Higher chance of surfacing `create-user.md` as the primary source.

## Performance Considerations

### Benefits
- **Reduced chunk search space**: Fewer chunks to compare in Stage 2
- **Better document clustering**: Results tend to come from coherent document sets
- **Improved precision**: Less noise from unrelated documents

### Costs
- **Extra query**: One additional vector similarity query (document-level)
- **Index overhead**: Additional vector index on `docs_files.avg_embedding`
- **Potential recall loss**: Relevant chunks in non-top documents are excluded

### Tuning

Adjust `DocumentLevelTopK` based on your needs:
- **Low (5-10)**: High precision, narrow focus, risk of missing relevant docs
- **Medium (20-50)**: Balanced precision/recall
- **High (100+)**: Minimal filtering, mostly for logging/debugging

## Monitoring

Watch the logs to see if document-level filtering is working:

```
Search produced 8 chunks (vector: 8, lexical: 0, doc-filter: enabled)
Document-level filtering selected 5 documents from top-20
```

If you see `doc-filter: disabled`, check:
1. Is `EnableDocumentLevelFiltering` set to `true`?
2. Are there documents with `avg_embedding` populated? Run the migration: `sql/05-add-document-level-embeddings.sql`
3. Are you using `searchDepth=1` (lexical-only)? Document filtering is skipped for lexical-only mode.

## Migration

If you have existing data, run the migration to populate `avg_embedding`:

```bash
psql $DB_CONNECTION_STRING -f sql/05-add-document-level-embeddings.sql
```

This will:
1. Add `avg_embedding vector(1536)` column to `docs_files`
2. Create a vector index for fast similarity search
3. Compute and populate averages for all existing documents
4. Create a helper function for future updates

Future document ingestion automatically updates `avg_embedding` when chunks are inserted.

## See Also

- [Search & RAG](../developer/search-rag.md) - Overall search architecture
- [Database Schema](../database/schema.md) - Table structures
- [API Configuration](api-configuration.md) - Environment variables reference
