# .NET 8 + React — OneDrive → OpenAI → pgvector

**Goal (single line):** Index ~10 MB of Word docs from OneDrive into Postgres+pgvector, expose a lightweight ChatGPT-like web UI that answers questions with explicit document citations. Runtime footprint for the query service ≤1 GB (target 256–512 MiB).

---

## Quick summary (2 sentences)
- Indexer: K8s Job/CronJob (.NET Console) — downloads .docx from OneDrive, extracts text, chunks, embeds via OpenAI, writes to Postgres (pgvector).  
- Query API: ASP.NET Core minimal API — embed query, nearest-neighbor (pgvector) search, build RAG prompt, call OpenAI generation model, return answer + source references.  
- UI: React single-page chat that shows answers and linked references.

---

## Architecture (components)
1. **Indexer (dotnet ConsoleApp)** — runs as Job/CronJob. Uses MS Graph to list/download files, extracts text (OpenXML), chunking, OpenAI embeddings, inserts vectors into Postgres.
2. **Postgres (+ pgvector)** — stores chunks: text, metadata, embedding vector, document id, offsets. Uses your existing Postgres in-cluster.
3. **Query API (ASP.NET Core Minimal API)** — exposes `/query` and `/chat` endpoints, does kNN search and generation, returns citations.
4. **Reranker (optional)** — small service or extra step in API that scores candidates with a smaller/cheaper model.
5. **React Web UI** — chat interface with history, source list, and per-answer download/open links to OneDrive paths.
6. **Kubernetes manifests / Helm chart** — Deployment for API, CronJob or Job for indexer, Service + Ingress, secrets.

---

## Data flow (compact)
1. Indexer enumerates OneDrive files (Microsoft Graph) and downloads changed `.docx`.
2. Extract raw text per paragraph / run. Convert to plain text with page/paragraph offsets.
3. Chunk text (500–1000 chars, 20% overlap). For each chunk call OpenAI embeddings and store embedding + metadata in Postgres.
4. User asks a question in UI → API embeds question → kNN search in Postgres (top-k chunks) → assemble prompt with selected chunks and call OpenAI generation → return answer + `[doc:file#chunk]` citations.

---

## Requirements mapping
- **≤1GB runtime:** API container tuned to `requests.memory=256Mi` / `limits.memory=512Mi`. Use single worker process. Keep DB connections low.  
- **Easy to maintain:** dotnet single-solution with 3 projects (Api, Indexer, WebClient). Clear env-var configuration.  
- **Expose chat UI:** React app served by API or static hosting behind same Ingress.  
- **Native OneDrive:** use Microsoft Graph SDK for .NET + MSAL client creds.  
- **Use existing Postgres:** schema + migration SQL provided.

---

## Postgres schema (SQL)
```sql
-- Enable pgvector (run as superuser)
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS docs_chunks (
  id BIGSERIAL PRIMARY KEY,
  doc_id TEXT NOT NULL,
  filename TEXT NOT NULL,
  chunk_num INT NOT NULL,
  text TEXT NOT NULL,
  metadata JSONB,
  embedding vector(1536),
  created_at TIMESTAMPTZ DEFAULT now()
);

-- Optional: ivfflat for larger datasets (adjust lists)
CREATE INDEX IF NOT EXISTS idx_docs_chunks_embedding ON docs_chunks USING ivfflat (embedding vector_cosine_ops) WITH (lists = 100);
```
**Note:** default embedding dims chosen for `text-embedding-3-small` (1536). You may change if you use different embedding model or trim dims during embedding generation.

---

## Chunking strategy (practical)
- Chunk size: 800–1000 characters (~200 tokens) with 20–30% overlap.  
- Store `doc_id`, `chunk_num`, `char_start`, `char_end` inside metadata JSON to allow precise linking back to file.  
- For very short doc sets, set top-k = 6–8 for retrieval; tune later.

---

## Implementation plan — step by step (high level)
1. **Prereqs**: .NET 8 SDK, Docker, kubectl/helm, k3s cluster, Postgres with pgvector enabled, Azure AD app for OneDrive, OpenAI API key (or Azure OpenAI endpoint & key).
2. **Create repository & solution**
   ```bash
   mkdir platform-qa && cd platform-qa
   dotnet new sln -n platform-qa
   dotnet new web -n Api --framework net8.0
   dotnet new console -n Indexer --framework net8.0
   dotnet new react -n WebClient # or create simple React app
   dotnet sln add Api/Indexer/WebClient
   ```
