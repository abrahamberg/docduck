# .NET 8 — Multi-Provider Document Indexer → OpenAI → pgvector

**Goal:** Index documents from multiple sources (OneDrive, S3, Local Files) into Postgres+pgvector with automatic lifecycle management, expose a lightweight RAG API that answers questions with explicit document citations. Runtime footprint for the query service ≤512 MiB.

---

## Quick summary
- **Indexer:** K8s CronJob/Job (.NET Console) — modular provider architecture supporting OneDrive (Microsoft Graph), AWS S3, and local filesystem. Downloads documents, extracts text, chunks, embeds via OpenAI, writes to Postgres (pgvector). Includes smart change detection (ETag-based) and automatic cleanup of deleted files.
- **Query API:** ASP.NET Core minimal API — embed query, nearest-neighbor (pgvector) search with provider filtering, build RAG prompt, call OpenAI generation model, return answer + source references.  
- **Provider System:** Plugin-based `IDocumentProvider` interface allows easy addition of new document sources. YAML-based configuration for simple Docker/Kubernetes deployment. React — OneDrive → OpenAI → pgvector

**Goal (single line):** Index ~10 MB of Word docs from OneDrive into Postgres+pgvector, expose a lightweight ChatGPT-like web UI that answers questions with explicit document citations. Runtime footprint for the query service ≤1 GB (target 256–512 MiB).

---

## Quick summary (2 sentences)
- Indexer: K8s Job/CronJob (.NET Console) — downloads .docx from OneDrive, extracts text, chunks, embeds via OpenAI, writes to Postgres (pgvector).  
- Query API: ASP.NET Core minimal API — embed query, nearest-neighbor (pgvector) search, build RAG prompt, call OpenAI generation model, return answer + source references.  
- UI: React single-page chat that shows answers and linked references.

---

## Architecture (components)
1. **Indexer (dotnet ConsoleApp)** — runs as Job/CronJob. Multi-provider architecture:
   - **OneDrive Provider:** Uses MS Graph API (business/personal accounts) with client secret or username/password auth
   - **S3 Provider:** AWS S3 bucket access with IAM role or access key support
   - **Local Provider:** Filesystem scanning with exclusion patterns
   - **Extensible:** Custom providers via `IDocumentProvider` interface
   - Includes smart change detection (ETags), automatic orphan cleanup, and force reindex capability
2. **Postgres (+ pgvector)** — stores chunks with embeddings, file tracking (ETags), and provider metadata. Schema includes:
   - `docs_chunks` — text chunks with 1536-dim vectors, provider tracking
   - `docs_files` — file state tracking (ETag, last_modified) for change detection
   - `providers` — registered provider metadata and sync timestamps
3. **Query API (ASP.NET Core Minimal API)** — exposes:
   - `/query` — simple Q&A with optional provider filtering
   - `/chat` — conversation with history support
   - `/providers` — list active providers (for frontend integration)
   - Does kNN search (cosine distance), RAG generation, returns citations
4. **Configuration System** — YAML-based with environment variable expansion, supports Docker Compose, Kubernetes ConfigMaps/Secrets
5. **Kubernetes manifests** — CronJob for scheduled indexing, Deployment for API, ConfigMaps, Secrets, ServiceAccount (for S3 IAM roles)

---

## Data flow

### Indexing Flow
1. **Provider Discovery:** Each enabled provider (OneDrive, S3, Local) lists available documents
2. **Change Detection:** Compare document ETags with `docs_files` table to identify new/updated/deleted files
3. **Orphan Cleanup:** Remove vectors for deleted files (if `CleanupOrphanedDocuments: true`)
4. **Processing:**
   - Download changed `.docx` files
   - Extract plain text (OpenXML SDK)
   - Chunk text (configurable size/overlap, default 500 chars with 50 char overlap)
   - Generate embeddings via OpenAI (`text-embedding-3-small`, 1536 dims)
   - Upsert chunks to Postgres (replaces old chunks on update)
   - Update file tracking with new ETag
5. **Sync Tracking:** Update provider `last_sync_at` timestamp

### Query Flow
1. User sends question to `/query` or `/chat` endpoint
2. API embeds question using same OpenAI model
3. kNN search in Postgres (cosine distance, top-k chunks)
   - Optional: filter by `providerType` and/or `providerName`
4. Assemble RAG prompt with retrieved chunks as context
5. Call OpenAI generation model (`gpt-4o-mini`)
6. Return answer with citations in format `[doc:filename#chunk]`

---

