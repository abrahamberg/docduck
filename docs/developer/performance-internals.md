# Performance Internals

## Indexing Cost Drivers
| Driver | Effect |
|--------|-------|
| Chunk Size | Number of embeddings & DB rows |
| Overlap | Redundant text & embedding cost |
| Batch Size | HTTP call amortization |

## Embedding Batching
- Aggregates chunk texts until batch size reached or flush needed
- Parallelization currently sequential (future parallel provider processing)

## DB Considerations
| Aspect | Guidance |
|--------|----------|
| Connection Pool | Keep small (indexer short-lived) |
| Vector Index Lists | Increase with data volume for recall |
| Autovacuum | Ensure aggressive enough for large churn |

## Memory
- Large PDF extraction may inflate transient memory; consider streaming

## Potential Future Optimizations
| Idea | Benefit |
|------|--------|
| Parallel provider processing | Reduce wall-clock time |
| Embedding caching | Skip duplicate identical text segments |
| Adaptive chunk sizing | Optimize for document variance |
| Rerank thresholding | Trim answer prompt size |

## Profiling Approach
1. Run indexer with large dataset
2. Measure wall-clock and per-file durations
3. Enable debug logs for fine-grained timings

## Next
- Observability / metrics: [Observability](observability.md)
