# DocDuck Web UI (Ask-only)

A lightweight React + TypeScript frontend to interact with the DocDuck Query API (`/query`, `/docsearch`, `/providers`). The UI provides a single-question "Ask" flow where each query is independent and returns sources; there is no persistent chat history.

## Features
- Provider filtering (type + name)
- Loading indicators while generating answers
- Source list with chunk distances and citations
- Simple dark UI with no external CSS framework

## Development

```bash
cd web
npm install
npm run dev
```
By default the UI expects the API at `http://localhost:5000`. You can override via `.env` or shell:

```bash
VITE_API_BASE=http://localhost:8080 npm run dev
```

## Build
```bash
npm run build
```
Static assets will be in `dist/`.

## Environment Variables
- `VITE_API_BASE` – Base URL for the API (default: `http://localhost:5000`)

## Structure
```
web/
  src/
    types.ts        # Shared TS interfaces matching API models
    api.ts          # Fetch wrappers for endpoints (postQuery, postDocSearch)
    App.tsx         # Root component with provider filter and search depth slider
    components/     # UI components (Ask, SourceList, DocSearchResults, etc.)
```

## Deployment
Serve the built `dist/` directory behind any static server (Nginx, S3 + CloudFront, etc.). Ensure CORS is enabled on the API.

## Next Steps / Enhancements
- Add integration tests for search depth behavior
- Improve source display and document aggregation
- Add a combined Ask + Document viewer flow
- Basic auth / API key header insertion

---
Minimal by design; extend only as needed.
