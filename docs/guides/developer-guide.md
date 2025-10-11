# Developer Guide

This guide covers local development, coding conventions, docs structure, and how to run, test, and maintain DocDuck.

## Prerequisites

- .NET 8 SDK
- PostgreSQL with pgvector extension
- OpenAI API key
- Optional: Azure AD app registration for Microsoft Graph (for indexing OneDrive)

## Project Structure

```
docduck/
├── Api/                # Query/Chat RAG API (ASP.NET Core minimal API)
├── Indexer/            # OneDrive indexer (console app)
├── Indexer.Tests/      # Unit and integration tests
├── sql/                # Database SQL scripts (schema, queries, maintenance)
├── k8s/                # Kubernetes manifests
└── docs/               # Documentation (this guide, database docs, reports)
```

## Setup Environment

1. Configure database and pgvector:
   - Quick path: use the CLI helper
     ```bash
     ./db-util.sh test
     ./db-util.sh init
     ./db-util.sh check
     ```
   - Or run SQL manually:
     ```bash
     psql -h localhost -U postgres -d vectors -f sql/01-init-schema.sql
     ```

2. Export required variables (bash/zsh):
   ```bash
   export OPENAI_API_KEY="sk-..."
   export DB_CONNECTION_STRING="Host=localhost;Database=vectors;Username=postgres;Password=password;MinPoolSize=1;MaxPoolSize=3"
   ```

3. For indexing OneDrive (optional):
   - See docs/guides/authentication.md for the two auth modes
   - Typical env for business account:
     ```bash
     export GRAPH_AUTH_MODE=ClientSecret
     export GRAPH_ACCOUNT_TYPE=business
     export GRAPH_TENANT_ID="..."
     export GRAPH_CLIENT_ID="..."
     export GRAPH_CLIENT_SECRET="..."
     export GRAPH_DRIVE_ID="..."
     export GRAPH_FOLDER_PATH="/Shared Documents/Docs"
     ```

## Build and Run

### Indexer

```bash
cd Indexer
dotnet build
dotnet run
```

Monitor database:
```bash
./db-util.sh stats
./db-util.sh recent
./db-util.sh health
```

### Query API

```bash
cd Api
dotnet run
```

Test endpoints:
```bash
curl http://localhost:5000/health
curl -X POST http://localhost:5000/query -H 'Content-Type: application/json' -d '{"question":"What is this project about?"}'
```

## Testing

```bash
cd Indexer.Tests
dotnet test

# Integration tests (requires PostgreSQL)
export TEST_DB_CONNECTION_STRING="Host=localhost;Database=vectors_test;Username=postgres;Password=password"
dotnet test
```

## Coding Conventions

- C# 12, .NET 8, file-scoped namespaces, nullable enabled
- Small, focused services (pragmatic SOLID)
- Async/await end-to-end; pass CancellationToken through public async APIs
- Structured logging with Microsoft.Extensions.Logging
- Prefer explicit types and clear naming
- Keep side effects at the edges; keep core logic simple and testable

## Docs Structure and Policy

- Only keep `README.md` in the repository root. All other docs live under `docs/`.
- Long-lived docs:
  - Database: `docs/database/pgvector.md`, `docs/database/pgvector-quickref.md`
  - Guides: `docs/guides/quickstart.md`, `docs/guides/authentication.md`, `docs/guides/developer-guide.md`
  - Architecture: `docs/architecture.md`
  - Changelog: `docs/changelog.md`
- Generated or short-lived implementation summaries belong in `docs/reports/` with clear dates, e.g., `docs/reports/api-implementation.md`.
- Prefer updating existing docs over creating new top-level documents. Avoid duplicating content.

## Quality Gates

- Build: `dotnet build` for Api and Indexer
- Tests: `dotnet test` in `Indexer.Tests`
- Lint/format: rely on SDK analyzers; keep warnings clean
- Smoke test:
  - Run indexer on a small set (set `MAX_FILES=3`)
  - Start API and hit `/health` and `/query`

## Useful References

- Database guide: `docs/database/pgvector.md`
- Quick reference: `docs/database/pgvector-quickref.md`
- Quick start: `docs/guides/quickstart.md`
- Auth guide: `docs/guides/authentication.md`