3. **Add NuGet packages** (core)
   - `Npgsql` (Postgres)  
   - `pgvector` (NuGet package for pgvector support) — or implement mapping via `NpgsqlParameter` if you prefer no extra package
   - `Microsoft.Graph` and `Microsoft.Identity.Client` (MSAL) for OneDrive
   - `DocumentFormat.OpenXml` (text extraction) or `Xceed.Words.NET` (`DocX`) if preferred
   - `System.Text.Json` / `Newtonsoft.Json` as needed
   - `Serilog` (structured logging) optional

4. **Write Indexer** (detailed code skeleton below). Build a Job manifest and run once (or CronJob for delta).  
5. **Write API** (detailed code skeleton below). Keep pool size small. Single-threaded worker, no heavy background tasks.  
6. **React UI** — minimal chat UI: send question to `/query`, display answer and list of sources (click to view OneDrive link).  
7. **Dockerize** both Api and Indexer. Optimize images using `dotnet publish -c Release -o ./bin/Release/net8.0/publish` if needed.  
8. **Kubernetes manifests / Helm values** — Deployment for Api (resources), CronJob for Indexer, Service + Ingress.  
9. **Bring up in k3s**: create secrets (DB_DSN, OPENAI_KEY, AZURE_CLIENT_ID/SECRET), apply manifests.  
10. **Initial indexing**: run indexer Job, verify rows in `docs_chunks`.  
11. **Test**: send sample queries, inspect returned citations.

---

## Key code snippets (condensed)

### A. Indexer — outline (C#)
```csharp
// Indexer: Program.cs (high-level)
using Microsoft.Graph;
using Azure.Identity; // or MSAL
using Npgsql;
using PgVector;

// 1. Authenticate to Graph (ClientCredential)
// 2. List .docx files under configured folder
// 3. For each changed file: download stream, extract text
// 4. Chunk text, call OpenAI Embeddings API (HTTP + HttpClient)
// 5. Insert chunk rows into Postgres with embedding vector

// Example: insert using PgVector package
var vec = new Vector<float>(embeddingArray);
await using var conn = new NpgsqlConnection(connString);
await conn.OpenAsync();
using var cmd = conn.CreateCommand();
cmd.CommandText = @"INSERT INTO docs_chunks (doc_id, filename, chunk_num, text, metadata, embedding) VALUES (@id,@fn,@num,@txt,@meta,@emb)";
cmd.Parameters.AddWithValue("@id", docId);
cmd.Parameters.AddWithValue("@fn", filename);
cmd.Parameters.AddWithValue("@num", chunkNum);
cmd.Parameters.AddWithValue("@txt", chunkText);
cmd.Parameters.AddWithValue("@meta", NpgsqlTypes.NpgsqlDbType.Jsonb, metadataJson);
cmd.Parameters.AddWithValue("@emb", vec);
await cmd.ExecuteNonQueryAsync();
```

### B. Calling OpenAI Embeddings (HTTP example)
```csharp
// simple HttpClient POST to /v1/embeddings
var payload = new { model = "text-embedding-3-small", input = chunkText };
var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings") {
  Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
};
req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", openaiKey);
var resp = await httpClient.SendAsync(req);
resp.EnsureSuccessStatusCode();
var body = await resp.Content.ReadAsStringAsync();
// parse JSON -> embedding array
```

> Use `text-embedding-3-small` as default (1536 dims) for cost-efficiency; switch to `text-embedding-3-large` if higher recall required.

### C. API — minimal Program.cs (ASP.NET Core)
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<NpgsqlConnection>(_ => new NpgsqlConnection(connString));
var app = builder.Build();

app.MapPost("/query", async (QueryRequest req) => {
  var qemb = await Embed(req.Question);
  // use Npgsql to query top-k
  using var cmd = new NpgsqlCommand("SELECT doc_id, chunk_num, text, metadata FROM docs_chunks ORDER BY embedding <-> @qemb LIMIT 8", conn);
  cmd.Parameters.AddWithValue("@qemb", new Vector<float>(qemb));
  // read rows -> build context -> call OpenAI completion/chat
  // return { answer, sources }
});

