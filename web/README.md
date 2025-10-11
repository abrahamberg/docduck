# DocDuck Web UI (Minimal Chat & Ask)

A lightweight React + TypeScript frontend to interact with the DocDuck Query API (`/chat`, `/query`, `/providers`). Provides two modes:

- Chat: iterative conversation with context citations.
- Ask: single question response with sources.

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
    api.ts          # Fetch wrappers for endpoints
    App.tsx         # Root component with tabs & provider filter
    components/     # UI components (Chat, Ask, SourceList, etc.)
```

## Deployment
Serve the built `dist/` directory behind any static server (Nginx, S3 + CloudFront, etc.). Ensure CORS is enabled on the API.

## Next Steps / Enhancements
- Streaming responses (server-sent events) for progressive answer display
- Persistent chat history (localStorage)
- Copy citation / chunk expand
- Basic auth / API key header insertion

---
Minimal by design; extend only as needed.
