# API Usage

Base URL (default dev): `http://localhost:5000`

## Health
```bash
curl http://localhost:5000/health
```
Response:
```json
{
  "status":"healthy",
  "chunks": 1200,
  "documents": 42,
  "openAiKeyPresent": true,
  "dbConnectionPresent": true
}
```

## Providers
```bash
curl http://localhost:5000/providers
```

## Query
POST `/query`
```json
{
  "question": "What is DocDuck?",
  "topK": 8,
  "providerType": "onedrive",
  "providerName": "corpdrive"
}
```
Minimal:
```bash
curl -X POST http://localhost:5000/query \
  -H 'Content-Type: application/json' \
  -d '{"question":"Explain the architecture"}'
```

Response (abridged):
```json
{
  "answer": "DocDuck indexes documents ...",
  "sources": [
    {
      "docId":"...",
      "filename":"architecture.md",
      "text":"Snippet...",
      "distance":0.12,
      "providerType":"local",
      "providerName":"localdocs"
    }
  ],
  "tokensUsed": 1543
}
```

## Document Search
POST `/docsearch` groups by document:
```bash
curl -X POST http://localhost:5000/docsearch \
  -H 'Content-Type: application/json' \
  -d '{"question":"vector index"}'
```

## Chat (batched)
```bash
curl -X POST http://localhost:5000/chat \
  -H 'Content-Type: application/json' \
  -d '{"message":"Summarize the system"}'
```

## Chat (streaming SSE)
Set `"streamSteps": true`:
```bash
curl -N -X POST http://localhost:5000/chat \
  -H 'Content-Type: application/json' \
  -d '{"message":"Explain chunking","streamSteps":true}'
```
Each event line begins with `data: {JSON}`.

Update types (example):
```json
{"type":"step","message":"Embedding question"}
{"type":"answer","message":"Final answer...","final":true}
```

## Error Handling
- 400 for validation (missing question)
- 500 generic problem JSON body

## Rate Limiting
Not built-in yet. Use reverse proxy / gateway (NGINX, Traefik, API Gateway) as needed.

## Auth
Currently open for query endpoints; admin endpoints (future) secured via secret. Harden with network policies or add auth middleware (see roadmap).

## Next
- Internals: [Search & RAG](../developer/search-rag.md)