## Requirements mapping
- **≤512 MiB runtime:** API container tuned to `requests.memory=256Mi` / `limits.memory=512Mi`. Single worker process. Minimal DB connection pool.
- **Multi-Provider Support:** Modular plugin architecture with `IDocumentProvider` interface. Currently implemented: OneDrive, S3, Local.
- **Easy Configuration:** YAML-based config (`appsettings.yaml`) with environment variable expansion. Works seamlessly with Docker Compose and Kubernetes ConfigMaps/Secrets.
- **File Lifecycle Management:** Automatic handling of new, updated, deleted, and renamed files via ETag tracking and orphan cleanup.
- **Provider Filtering:** API supports filtering search results by provider type/name. Frontend can discover active providers via `/providers` endpoint.
- **Native Integrations:**
  - **OneDrive:** Microsoft Graph SDK with MSAL (client secret or user password auth)
  - **S3:** AWS SDK with IAM role support (EKS ServiceAccount) or access keys
  - **Local:** Filesystem scanning with glob pattern exclusions
- **Use existing Postgres:** Complete schema with provider tracking, file state, and vector indexes provided.

---

## Postgres schema (SQL)
```sql
-- Enable pgvector (run as superuser)
CREATE EXTENSION IF NOT EXISTS vector;

-- Provider registry
CREATE TABLE IF NOT EXISTS providers (
    provider_type TEXT NOT NULL,
    provider_name TEXT NOT NULL,
    is_enabled BOOLEAN NOT NULL DEFAULT true,
    registered_at TIMESTAMPTZ DEFAULT now(),
    last_sync_at TIMESTAMPTZ,
    metadata JSONB,
    PRIMARY KEY (provider_type, provider_name)
);

-- Document chunks with embeddings
CREATE TABLE IF NOT EXISTS docs_chunks (
    id BIGSERIAL PRIMARY KEY,
    doc_id TEXT NOT NULL,
    filename TEXT NOT NULL,
    provider_type TEXT NOT NULL,
    provider_name TEXT NOT NULL,
    chunk_num INT NOT NULL,
    text TEXT NOT NULL,
    metadata JSONB,
    embedding vector(1536),
    created_at TIMESTAMPTZ DEFAULT now(),
    CONSTRAINT unique_doc_chunk UNIQUE (doc_id, chunk_num)
);

-- File state tracking (ETag-based change detection)
CREATE TABLE IF NOT EXISTS docs_files (
    doc_id TEXT NOT NULL,
    provider_type TEXT NOT NULL,
    provider_name TEXT NOT NULL,
    filename TEXT NOT NULL,
    etag TEXT NOT NULL,
    last_modified TIMESTAMPTZ NOT NULL,
    relative_path TEXT,
    PRIMARY KEY (doc_id, provider_type, provider_name)
);

-- Indexes
CREATE INDEX IF NOT EXISTS docs_chunks_embedding_idx 
    ON docs_chunks USING ivfflat (embedding vector_cosine_ops) WITH (lists = 100);
CREATE INDEX IF NOT EXISTS docs_chunks_doc_id_idx ON docs_chunks(doc_id);
CREATE INDEX IF NOT EXISTS docs_chunks_filename_idx ON docs_chunks(filename);
CREATE INDEX IF NOT EXISTS docs_chunks_provider_idx ON docs_chunks(provider_type, provider_name);
```

**Key Features:**
- `UNIQUE (doc_id, chunk_num)` enables upsert on file updates
- `docs_files` table tracks ETags for efficient change detection
- `providers` table maintains provider metadata and sync timestamps
- IVFFlat index optimized for cosine similarity search

---

## Chunking & Lifecycle Strategy

### Chunking
- **Chunk size:** Configurable (default 500 chars, ~125 tokens) with configurable overlap (default 50 chars, 10%)
- **Metadata:** Each chunk stores `doc_id`, `chunk_num`, `char_start`, `char_end`, `provider_type`, `provider_name`, `etag`, `last_modified`, `relative_path`
- **Top-k retrieval:** Default 6 chunks; tune based on dataset size and accuracy requirements

### File Lifecycle (Automatic)
- **New files:** Indexed with embeddings
- **Updated files:** Re-indexed when ETag changes (old chunks replaced via upsert)
- **Unchanged files:** Skipped based on ETag comparison (efficient incremental indexing)
- **Deleted files:** Vectors automatically removed if `CleanupOrphanedDocuments: true` (default)
- **Renamed files:**
  - OneDrive/S3: Stable doc_id → treated as update
  - Local: Path-based doc_id → treated as delete + add

### Configuration Options
```yaml
Chunking:
  ChunkSize: 500
  ChunkOverlap: 50
  CleanupOrphanedDocuments: true   # Auto-remove deleted files
  ForceFullReindex: false          # Incremental by default
```

