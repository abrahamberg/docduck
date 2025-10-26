# DocDuck

Multi-provider document indexing and AI-powered Retrieval-Augmented Generation (RAG) built with .NET 8, PostgreSQL + pgvector, and flexible AI model support.

> Index your OneDrive, S3, and local knowledge; ask natural language questions; receive cited answers using any AI provider.

## Highlights

- 🔌 Extensible Providers: OneDrive (business & personal), S3, local filesystem (add more via a tiny interface)
- 🤖 Model-Agnostic AI: OpenAI, Anthropic, Azure OpenAI, local models (Ollama), custom endpoints via flexible JSON config
- 🧠 Smart Indexing: ETag change detection, orphan cleanup, optional full reindex
- ✂️ Robust Text Extraction: DOCX, Markdown/Text, PDF*, ODT, RTF (pluggable)
- 🧩 Flexible Chunking: Tunable size & overlap for quality vs cost balance
- 🗄️ Vector Search: PostgreSQL + pgvector (IVFFlat) + lexical hybrid search
- 💬 Unified Query API: /query with adaptive depth (1-5) and optional SSE streaming
- 🎛️ Admin UI: Configure AI models, providers, test connections via web interface
- 🧱 Clean Architecture: Focused services, pragmatic SOLID, modern C#
- 🚀 Deployment Friendly: Docker, Kubernetes CronJob (indexer) + Deployment (API)

*PDF extraction requires optional dependency.

## Quick Glance

```
Providers → Indexer Pipeline → PostgreSQL (chunks + embeddings) ← Query API ← User
```

## Fast Start

```bash
# 1. Ensure PostgreSQL + pgvector
psql -h localhost -U postgres -c "CREATE DATABASE docduck;" || true
psql -h localhost -U postgres -d docduck -c "CREATE EXTENSION IF NOT EXISTS vector;"
psql -h localhost -U postgres -d docduck -f sql/01-init-schema.sql

# 2. Environment
export OPENAI_API_KEY=sk-...  # Used for initial seeding only
export DB_CONNECTION_STRING="Host=localhost;Database=docduck;Username=postgres;Password=postgres;MinPoolSize=1;MaxPoolSize=5"
export PROVIDER_LOCAL_ENABLED=true
export PROVIDER_LOCAL_NAME=localdocs
export PROVIDER_LOCAL_ROOT_PATH=./sample-docs

# 3. Index some files
mkdir -p sample-docs && echo "DocDuck is a multi-provider RAG system" > sample-docs/intro.txt
cd Indexer && dotnet run && cd ..

# 4. Start API
cd Api && dotnet run &
sleep 3

# 5. Ask a question
curl -s -X POST http://localhost:5000/query \
  -H 'Content-Type: application/json' \
  -d '{"question":"What is DocDuck?"}' | jq
```

Full detail: see **Docs → Quick Start**.

## Documentation

| Topic | Link |
|-------|------|
| Quick Start | docs/guides/quickstart.md |
| **AI Configuration** | **docs/guides/ai-configuration.md** |
| Installation Paths | docs/guides/installation.md |
| Configuration Reference | docs/guides/configuration.md |
| Providers Overview | docs/providers/index.md |
| API Usage | docs/guides/api-usage.md |
| Indexer Operation | docs/guides/indexer.md |
| Architecture | docs/developer/architecture.md |
| AI Layer & Models | docs/developer/ai-layer.md |
| Pipeline Internals | docs/developer/pipeline.md |
| Provider Framework | docs/developer/provider-framework.md |
| Data Schema | docs/database/schema.md |
| AI Agent Context | docs/context/ai-context.md |

The full site is structured for both operators and developers (MkDocs config included in `mkdocs.yml`).

## Core Concepts

| Concept | Summary |
|---------|---------|
| Provider | Source of documents (OneDrive/S3/local); identified by type + name |
| ETag | Change token used to skip unchanged files |
| Chunk | Overlapping text segment (size/overlap configurable) |
| Embedding | Float vector (1536-d OpenAI by default) stored in `docs_chunks` |
| Orphan Cleanup | Removes vectors for deleted / moved documents |

## Extending

Add a provider: implement `IDocumentProvider` (see `src/Indexer/Providers/IDocumentProvider.cs`), register in DI, define env flags, supply ETag strategy.

Add a text extractor: implement `ITextExtractor`, list supported extensions, and let `TextExtractionService` auto-register it.

## Deployment Overview

| Component | Deployment | Scaling |
|-----------|-----------|---------|
| Indexer | CronJob / on-demand job | Horizontal (separate schedules) |
| API | Deployment / container | Scale replicas behind LB |
| Postgres | Managed service / StatefulSet | Scale vertically + tuned indexes |

See Kubernetes examples in docs.

## Performance Tuning
- Raise `EMBED_BATCH_SIZE` for faster indexing (respect rate limits)
- Adjust `CHUNK_SIZE` for retrieval granularity vs cost
- Tune pgvector index `lists` parameter based on row count

## Roadmap (Excerpt)
- Authentication/authorization for query endpoints
- Additional vector backends (optional abstraction)
- Advanced reranking & hybrid search
- UI reference implementation

Full: see `docs/guides/roadmap.md`.

## Contributing
- Read coding standards: `docs/developer/coding-standards.md`
- Open an issue describing change intent
- Keep PRs small & focused; include tests where possible

## License
MIT. See `docs/guides/license.md`.

## Legacy Notice
`README.old.md` retained temporarily; content merged & superseded by this README and new docs.

---
Enjoy fast, transparent RAG over your documents — ducks in a row 🦆.
