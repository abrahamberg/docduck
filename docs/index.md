# DocDuck

Multi-provider document indexing + AI retrieval (RAG) for your internal knowledge. Index OneDrive, S3, or local files; ask natural language questions; get cited answers.

## Key Features

- Multi-provider: OneDrive (business & personal), S3, local filesystem (extensible)
- Smart idempotent indexing (ETag tracking, orphan cleanup, force reindex)
- Pluggable text extraction (DOCX, TXT/MD, PDF*, ODT, RTF)
- Configurable chunking & OpenAI embeddings
- Minimal Query API with /query, /chat (streaming), /docsearch & provider filtering
- PostgreSQL + pgvector for similarity search
- Kubernetes & Docker friendly (CronJob indexer, long-running API)
- Secure admin operations (seeded admin, secret-based token)
- Pragmatic modern .NET 8 codebase (records, DI, logging)

*PDF requires optional dependency.

## Quick Start

If you just want to try it locally:

1. Provision PostgreSQL with pgvector extension
2. Set environment variables (OpenAI API key, DB connection string)
3. Run the Indexer once
4. Query the API

See [Quick Start](guides/quickstart.md) for copy-paste steps.

## Documentation Map

| Audience | Start Here |
|----------|------------|
| Casual evaluator | [Quick Start](guides/quickstart.md) |
| Power user / operator | [Installation](guides/installation.md), [Configuration](guides/configuration.md) |
| Architect / engineer | [Architecture](developer/architecture.md) |
| Extending providers | [Provider Framework](developer/provider-framework.md) |
| RAG internals | [Search & RAG](developer/search-rag.md) |
| AI Agent embedding | [AI Agent Context](context/ai-context.md) |

## High-Level Architecture

```
Providers → Indexer Pipeline → PostgreSQL (chunks + metadata) ← Query API (RAG) ← User
```

- Indexer runs on a schedule (or ad-hoc) and updates embeddings
- Query API performs semantic search + synthesis across stored chunks

See [Architecture](developer/architecture.md) for detailed diagrams.

## Why DocDuck?

- Production-focused from the start (idempotency, cleanup, metrics-friendly logging)
- Lean: only the moving parts needed for reliable RAG over your documents
- Extensible: add new providers & text extractors with small focused interfaces
- Transparent: clear data model & SQL; easy to audit

## Status

Active development. API surface & schema considered stable for initial OSS release (v1). Expect additive enhancements.

## License

MIT — see [License](guides/license.md).
