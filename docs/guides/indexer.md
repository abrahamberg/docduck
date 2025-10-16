# Indexer Operation

The indexer aggregates documents from all enabled providers and maintains an up-to-date vector store.

## Flow
```
List docs → Filter unchanged (ETag) → Download → Extract → Chunk → Embed → Upsert → Cleanup orphaned
```

## Exit Codes
| Code | Meaning |
|------|---------|
| 0 | Success (≥1 file processed) |
| 1 | Error or nothing processed |
| 130 | Cancelled (SIGTERM/SIGINT) |

## Key Behaviors
- **Idempotent**: Skips unchanged via ETag
- **Force Reindex**: Set `FORCE_FULL_REINDEX=true`
- **Orphan Cleanup**: Removes DB entries for missing docs when enabled
- **Batch Embeddings**: Controlled by `EMBED_BATCH_SIZE`

## Scheduling
Kubernetes CronJob example:
```yaml
apiVersion: batch/v1
kind: CronJob
metadata:
  name: docduck-indexer
spec:
  schedule: "0 */6 * * *"
  jobTemplate:
    spec:
      template:
        spec:
          containers:
          - name: indexer
            image: your-registry/docduck-indexer:latest
            env: # supply provider & DB vars
          restartPolicy: OnFailure
```

## Operational Tips
| Scenario | Recommendation |
|----------|----------------|
| High churn folder | Run indexer more frequently |
| Large initial ingest | Temporarily raise batch size & CPU limits |
| Memory pressure | Reduce `EMBED_BATCH_SIZE` |

## Logs
Structured info-level logs show provider, filename, chunk counts, durations.

## Failure Handling
Exceptions per file are logged; pipeline continues with next file.

## Next
- Pipeline internals: [Pipeline](../developer/pipeline.md)
- Tuning: [Performance & Scaling](performance.md)
