# Testing

## Current State
- Unit tests minimal placeholder (expand coverage)
- Focus areas for testing: chunking logic, provider listing/filtering, text extraction edge cases, vector repository operations (integration), search ranking.

## Test Types
| Type | Goal | Tools |
|------|------|------|
| Unit | Fast logic validation | xUnit, assertions |
| Integration | DB operations / provider flows | Temporary test DB |
| End-to-end (future) | Full query answer path | Spin API + ephemeral DB |

## Suggested Test Areas
- `TextChunker`: boundaries, overlap, empty input
- `TextExtractionService`: unsupported extension, multi-extractor dispatch
- `VectorRepository`: upsert idempotency, orphan cleanup
- Provider ETag skip logic

## Patterns
- Use factory helpers for sample documents
- Keep test data small, deterministic

## Example (Pseudo Snippet)
```csharp
[Fact]
public void Chunker_Splits_WithOverlap() {
  var c = new TextChunker(new ChunkingOptions{ ChunkSize=10, Overlap=2 });
  var chunks = c.Chunk("abcdefghijklmnopqrstuvwxyz").ToList();
  Assert.True(chunks.Count > 2);
}
```

## Integration DB Setup
```bash
export TEST_DB_CONNECTION_STRING=Host=localhost;Database=docduck_test;Username=postgres;Password=postgres
psql -h localhost -U postgres -c "CREATE DATABASE docduck_test;" || true
psql -h localhost -U postgres -d docduck_test -c "CREATE EXTENSION IF NOT EXISTS vector;"
psql -h localhost -U postgres -d docduck_test -f sql/01-init-schema.sql
```

## Future Enhancements
- Testcontainers for ephemeral Postgres
- Golden-file tests for extraction
- Load tests for search latency

## Next
- Release workflow: [Release & Versioning](release.md)