---

## Implementation plan — step by step

### Prerequisites
- .NET 8 SDK
- Docker & Docker Compose (or Kubernetes cluster)
- PostgreSQL with pgvector extension enabled
- OpenAI API key
- Optional: Azure AD app for OneDrive, AWS credentials for S3

### Quick Start (Local Development)
```bash
# 1. Clone repository
git clone <repo-url> && cd docduck

# 2. Set up database
./db-util.sh init

# 3. Configure providers (edit appsettings.yaml)
cd Indexer
cp appsettings.yaml.example appsettings.yaml
# Edit: set OpenAI key, enable/configure desired providers

# 4. Run indexer
dotnet run

# 5. Run API
cd ../Api
dotnet run

# 6. Test
curl -X POST http://localhost:5000/query \
  -H "Content-Type: application/json" \
  -d '{"question":"What is the project about?"}'
```

### Docker Compose Deployment
```bash
# 1. Copy env file
cp env.multi-provider.example .env
# Edit .env with your credentials

# 2. Start all services
docker-compose -f docker-compose-multi-provider.yml up -d

# 3. Run initial indexing
docker-compose run indexer

# 4. Query API
curl http://localhost:8080/providers
```

### Kubernetes Deployment
```bash
# 1. Create secrets
kubectl create secret generic docduck-secrets \
  --from-literal=OPENAI_API_KEY=<key> \
  --from-literal=DB_CONNECTION_STRING=<conn>

# 2. Apply manifests
kubectl apply -f k8s/indexer-multi-provider.yaml

# 3. Trigger indexing job
kubectl create job --from=cronjob/docduck-indexer manual-index

# 4. Check logs
kubectl logs -l job-name=manual-index -f
```

### NuGet Packages (Current)
- `Npgsql` 8.0.3 — Postgres driver
- `Pgvector` 0.2.2 — pgvector support
- `Microsoft.Graph` 5.94.0 — OneDrive/SharePoint access
- `Azure.Identity` 1.13.1 — MSAL authentication
- `AWSSDK.S3` 3.7.408.7 — S3 provider
- `DocumentFormat.OpenXml` 3.3.0 — DOCX text extraction
- `NetEscapades.Configuration.Yaml` 3.1.0 — YAML configuration

---

## Key code snippets

### A. Provider Interface
```csharp
public interface IDocumentProvider
{
    string ProviderType { get; }
    string ProviderName { get; }
    bool IsEnabled { get; }
    
    Task<IReadOnlyList<ProviderDocument>> ListDocumentsAsync(CancellationToken ct);
    Task<Stream> DownloadDocumentAsync(string documentId, CancellationToken ct);
    Task<ProviderMetadata> GetMetadataAsync(CancellationToken ct);
}
```

### B. Multi-Provider Indexing Flow
```csharp
// MultiProviderIndexerService.cs (simplified)
var enabledProviders = _providers.Where(p => p.IsEnabled).ToList();

foreach (var provider in enabledProviders)
{
    // Register provider
    var metadata = await provider.GetMetadataAsync(ct);
    await _vectorRepository.RegisterProviderAsync(metadata, ct);
    
    // List documents
    var documents = await provider.ListDocumentsAsync(ct);
    
    // Cleanup orphaned documents (deleted files)
    if (_chunkingOptions.CleanupOrphanedDocuments)
    {
        var currentDocIds = documents.Select(d => d.DocumentId).ToList();
        await _vectorRepository.CleanupOrphanedDocumentsAsync(
            provider.ProviderType, provider.ProviderName, currentDocIds, ct);
    }
    
    foreach (var doc in documents)
    {
        // Skip if unchanged (ETag match)
        if (await _vectorRepository.IsDocumentIndexedAsync(
            doc.DocumentId, doc.ETag, provider.ProviderType, provider.ProviderName, ct))
        {
            continue;
        }
        
        // Download, extract, chunk, embed, upsert
        var stream = await provider.DownloadDocumentAsync(doc.DocumentId, ct);
        var text = await _docxExtractor.ExtractPlainTextAsync(stream, ct);
        var chunks = _textChunker.Chunk(text);
        var embeddings = await _embeddingsClient.EmbedBatchedAsync(chunks, ct);
        
        await _vectorRepository.InsertOrUpsertChunksAsync(
            chunks, embeddings, provider.ProviderType, provider.ProviderName, ct);
        
        await _vectorRepository.UpdateFileTrackingAsync(
            doc.DocumentId, doc.Filename, doc.ETag, doc.LastModified, 
            provider.ProviderType, provider.ProviderName, ct);
    }
}
```

