# Frontend Quickstart (DocDuck Web UI)

A minimal React interface is provided in `web/` to interact with the Query API. It supports both `/chat` and `/query` endpoints and provider filtering.

## Prerequisites
- Node.js 18+
- Running DocDuck API (e.g. `cd Api && dotnet run` listening on `http://localhost:5000`)

## Start the UI
```bash
cd web
npm install
npm run dev
```
Visit `http://localhost:5173` (default Vite port).

## Environment Variable
Override API base if different:
```bash
VITE_API_BASE=http://localhost:8080 npm run dev
```

## Usage
1. Ensure providers are indexed (run Indexer) so queries have content.
2. Open the UI, select provider type/name filters (optional).
3. Use Chat tab for multi-turn conversation; Ask tab for single question.
4. Inspect sources list for each answer to view citations.

## Files
- `web/src/types.ts` – TS interfaces mapping to API models.
- `web/src/api.ts` – Fetch helper functions.
- `web/src/components/*` – UI components (Chat, Ask, ProviderFilter, SourceList, LoadingIndicator).
- `web/src/App.tsx` – Root orchestrator (tabs + provider filter).

## Customization
Style changes: adjust inline styles or introduce a CSS file.
Add authentication: inject headers in `api.ts` (e.g., API key).
Streaming: Replace `postChat/postQuery` with fetch + ReadableStream consumption (server must support streaming).

## Deployment
```bash
cd web
npm run build
```
Serve `web/dist/` via any static host. Ensure API CORS allows your origin.

### Docker Compose
The root `docker-compose-multi-provider.yml` now includes a `web` service. Start everything:
```bash
docker-compose -f docker-compose-multi-provider.yml up --build
```
Access the UI at http://localhost:5173 (served by nginx container) pointing to the `api` service internally.

To customize API base, change the build arg in the compose file under `web.args.VITE_API_BASE`.

## Troubleshooting
- Empty sources: Ensure indexing completed and DB populated.
- CORS errors: Confirm API sets permissive CORS (already enabled in `Program.cs`).
- 500 errors: Check API logs for underlying exception.

---
Minimal UI intended for rapid iteration—extend as features grow.
