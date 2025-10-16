# Pipeline

Detailed look at the indexing pipeline driven by `MultiProviderIndexerService`.

## Steps
| Step | Purpose | Key Methods |
|------|---------|-------------|
| Enumerate Providers | Gather enabled providers | `ProviderCatalog.GetProvidersAsync` |
| Register Provider | Track presence & sync time | `VectorRepository.RegisterProviderAsync` |
| List Documents | Get candidate docs | `IDocumentProvider.ListDocumentsAsync` |
| Skip Unchanged | Avoid reprocessing | `VectorRepository.IsDocumentIndexedAsync` |
| Download | Stream file contents | `IDocumentProvider.DownloadDocumentAsync` |
| Extract | Produce plain text | `TextExtractionService.ExtractTextAsync` |
| Chunk | Segment into overlapping units | `TextChunker.Chunk` |
| Embed | Generate vector | `OpenAiEmbeddingsClient.EmbedBatchedAsync` |
| Upsert | Persist chunk & metadata | `VectorRepository.InsertOrUpsertChunksAsync` |
| Track File | Store ETag & timestamps | `VectorRepository.UpdateFileTrackingAsync` |
| Cleanup Orphans | Remove missing docs | `VectorRepository.CleanupOrphanedDocumentsAsync` |

## Idempotency
- ETag equality ⇒ skip
- Force full reindex deletes provider scope first

## Metadata JSON
Each chunk stores JSON metadata (doc id, provider, etag, path, chunk position). Useful for future filtering.

## Error Handling
- Per-file try/catch logs and continues
- Global catch returns non-zero exit

## Cancellation
- Cooperative via `CancellationToken`
- SIGTERM maps to graceful cancellation (Kubernetes jobs)

## Performance Considerations
| Lever | Impact | Tradeoff |
|-------|--------|----------|
| Batch size | Fewer HTTP calls | Memory & rate limit risk |
| Chunk size | Fewer embeddings | Less granular retrieval |
| Overlap | Better context continuity | Higher cost |

## Future Enhancements
- Parallel provider processing
- Adaptive chunk sizing per file type
- Retries with exponential backoff on transient embedding failures

## Next
- RAG internals: [Search & RAG](search-rag.md)
