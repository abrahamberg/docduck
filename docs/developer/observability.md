# Observability

Current observability is logging-centric; metrics/tracing are future enhancements.

## Logging
- Console logger configured with information level by default
- Key events: provider start/end, file processed, chunk counts, errors

## Suggested Conventions
| Event | Level |
|-------|------|
| Start indexing | Information |
| Skip unchanged | Information |
| File processed | Information |
| Unsupported file | Warning |
| Extraction failure | Error |
| Fatal pipeline error | Error |

## Adding Metrics (Future)
Potential integration with `OpenTelemetry`:
- Counter: files_processed
- Counter: chunks_indexed
- Histogram: embedding_latency_ms
- Gauge: providers_enabled

## Tracing (Future)
- Activity per provider run
- Child spans: list, download, extract, embed, upsert

## Log Enrichment
- Include provider type/name, filename, operation durations

## Shipping Logs
- Container platform aggregation (stdout) -> Loki, ELK, Cloud-native logging

## Alert Ideas
| Condition | Alert |
|-----------|-------|
| 0 files processed | Investigate provider config |
| Error rate spike | Extraction / API failure |
| High embedding latency | Upstream model slowness |

## Next
- Performance tuning internals: [Performance Internals](performance-internals.md)
