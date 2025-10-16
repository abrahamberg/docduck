# Quick Start

Goal: Run DocDuck locally, index a few files, ask a question — in under 10 minutes.

## 🚀 Fastest Path: Docker Compose (Recommended)

If you have Docker installed, you can be querying your docs in a few commands.

1. Create a folder with some test documents (local provider mounts them):
  ```bash
  mkdir -p sample-docs
  echo "DocDuck is a multi-provider RAG system" > sample-docs/intro.txt
  ```
2. Create an `.env` file in the repo root (used by compose):
  ```bash
  cat > .env <<'EOF'
  OPENAI_API_KEY=sk-yourkey
  LOCAL_DOCS_PATH=./sample-docs
  # Optional: change admin secret
  ADMIN_AUTH_SECRET=change-me-local-admin-secret
  EOF
  ```
3. Launch the stack:
  ```bash
  docker compose up --build -d postgres
  # wait for healthy DB (healthcheck), then bring up remaining services
  docker compose up --build -d indexer api web
  ```
4. Re-run the indexer whenever documents change:
  ```bash
  docker compose run --rm indexer
  ```
5. Query the API:
  ```bash
  curl -s -X POST http://localhost:8080/api/query \
    -H 'Content-Type: application/json' \
    -d '{"question":"What is DocDuck?"}' | jq
  ```
6. Open the (optional) web frontend at: http://localhost:8080

Skip to [Next Steps](#next) or continue for the manual (.NET SDK) path below.

## Prerequisites

- Docker (recommended) OR .NET 8 SDK if building locally
- PostgreSQL 15+ with `pgvector` extension (1536-dim embeddings)
- OpenAI API key (or Azure OpenAI compatible endpoint)

## 1. Prepare PostgreSQL

Create database & enable pgvector (inside psql):

```sql
CREATE DATABASE docduck;
\c docduck;
CREATE EXTENSION IF NOT EXISTS vector;
```

Load schema:
```bash
psql -h localhost -U postgres -d docduck -f sql/01-init-schema.sql
```

## 2. Create a minimal .env file

Create `.env.local` (never commit secrets):
```
OPENAI_API_KEY=sk-yourkey
DB_CONNECTION_STRING=Host=localhost;Database=docduck;Username=postgres;Password=postgres;MinPoolSize=1;MaxPoolSize=5
# Optional chunking tweaks
CHUNK_SIZE=1000
CHUNK_OVERLAP=200
```

## 3. Add some local test documents

Put a few `.md` or `.txt` files in a folder, e.g. `./sample-docs/`.

## 4. Run Indexer (local folder provider)

Set provider env (simplest local provider — assumes directory mount or direct path):
```
PROVIDER_LOCAL_ENABLED=true
PROVIDER_LOCAL_NAME=localdocs
PROVIDER_LOCAL_ROOT_PATH=./sample-docs
```

Run:
```bash
cd Indexer
dotnet run
```

If successful you'll see logs like:
```
Processed README.md from localdocs: 5 chunks
```

## 5. Start Query API

```bash
cd Api
DB_CONNECTION_STRING=Host=localhost;Database=docduck;Username=postgres;Password=postgres OPENAI_API_KEY=$OPENAI_API_KEY dotnet run
```

## 6. Ask a Question

```bash
curl -s -X POST http://localhost:5000/query \
  -H 'Content-Type: application/json' \
  -d '{"question":"What is DocDuck?"}' | jq
```

## 7. Explore Providers

```bash
curl http://localhost:5000/providers
```

## 8. Chat (streaming disabled example)
```bash
curl -s -X POST http://localhost:5000/chat \
  -H 'Content-Type: application/json' \
  -d '{"message":"Summarize the indexed docs"}' | jq
```

## Clean Up / Re-run
- Re-run the indexer any time after modifying documents
- Use force reindex (set `FORCE_FULL_REINDEX=true`) if you alter chunking parameters

## Next
- Configure OneDrive or S3: see [Providers Overview](../providers/index.md)
- Tuning performance: see [Performance & Scaling](performance.md)
- Troubleshooting: see [Troubleshooting](troubleshooting.md)
