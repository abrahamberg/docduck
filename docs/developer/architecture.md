# Architecture

DocDuck comprises two primary runtime components plus shared libraries:

```
+----------------+        +-----------------+        +--------------------+
|  Providers     |  --->  | Indexer Service |  --->  | PostgreSQL + pgvector|
+----------------+        +-----------------+        +--------------------+
                                                   ^          |
                                                   |          v
                                                +------------------+
                                                |   Query API      |
                                                +------------------+
```

## Components
| Component | Responsibility |
|-----------|----------------|
| Indexer | Periodic ingestion of documents, embeddings generation, DB upsert |
| Query API | Semantic search & answer/chat generation |
| Providers Shared | Common configuration / provider abstractions |
| PostgreSQL + pgvector | Durable storage + similarity search |

## Indexer Internals
- `MultiProviderIndexerService` orchestrates the full run
- `IDocumentProvider` implementations supply documents
- `TextExtractionService` selects extractor by extension
- `TextChunker` splits text
- `OpenAiEmbeddingsClient` batches embedding requests
- `VectorRepository` handles persistence & idempotency

## Query API Internals
- Minimal ASP.NET Core host (`Api/Program.cs`)
- `VectorSearchService` performs vector similarity queries
- `OpenAiSdkService` produces embeddings & answers
- `ChatService` manages conversation state/streaming updates

## Data Flow (Detailed)
1. Provider enumerates docs → doc metadata
2. Repository checks if doc already indexed (ETag)
3. Download + extract text
4. Chunk text (size, overlap)
5. Embed each chunk (batched)
6. Upsert chunks + update file tracking
7. Cleanup orphaned records

Query:
1. Embed question
2. Vector similarity search (top-K)
3. Compose context
4. Generate answer (OpenAI)
5. Return answer + source citations

## Extensibility Points
| Area | Mechanism |
|------|-----------|
| New Provider | Implement `IDocumentProvider` |
| New Extractor | Implement `ITextExtractor` & DI registration |
| Embedding Model | Add client service + adjust vector dimension & schema |
| Search Strategy | Modify `VectorSearchService` (reranking / filters) |

## Non-Goals (Current Version)
- Multi-tenant database isolation
- Complex ACL enforcement
- Built-in auth for public query endpoints (planned)

## Diagrams
Future enhancement: richer UML sequence diagrams.

## Next
- Pipeline deep dive: [Pipeline](pipeline.md)
- Provider details: [Provider Framework](provider-framework.md)
