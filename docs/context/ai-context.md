# AI Agent Context

This document is a concise reference for LLM-based automation or code-assist tools interacting with DocDuck.

## Core Components
| Concern | Location | Key Types |
|---------|----------|-----------|
| Index Orchestration | `Indexer/MultiProviderIndexerService.cs` | `MultiProviderIndexerService` |
| Provider Abstraction | `Indexer/Providers/IDocumentProvider.cs` | `IDocumentProvider`, `ProviderDocument` |
| Text Extraction | `Indexer/Services/TextExtraction` | `TextExtractionService`, `ITextExtractor` |
| Embeddings | `Indexer/Services/OpenAiEmbeddingsClient.cs` | `OpenAiEmbeddingsClient` |
| Storage | `Indexer/Services/VectorRepository.cs` | `VectorRepository` |
| Query API | `Api/Program.cs` | Minimal API endpoints |
| Search Logic | `Api/Services/VectorSearchService.cs` | `VectorSearchService` |
| Answer Generation | `Api/Services/OpenAiSdkService.cs` | `OpenAiSdkService` |
| Chat Orchestration | `Api/Services/ChatService.cs` | `ChatService` |

## Endpoint Contract Summary
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/health` | GET | Health & counts |
| `/providers` | GET | Active providers list |
| `/query` | POST | Q&A over indexed chunks |
| `/docsearch` | POST | Document-level search |
| `/chat` | POST | Conversational interface (SSE optional) |

## Query Request Fields
- `question` (string) – required for `/query`
- `topK` (int?) – optional override
- `providerType` / `providerName` – optional filters (AND semantics)

## Chat Request Fields
- `message` (required)
- `history` (role/content pairs)
- `streamSteps` (bool) to enable SSE streaming

## Indexing Flow Hooks
1. Provider enumeration (`ListDocumentsAsync`)
2. Skip check (`IsDocumentIndexedAsync` via `VectorRepository`)
3. Extraction chooses extractor by file extension
4. Chunking yields `(chunk_num, char_start, char_end)`
5. Batch embedding returned as vector(float[1536])
6. Upsert with metadata JSON (includes provider, etag)

## Configuration Hotspots
- Environment variable ingestion in `Api/Program.cs` and Indexer `Program.cs`
- Provider enabling: `PROVIDER_<TYPE>_ENABLED`
- Chunk tuning: `CHUNK_SIZE`, `CHUNK_OVERLAP`
- Force reindex: `FORCE_FULL_REINDEX`

## Adding a Provider (Minimal Outline)
1. Implement `IDocumentProvider`
2. Provide constructor DI registration
3. Provide env parsing for enable flag & options
4. Ensure `ProviderType` stable string
5. Supply ETag logic for incremental indexing

## Safety / Idempotency Assumptions
- ETag stable across identical content versions
- Chunk numbering deterministic with same text & chunk settings
- Upserts overwrite embeddings of changed chunks only

## Potential Extension Points
| Area | Strategy |
|------|----------|
| Alternative Embeddings | Add second client & model field in metadata |
| Reranking | Post-process search results before answer synthesis |
| Access Control | Add provider-level ACL filter in search query |
| Caching | Cache question embedding & top-K hits by hash |

## Common Pitfalls
- Missing DB or OpenAI key aborts startup (fail-fast)
- Unsupported file extension triggers `NotSupportedException` in extraction
- Large PDF may require memory/duration safeguards

## Non-Goals (Current)
- Fine-grained auth per query
- Multi-tenant isolation across databases
- Real-time file change push (polling-only indexing)

## Reference Next
- Architecture: [Architecture](../developer/architecture.md)
- Provider Framework: [Provider Framework](../developer/provider-framework.md)
