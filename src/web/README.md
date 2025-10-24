# DocDuck Web UI

A lightweight React + TypeScript frontend for the DocDuck Query API. Provides an intelligent question-answering interface with adaptive search depth and optional streaming of intermediate reasoning steps.

## Features
- **Unified `/query` endpoint** with adaptive intelligence based on search depth (1-5)
- **Streaming mode**: See intermediate thinking steps in real-time
- **Doc Search**: Find documents without AI generation (fast, no token cost)
- Provider filtering (multi-select)
- Search depth control (1=simple, 5=deep with multiple refinements)
- Clean Material-UI interface with dark/light mode

## Search Modes

### Ask DocDuck (Intelligent Q&A)
- **Depth 1**: Simple mode - single search + answer generation
- **Depth 2-3**: Smart mode - 2 attempts with query refinement
- **Depth 4**: Advanced mode - 3 attempts with answerability checks
- **Depth 5**: Deep mode - 4 attempts, multiple refinements for best results
- **"Show thinking" toggle**: Stream intermediate reasoning steps via SSE

### Doc Search
- Fast document retrieval without AI generation
- Returns top 5 most relevant documents grouped by doc_id
- No token cost, ideal for browsing available content

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
    api.ts          # Fetch wrappers (postQuery, postQueryStream, postDocSearch)
    App.tsx         # Root component with provider filter and theme toggle
    components/
      Ask.tsx                # Main Q&A interface (landing + chat modes)
      SourceList.tsx         # Source citations display
      DocSearchResults.tsx   # Document search results
      EnvironmentBanner.tsx  # Health/status indicator
```

## Deployment
Serve the built `dist/` directory behind any static server (Nginx, S3 + CloudFront, etc.). Ensure CORS is enabled on the API.

---
Minimal by design; extend only as needed.
