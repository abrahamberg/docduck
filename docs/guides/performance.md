# Performance & Scaling

## Dimensions to Tune
| Dimension | Knob | Impact |
|-----------|------|--------|
| Index Throughput | `EMBED_BATCH_SIZE` | Larger batch reduces HTTP overhead |
| Embedding Volume | `CHUNK_SIZE` / `CHUNK_OVERLAP` | Larger chunks => fewer embeddings |
| Query Latency | Vector index `lists` | Higher lists improves recall but slower build |
| API Concurrency | API replicas | Scale horizontally |

## Guidelines
- Start with defaults: 1000 size / 200 overlap
- Re-evaluate after analyzing typical document lengths

## Resource Planning
| Resource | Driver |
|----------|-------|
| CPU | Embedding requests, extraction (PDF/Docx) |
| Memory | Embedding batch accumulation, large file extraction |
| Storage | Number of chunks * avg text + embedding vector |

Approx embedding storage (float4 1536 dims ≈ 6KB per chunk + metadata).

## Scaling Approaches
| Goal | Strategy |
|------|----------|
| Faster index run | Parallelize providers (future), raise batch size |
| Lower query latency | Warm caches, tune DB, optimize index lists |
| Reduced cost | Increase chunk size, decrease overlap |

## Observability
Track (future metrics):
- Files processed per run
- Chunks per file distribution
- Query latency p95

## Advanced
- Shard by provider_name if dataset huge
- Introduce reranking microservice (optional)

## Next
- Troubleshooting: [Troubleshooting](troubleshooting.md)
