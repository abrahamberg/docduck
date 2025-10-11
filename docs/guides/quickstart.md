# Quickstart (2-Minute Run)

This page is intentionally minimal. You will get the app running locally, open the UI, ask a question, and see source documents. For advanced provider setup (OneDrive, S3, multi-provider, Kubernetes, troubleshooting) go to: **[Advanced Install & Setup](install.md)**.

## What You Get
- Automatic one-shot indexing of a local folder (docx / pdf / txt)
- Vector search + Chat and Ask endpoints (`/query`, `/chat`)
- React UI (Material UI) with sources in answers
- No external cloud storage configuration required

## Prerequisites
- Docker + Docker Compose
- OpenAI API key (standard account) exported as `OPENAI_API_KEY`
- A folder of documents on your machine (absolute path)

## 1. Create `.env.local`
Add a file in the project root named `.env.local` with:
```
OPENAI_API_KEY=sk-your-key
LOCAL_DOCS_PATH=/absolute/path/to/docs
```
Replace the placeholders. The path must be absolute and point to a folder containing your `.pdf`, `.docx`, or `.txt` files.

Load it into your shell (so `docker compose` can see the values):
```bash
set -a; source .env.local; set +a
```
Quick sanity check:
```bash
ls -1 "$LOCAL_DOCS_PATH" | head
```

## 2. Launch Everything
Use the modern syntax (`docker compose`).
```bash
docker compose -f docker-compose-local.yml up --build
```
Run this from the project root (the directory that contains `docker-compose-local.yml`, `Api/`, `Indexer/`, and `web/`). Running from another directory can cause `failed to read dockerfile: open Dockerfile: no such file or directory` errors.
Containers:
- `postgres` – stores chunks (pgvector enabled)
- `indexer` – runs once, ingests files from `LOCAL_DOCS_PATH`, then exits
- `api` – serves `/health`, `/query`, `/chat`, `/providers` on `http://localhost:8080`
- `web` – UI served at `http://localhost:5173`

## 3. Watch Indexing (Optional)
```bash
docker logs -f docduck-indexer
```
You should see file processing and chunk counts. When it exits with code 0 indexing is done.

## 4. Open the App
Navigate to: `http://localhost:5173`

In the top bar you can switch between:
- Chat (multi-turn with memory)
- Ask (single stateless question)

Start with a question like:
> "Give me a concise overview of the documents."

Responses include expandable source chunks (click to inspect text + metadata).

## 5. Health & Verification
Check raw status:
```bash
curl http://localhost:8080/health | jq
```
Fields:
- `openAiKeyPresent` should be true
- `dbConnectionPresent` should be true
- `chunks` > 0 after indexing
- `documents` count of processed files

## 6. Re-Index After Adding Files
Add or modify files in `LOCAL_DOCS_PATH`, then rerun:
```bash
docker compose -f docker-compose-local.yml run --rm indexer
```
This performs a fresh ingestion (upserts modified chunks).

## 7. API Curl Example
Query endpoint returns an answer plus source chunks.
```bash
curl -X POST http://localhost:8080/query \
  -H 'Content-Type: application/json' \
  -d '{"question":"List the main themes across all documents"}' | jq
```

## 8. Clean Up
```bash
docker compose -f docker-compose-local.yml down -v
```
Removes containers + volumes (you keep your local docs).

## 9. Where to Go Next
Head to **[Advanced Install & Setup](install.md)** for:
- OneDrive Business & Personal configuration
- S3 and other providers
- Multi-provider compose file
- Detailed troubleshooting & performance tips
- Kubernetes deployment notes

---
Need to customize file types or chunking? Edit `Indexer/appsettings.local.yaml` then rerun the indexer container.
