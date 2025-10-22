# Search & RAG

Describes how DocDuck performs similarity search and constructs answers.

## Steps (/query)
1. Embed question text
2. Run hybrid retrieval:
	- Vector search over embeddings (cosine distance)
	- Optional lexical search (`tsvector` + `websearch_to_tsquery`) when enabled
	- Blend and rerank chunk candidates based on configured search depth
3. Concatenate chunk texts (ordered by blended score)
4. Prompt model to synthesize answer
5. Return answer + mapped sources (filename + snippet + blended score)

## /docsearch Variation
- Retrieves more chunks (capped) then groups by `doc_id`
- Selects top documents by best (lowest) chunk distance
- Returns representative snippet from each doc

## Scoring
- Vector similarity: cosine distance via pgvector (`embedding <=> $query`)
- Lexical similarity: `ts_rank_cd` over `search_lexeme` generated column
- Blended score = `(1 - distance/2) * vector_weight + lexical_score * lexical_weight`
- Lower blended distance (1 - score) ⇒ higher relevance

## Filters
- Optional `providerType` and `providerName` narrow search

## Ranking Quality Levers
| Lever | Effect |
|-------|-------|
| Chunk Size | Too large: diluted relevance; too small: fragmented context |
| Overlap | Prevents context boundary loss |
| Top-K | Higher recall vs prompt cost tradeoff |

## Search Depth Levels
The API exposes a `searchDepth` knob (1-5) to control retrieval effort:

| Depth | Behavior |
|-------|----------|
| 1 | Lexical-only search, minimal orchestration |
| 2 | Lexical + vector blend, single retrieval pass |
| 3 | Default; blended retrieval with retry heuristics |
| 4 | Same as 3, but allows an extra refinement pass |
| 5 | Max effort; expanded retries and aggressive reranking |

Higher depths cost more tokens but improve recall.

## Failure Modes
| Mode | Symptom | Mitigation |
|------|---------|------------|
| Sparse Index | Generic answers | Ensure enough documents, adjust chunk size |
| Overlap Too Low | Missing context | Increase `CHUNK_OVERLAP` |
| Overlap Too High | Cost spike | Reduce overlap when stable |

## Next
- Data schema: [Schema](../database/schema.md)