app.Run();
```

**Tune**: set Npgsql connection pool `MinPoolSize=1;MaxPoolSize=3;` to limit memory.

---

## Dockerfile examples (short)
**Api/Dockerfile**
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS base
WORKDIR /app
COPY ./bin/Release/net8.0/publish/ .
ENTRYPOINT ["dotnet", "Api.dll"]
```
Build with `dotnet publish -c Release -o ./bin/Release/net8.0/publish` before docker build.

**Indexer/Dockerfile** similar but using `mcr.microsoft.com/dotnet/runtime:8.0` base.

---

## Kubernetes hints / resource settings
- Api Deployment: `replicas:1`, `resources.requests.memory: 256Mi`, `limits.memory: 512Mi`, CPU `250m`.
- Indexer Job: `requests.memory: 1Gi`, `limits.memory: 2Gi` (ephemeral).  
- Use `readinessProbe` that checks `/healthz`.
- Store secrets in `kubernetes.secret` and mount as env vars.

---

## Security & secrets
- Do not store OpenAI key in code. Use k8s Secrets.  
- For OneDrive use client credential flow with only `Files.Read.All` for app-level access and store client_secret in k8s Secret.  
- Postgres credentials in Secret; ensure network policy restricts access to DB from only application namespace.

---

## Operational notes
- **Indexing cadence:** initial heavy job, then CronJob daily/weekly, store file ETag/LastModified in `docs_files` table to run delta updates.  
- **Search tuning:** start with cosine L2 or dot-product? Use `vector_cosine_ops` index for cosine; measure recall then tune top-k.  
- **Costs:** OpenAI embeddings + generations will be main cost. Batch embeddings (multi-input) to reduce latency & cost.

---

## Repo layout (suggested)
```
platform-qa/
  README.md
  charts/ (helm chart)
  Api/
    Program.cs
    Controllers/
    Services/OpenAiClient.cs
    Services/PgVectorStore.cs
    Dockerfile
  Indexer/
    Program.cs
    OneDriveClient.cs
    DocxExtractor.cs
    Dockerfile
  WebClient/
    src/
    package.json
  k8s/
    api-deployment.yaml
    indexer-job.yaml
    secrets.yaml.example
  sql/
    00-init-pgvector.sql
```

---

## Step-by-step implementation checklist (actionable)
1. Create Azure AD App (client credentials) — grant Files.Read.All. Save `CLIENT_ID`, `TENANT_ID`, `CLIENT_SECRET`.
2. Enable `vector` extension in Postgres and run `sql/00-init-pgvector.sql`.
3. Implement Indexer `OneDriveClient` using `Microsoft.Graph` and MSAL client credentials.
4. Implement `DocxExtractor` using `DocumentFormat.OpenXml` to extract paragraphs.
5. Implement batching of chunk texts -> call OpenAI embeddings endpoint -> store vectors.
6. Implement Api: `/query` endpoint that does embed -> kNN -> generate -> return answer + sources.
7. Build and publish docker images; push to registry accessible by k3s.
8. Apply k8s manifests; create secrets; run initial indexer Job.
9. Verify by querying local API and inspecting DB rows.

---

## Minimal testing & validation
- Unit test chunking and extraction locally with sample docx.  
- Integration test: small set of docs, run indexer, run `/query` locally, ensure returned sources match expected.

---

## Next steps I can deliver for you (pick one or more)
1. Full repo scaffold with .NET projects + React client + Dockerfiles + Helm chart (ready-to-build).  
2. Complete Indexer implementation (C#) that authenticates to Microsoft Graph, extracts text, chunks, and writes embeddings to Postgres.  
3. Complete API implementation (C#) with kNN SQL and generation call.  
4. Kubernetes manifests and Helm chart tuned for k3s with resource limits.  
5. CI pipeline (GitHub Actions) to build, test, publish images.

Choose which items to generate and I will create them in the repo.

---

*Document version:* 2025-10-11 — concise, implementation-ready.
