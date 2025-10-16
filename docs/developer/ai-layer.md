# Embeddings & AI Layer

Responsible for generating embeddings and synthesizing answers.

## Embeddings
- Default model: `text-embedding-3-small` (1536 dims)
- Batched via `OpenAiEmbeddingsClient.EmbedBatchedAsync`
- Batch size tuned by env (`EMBED_BATCH_SIZE`)

## Answer Generation
- `OpenAiSdkService.GenerateAnswerAsync(question, contextChunks, history)`
- Concatenates top-K chunk texts into a prompt
- Tracks token usage (approx) for logging

## Chat
- `ChatService` sequences: embed last user message → retrieve context → build incremental answer
- Streaming: server-sent events emitting step updates & final answer

## Extending Models
| Goal | Strategy |
|------|----------|
| New embedding model | Add config + alter schema dimension + reindex |
| Multi-model | Store model id in metadata; choose at query time |
| Local model proxy | Change `OPENAI_BASE_URL` to point at gateway |

## Prompt Strategy (Simplified)
- System-style instruction (implicit)
- Context concatenation (ordered by similarity)
- User question appended
- Model asked to answer citing sources implicitly (source chunk mapping done externally)

## Considerations
| Concern | Mitigation |
|---------|------------|
| Context overflow | Limit chunk sizes / reduce top-K |
| Hallucination | Provide direct chunk content; consider answer validation |
| Cost | Batch embeddings; right-size chunk length |

## Future Enhancements
- Per-provider model routing
- Reranking stage (e.g. cross encoder)
- Source citation markers referencing chunk IDs

## Next
- Search & ranking internals: [Search & RAG](search-rag.md)
