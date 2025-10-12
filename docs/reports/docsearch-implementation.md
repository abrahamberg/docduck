## Document Search (docsearch) — implementation notes

This page documents the lightweight "document search" feature added to the API and front-end. It explains the API contract, front-end UX, how to run and test locally (compose or directly), and common troubleshooting steps.

### Summary

- Goal: provide a simple endpoint that embeds a free-text query, finds the most relevant document-level results (grouping chunk hits by document), and returns up to N (default 5) documents ordered by relevance. Each returned document includes a representative chunk text and address/metadata so the UI can show a title/address and expand to reveal the snippet.
- Where to look in the codebase:
  - Backend: `Api/Program.cs`, `Api/Models/QueryModels.cs`, `Api/Services/VectorSearchService.cs`
  - Frontend: `web/src/types.ts`, `web/src/api.ts`, `web/src/components/DocSearchResults.tsx`, `web/src/components/DocSearchPanel.tsx`, `web/src/components/Ask.tsx`, `web/src/App.tsx`

### API contract

POST /docsearch

- Request JSON

```json
{
  "question": "search text",
  "topK": 5,
  "providerType": "local",        // optional
  "providerName": "default"      // optional
}
```

- Response JSON (array of document results)

```json
[
  {
    "docId": 123,
    "filename": "example.pdf",
    "address": "/data/documents/example.pdf",
    "text": "Representative chunk of text from the document...",
    "distance": 0.0123,
    "providerType": "local",
    "providerName": "default"
  }
]
```

Notes:
- `text` is the representative chunk text chosen for the returned document (usually the best-scoring chunk for that document). This lets the front-end render a collapsed document row showing filename/address and show the snippet when expanded.
- `distance` is the vector similarity distance (lower = closer for the chosen metric). The backend groups chunk-level results by `doc_id` and picks the best chunk per document before sorting.

### Front-end UX

- A new "Docs" tab (top-level) exposes `DocSearchPanel` where you can enter a query and see results.
- `DocSearchResults` renders a Material-UI Accordion for each returned document. The accordion summary shows filename and address; expanding it reveals the `text` snippet and provider info.
- The Ask chat UI also has an optional "Doc Search" action that triggers the same `/docsearch` endpoint and shows results inline.

### How to run and test locally

1. Ensure environment variables are available. Important ones:
   - `OPENAI_API_KEY` — for generating embeddings
   - `LOCAL_DOCS_PATH` — path mounted into the Postgres/indexer containers (used by indexer). The compose stack expects this to be set before `podman/docker compose up`.

2. Build the API locally (optional):

```bash
dotnet build Api/Api.csproj
dotnet run --project Api/Api.csproj --urls "http://localhost:5000"
```

3. Call the API directly (when running locally):

```bash
curl -sS -X POST http://localhost:5000/docsearch \
  -H 'Content-Type: application/json' \
  -d '{"question":"how to connect to s3","topK":5}' | jq
```

4. Or run via compose (builds the `api` and `web` containers):

```bash
# make sure .env.local is sourced so LOCAL_DOCS_PATH and OPENAI_API_KEY are set
set -a; source .env.local; set +a
podman compose -f docker-compose-local.yml up -d --build api web
```

5. Open the web UI at `http://localhost` (or the configured web host) and use the "Docs" tab.

### Troubleshooting

- If the `/docsearch` response is empty:
  - Ensure documents are indexed. The indexer must run and insert chunks into Postgres. Check the `indexer` run or the `Indexer` service logs.
  - Verify `LOCAL_DOCS_PATH` points to the directory containing documents and is mounted into any indexer/container as expected.

- If the web UI is "stuck" or not rendering:
  - Open the browser DevTools Console and paste any errors into an issue.
  - Server-side container logs (nginx) won't show client-side JS errors. To inspect web build logs, run the web build locally (`npm run build` in `web`) or tail the container build logs.

- If you see many bundler warnings mentioning `"use client"` (Material UI): they are warnings from the build tool about bundling client directives; they are noisy but usually harmless. If you encounter runtime errors, prioritize the console stack traces.

### Testing tips

- Small smoke test example (direct API):

```bash
curl -sS -X POST http://localhost:5000/docsearch \
  -H 'Content-Type: application/json' \
  -d '{"question":"installation steps","topK":3}' | jq
```

- Tell the UI to search for a phrase that you know exists in one of the indexed documents; the returned `text` should include that phrase in the expanded accordion.

### File diffs / notable changes

- Backend
  - `Api/Program.cs` — added POST `/docsearch` route. Embeds `question`, calls `VectorSearchService.SearchAsync`, groups chunk hits by `doc_id`, picks best chunk per doc, returns top N documents.
  - `Api/Models/QueryModels.cs` — `DocumentResult` response model (contains `docId`, `filename`, `address`, `text`, `distance`, `providerType`, `providerName`).

- Frontend
  - `web/src/types.ts` — `DocumentResult` type
  - `web/src/api.ts` — `postDocSearch()` helper
  - `web/src/components/DocSearchResults.tsx` — Accordion UI that shows `text` in the details
  - `web/src/components/DocSearchPanel.tsx` — top-level page
  - `web/src/components/Ask.tsx` — optional doc search call from the Ask UI
  - `web/src/App.tsx` — added "Docs" tab

### Next steps and improvements

- Add an integration test for the `/docsearch` endpoint that runs against a small seeded database (happy path + empty index case).
- Add a small e2e test for the Docs UI (Playwright or Cypress) that asserts the accordion expands and shows the snippet.
- Consider pagination or cursoring when there are many documents.

If you want, I can:
- Add the tiny unit/integration test for the API now (suggested: xUnit test that seeds an in-memory Postgres-compatible DB or runs the query against a test container).
- Or implement automated e2e steps for the web UI.

---
Generated: October 12, 2025