### C. Upsert with Conflict Resolution
```csharp
// VectorRepository.cs
const string sql = @"
    INSERT INTO docs_chunks (doc_id, filename, provider_type, provider_name, 
                             chunk_num, text, metadata, embedding)
    VALUES (@doc_id, @filename, @provider_type, @provider_name, 
            @chunk_num, @text, @metadata::jsonb, @embedding)
    ON CONFLICT (doc_id, chunk_num)
    DO UPDATE SET
        filename = EXCLUDED.filename,
        text = EXCLUDED.text,
        embedding = EXCLUDED.embedding,
        created_at = now()";

cmd.Parameters.AddWithValue("@embedding", new Vector(embeddingArray));
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

### D. API with Provider Filtering
```csharp
// Api/Program.cs (simplified)
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<VectorSearchService>();
var app = builder.Build();

// List active providers
app.MapGet("/providers", async (VectorSearchService search) =>
{
    var providers = await search.GetProvidersAsync();
    return Results.Ok(new { providers });
});

// Query with optional provider filtering
app.MapPost("/query", async (QueryRequest req, VectorSearchService search) =>
{
    var embedding = await search.EmbedQuestionAsync(req.Question);
    
    // Search with optional provider filters
    var results = await search.SearchAsync(
        embedding, 
        topK: req.TopK ?? 6,
        providerType: req.ProviderType,
        providerName: req.ProviderName);
    
    var answer = await search.GenerateAnswerAsync(req.Question, results);
    return Results.Ok(new { answer, sources = results });
});

app.Run();
```

**Query SQL with Provider Filtering:**
```sql
SELECT doc_id, filename, chunk_num, text, metadata,
       embedding <=> @embedding AS distance
FROM docs_chunks
WHERE (@provider_type IS NULL OR provider_type = @provider_type)
  AND (@provider_name IS NULL OR provider_name = @provider_name)
ORDER BY embedding <=> @embedding
LIMIT @top_k
```

**Tune**: Npgsql connection pool `MinPoolSize=1;MaxPoolSize=3;` to limit memory.

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

### Indexing Modes
- **Run-once (Current):** Kubernetes CronJob triggers indexer on schedule (e.g., every 6 hours). Clean exit after each run.
- **Daemon mode (Alternative):** Long-running process with internal scheduler. See `ScheduledIndexerService.cs` and `docs/guides/execution-modes.md`

### Indexing Cadence
- **Initial run:** Heavy job processes all files
- **Subsequent runs:** Incremental updates based on ETag comparison
  - New files → indexed
  - Updated files (ETag changed) → re-indexed
  - Unchanged files (same ETag) → skipped
  - Deleted files → removed (if cleanup enabled)
- **Recommended:** CronJob every 6-12 hours for most use cases

### File Lifecycle
- **Change detection:** ETag-based (OneDrive cTag, S3 ETag, Local SHA256 hash)
- **Orphan cleanup:** Automatic removal of deleted file vectors (configurable)
- **Force reindex:** `ForceFullReindex: true` for complete refresh (testing/troubleshooting)
- See `docs/guides/file-lifecycle.md` for comprehensive details

### Search Tuning
- **Index:** IVFFlat with `vector_cosine_ops` (cosine similarity)
- **Top-k:** Start with 6-8, tune based on accuracy/latency trade-offs
- **Provider filtering:** Filter by specific providers to scope search results
- **Lists parameter:** Adjust IVFFlat lists based on dataset size (recommended: rows/1000)

### Cost Optimization
- **Embeddings:** Batch multiple chunks in single API call (up to 100 chunks)
- **Generation:** Use `gpt-4o-mini` (cheaper) vs `gpt-4` (higher quality)
- **Incremental indexing:** Skip unchanged files to reduce embedding API calls
- **Provider control:** Enable/disable providers to limit scope

---

## Repo layout (actual)
```
docduck/
  README.md
  docduck.sln
  schema.sql
  docker-compose-multi-provider.yml
  env.multi-provider.example
  Api/
    Program.cs
    Dockerfile
    appsettings.json
    Models/
      QueryModels.cs
    Services/
      OpenAiClient.cs
      VectorSearchService.cs
    Options/
      DbOptions.cs
      OpenAiOptions.cs
      SearchOptions.cs
  Indexer/
    Program.cs
    ProgramDaemon.cs              # Alternative: daemon mode
    ScheduledIndexerService.cs    # Background scheduler
    Dockerfile
    appsettings.yaml
    MultiProviderIndexerService.cs
    Providers/
      IDocumentProvider.cs        # Core interface
      ProviderModels.cs
      OneDriveProvider.cs
      LocalProvider.cs
      S3Provider.cs
    Services/
      DocxExtractor.cs
      TextChunker.cs
      OpenAiEmbeddingsClient.cs
      VectorRepository.cs
    Options/
      ProvidersConfiguration.cs
      OneDriveProviderConfig.cs
      LocalProviderConfig.cs
      S3ProviderConfig.cs
      ChunkingOptions.cs
      DbOptions.cs
      OpenAiOptions.cs
  Indexer.Tests/
    VectorRepositoryTests.cs
    FileLifecycleTests.cs
  k8s/
    api-deployment.yaml
    indexer-multi-provider.yaml   # CronJob with ConfigMap/Secrets
  docs/
    architecture.md               # This file
    changelog.md
    guides/
      quickstart.md
      developer-guide.md
      multi-provider-setup.md
      file-lifecycle.md
      file-lifecycle-quickref.md
      execution-modes.md
    database/
      pgvector.md
      pgvector-quickref.md
    reports/
      api-implementation.md
      multi-provider-implementation.md
```

---

## Implementation checklist

### ✅ Completed
1. ✅ Multi-provider architecture with `IDocumentProvider` interface
2. ✅ OneDrive provider (Microsoft Graph + MSAL, business/personal accounts)
3. ✅ S3 provider (AWS SDK, IAM role + access key support)
4. ✅ Local filesystem provider (recursive scanning, exclusion patterns)
5. ✅ YAML configuration system with env var expansion
6. ✅ File lifecycle management (ETag tracking, orphan cleanup, force reindex)
7. ✅ Database schema with provider tracking, file state, and vector indexes
8. ✅ VectorRepository with upsert logic and cleanup methods
9. ✅ Query API with `/query`, `/chat`, `/providers` endpoints
10. ✅ Provider filtering in search queries
11. ✅ Docker Compose configuration for all-in-one deployment
12. ✅ Kubernetes manifests with CronJob, ConfigMap, Secrets
13. ✅ Comprehensive documentation (setup, lifecycle, execution modes)

### 🚀 Ready to Deploy
**Local Development:**
```bash
cd Indexer && dotnet run    # Run indexer
cd Api && dotnet run        # Run API
```

**Docker Compose:**
```bash
docker-compose -f docker-compose-multi-provider.yml up
```

**Kubernetes:**
```bash
kubectl apply -f k8s/indexer-multi-provider.yaml
```

---

## Testing & validation

### Unit Tests
- `Indexer.Tests/VectorRepositoryTests.cs` — Database operations
- `Indexer.Tests/FileLifecycleTests.cs` — Change detection and cleanup

### Integration Testing
```bash
# 1. Set up test database
./db-util.sh init

# 2. Run indexer with sample docs
cd Indexer
dotnet run

# 3. Verify database
./db-util.sh stats

# 4. Test API queries
curl -X POST http://localhost:5000/query \
  -H "Content-Type: application/json" \
  -d '{"question":"test query"}'
```

### Manual Testing Scenarios
1. **New file:** Add document to provider → run indexer → verify indexed
2. **Updated file:** Modify document → run indexer → verify re-indexed (old chunks replaced)
3. **Deleted file:** Remove document from provider → run indexer → verify vectors removed
4. **Provider filtering:** Query with `providerType`/`providerName` → verify results scoped

---

## Production Readiness

### Features Implemented
✅ Multi-provider document indexing  
✅ Automatic file lifecycle management  
✅ ETag-based change detection  
✅ Orphan cleanup for deleted files  
✅ Force reindex capability  
✅ YAML configuration with env vars  
✅ Docker & Kubernetes deployment  
✅ Provider filtering in search  
✅ Health checks and structured logging  
✅ Memory-optimized API (≤512 MiB)  
✅ Graceful shutdown (SIGTERM handling)  

### Deployment Options
- **Local:** Direct `dotnet run` for development
- **Docker Compose:** All-in-one deployment for testing
- **Kubernetes:** CronJob-based scheduled indexing (production)

### Documentation
- 📖 Quick Start: `docs/guides/quickstart.md`
- 📖 Setup Guide: `docs/guides/multi-provider-setup.md`
- 📖 File Lifecycle: `docs/guides/file-lifecycle.md`
- 📖 Execution Modes: `docs/guides/execution-modes.md`
- 📖 Developer Guide: `docs/guides/developer-guide.md`

---

*Document version:* 2025-10-11 — Multi-provider architecture with complete file lifecycle management.
