# DocDuck - Multi-Provider Document Search with RAG

A complete .NET 8 solution for indexing documents from multiple sources (OneDrive, S3, Local Files) and providing AI-powered search with citations using PostgreSQL + pgvector and OpenAI.

## 🎯 Overview

DocDuck consists of two main components:

1. **Indexer** - Console application that indexes documents from multiple providers with smart change detection and lifecycle management
2. **Query API** - Minimal ASP.NET Core API providing RAG (Retrieval-Augmented Generation) endpoints for semantic search and chat

## ✨ Features

### Indexer
- **Multi-Provider Architecture**: Modular plugin-based system supporting multiple document sources
  - **OneDrive**: Microsoft Graph API integration (business/personal accounts)
  - **S3**: AWS S3 bucket indexing with IAM role support
  - **Local**: Filesystem scanning with exclusion patterns
  - **Extensible**: Easy to add custom providers via `IDocumentProvider` interface
- **YAML Configuration**: Simple configuration for Docker, Kubernetes, and local development
- **Smart Change Detection**: ETag-based tracking to skip unchanged files
- **File Lifecycle Management**: Automatic handling of new, updated, deleted, and renamed files
- **Orphan Cleanup**: Removes vectors for deleted files to keep database in sync
- **Force Reindex**: Optional complete refresh for schema changes
- **Modular Text Extraction**: Pluggable extractors for multiple file formats (DOCX, TXT, MD, PDF*, etc.) via `ITextExtractor` interface
- **Smart Chunking**: Splits text into overlapping chunks with configurable size and overlap
- **OpenAI Embeddings**: Generates vector embeddings using OpenAI API (default: text-embedding-3-small, 1536 dimensions)
- **PostgreSQL Storage**: Stores chunks and embeddings in Postgres with pgvector extension
- **K8s Ready**: Handles SIGTERM gracefully, runs as a CronJob/Job, exits with proper codes

*PDF extraction requires additional NuGet package (PdfPig, Docnet.Core, etc.)

### Query API
- **RAG Pipeline**: Embed question → kNN search → Generate answer with citations
- **Dual Endpoints**: `/query` (simple Q&A) and `/chat` (conversation with history)
- **Provider Filtering**: Filter search by provider type/name
- **Active Providers API**: `/providers` endpoint exposes enabled providers to frontends
- **Vector Search**: Fast similarity search using pgvector with cosine distance
- **Memory Efficient**: Optimized for ≤512 MiB runtime footprint
- **Production Ready**: Health checks, structured logging, Docker & Kubernetes deployment

## Prerequisites

- .NET 8 SDK
- PostgreSQL with pgvector extension installed
- Microsoft Azure AD app registration with Graph API permissions
- OpenAI API key

## Database Setup

### Quick Setup (Recommended)

Use the included database utility script:

```bash
# Test connection
./db-util.sh test

# Initialize schema (creates tables and indexes)
./db-util.sh init

# Verify pgvector extension
./db-util.sh check

# View statistics
./db-util.sh stats
```

### Manual Setup

Or run the SQL directly:

```bash
psql -h localhost -U postgres -d vectors -f sql/01-init-schema.sql
```

The schema includes:
- `docs_chunks` table with vector embeddings (1536 dimensions)
- `docs_files` table for tracking file state (ETags)
- IVFFlat index for fast similarity search
- Supporting indexes for metadata queries

See [sql/01-init-schema.sql](sql/01-init-schema.sql) for details.

## Configuration

All configuration is read from environment variables:

### Microsoft Graph (Required)

**Choose one authentication mode:**

#### Option 1: Client Secret (Business OneDrive - App-Only) - Recommended for production

- `GRAPH_AUTH_MODE=ClientSecret` (default)
- `GRAPH_ACCOUNT_TYPE=business` (default)
- `GRAPH_TENANT_ID` - Azure AD tenant ID
- `GRAPH_CLIENT_ID` - Application (client) ID
- `GRAPH_CLIENT_SECRET` - Client secret value
- `GRAPH_DRIVE_ID` - OneDrive drive ID (preferred) **OR**
- `GRAPH_SITE_ID` - SharePoint site ID (alternative)
- `GRAPH_FOLDER_PATH` - Folder path to index (default: `/Shared Documents/Docs`)

#### Option 2: Username/Password (Personal OneDrive or Delegated Access) - For development/personal use

- `GRAPH_AUTH_MODE=UserPassword`
- `GRAPH_ACCOUNT_TYPE=personal` (for personal OneDrive) or `business` (for delegated business access)
- `GRAPH_CLIENT_ID` - Application (client) ID
- `GRAPH_USERNAME` - Your email (e.g., `yourname@outlook.com`)
- `GRAPH_PASSWORD` - Your password
- `GRAPH_FOLDER_PATH` - Folder path to index (default: `/Documents` for personal OneDrive)

**⚠️ Note**: Username/Password authentication:
- Does NOT support accounts with MFA enabled
- Is deprecated by Microsoft
- Works for personal OneDrive accounts without MFA
- Use for development/testing only

### OpenAI (Required)

- `OPENAI_API_KEY` - OpenAI API key
- `OPENAI_BASE_URL` - API base URL (default: `https://api.openai.com/v1`, or use Azure OpenAI endpoint)
- `OPENAI_EMBED_MODEL` - Embedding model (default: `text-embedding-3-small`)

### Database (Required)

- `DB_CONNECTION_STRING` - PostgreSQL connection string  
  Example: `Host=localhost;Port=5432;Database=vectors;Username=user;Password=pass;MinPoolSize=1;MaxPoolSize=3`

### Chunking (Optional)

- `CHUNK_SIZE` - Characters per chunk (default: `1000`)
- `CHUNK_OVERLAP` - Overlap between chunks (default: `200`)
- `MAX_FILES` - Limit number of files to process (for testing)
- `BATCH_SIZE` - Embeddings batch size (default: `16`)

## Running Locally

### Indexer

#### 1. Set environment variables

```bash
export GRAPH_TENANT_ID="your-tenant-id"
export GRAPH_CLIENT_ID="your-client-id"
export GRAPH_CLIENT_SECRET="your-client-secret"
export GRAPH_DRIVE_ID="your-drive-id"
export GRAPH_FOLDER_PATH="/Shared Documents/Docs"

export OPENAI_API_KEY="sk-..."
export OPENAI_BASE_URL="https://api.openai.com/v1"

export DB_CONNECTION_STRING="Host=localhost;Database=vectors;Username=postgres;Password=password;MinPoolSize=1;MaxPoolSize=3"

# Optional: limit to 3 files for testing
export MAX_FILES=3
```

#### 2. Build and run indexer

```bash
cd Indexer
dotnet build
dotnet run
```

#### 3. Monitor and verify

```bash
# Check database statistics
./db-util.sh stats

# View recently indexed documents
./db-util.sh recent

# Run health check
./db-util.sh health
```

### Query API

#### 1. Set environment variables

```bash
export DB_CONNECTION_STRING="Host=localhost;Database=vectors;Username=postgres;Password=password;MinPoolSize=1;MaxPoolSize=5"
export OPENAI_API_KEY="sk-..."
```

#### 2. Run the API

```bash
cd Api
dotnet run
```

API starts on `http://localhost:5000`

#### 3. Test the API

```bash
# Health check
curl http://localhost:5000/health

# Query
curl -X POST http://localhost:5000/query \
  -H "Content-Type: application/json" \
  -d '{"question": "What is this project about?"}'

# Or use the test script
./test-api.sh
```

### 4. Run tests

```bash
cd Indexer.Tests
dotnet test

# For integration tests (requires PostgreSQL)
export TEST_DB_CONNECTION_STRING="Host=localhost;Database=vectors_test;Username=postgres;Password=password"
dotnet test
```

## Database Management

The included `db-util.sh` script provides convenient database management commands:

```bash
./db-util.sh test        # Test database connection
./db-util.sh init        # Initialize schema
./db-util.sh check       # Verify pgvector extension
./db-util.sh stats       # Show statistics
./db-util.sh recent      # Show recently indexed docs
./db-util.sh health      # Run health check
./db-util.sh maintain    # Run VACUUM/ANALYZE
./db-util.sh backup      # Create backup
./db-util.sh shell       # Open psql shell
```

For advanced queries and maintenance, see:
- [sql/02-queries.sql](sql/02-queries.sql) - Analysis and search queries
- [sql/03-maintenance.sql](sql/03-maintenance.sql) - Maintenance tasks
- [docs/database/pgvector.md](docs/database/pgvector.md) - Detailed pgvector documentation

## Architecture

### Data Flow

```
Indexer Pipeline:
OneDrive → Download .docx → Extract Text → Chunk → 
Generate Embeddings → Store in PostgreSQL (pgvector)

Query Pipeline:
User Question → Embed Question → kNN Search (pgvector) → 
Build Context → OpenAI Generation → Answer + Citations
```

### Components

```
docduck/
├── Indexer/           # Document indexing service
│   ├── Services/      # Graph, OpenAI, Database services
│   └── Options/       # Configuration options
├── Api/               # Query/Chat RAG API
│   ├── Services/      # OpenAI client, Vector search
│   └── Models/        # Request/response models
├── sql/               # Database scripts
├── k8s/               # Kubernetes manifests
└── db-util.sh         # Database management CLI
```

## Query API Usage

The API provides two main endpoints for querying indexed documents:

### Simple Query
```bash
curl -X POST http://localhost:5000/query \
  -H "Content-Type: application/json" \
  -d '{
    "question": "What are the main features?",
    "topK": 8
  }'
```

### Chat with History
```bash
curl -X POST http://localhost:5000/chat \
  -H "Content-Type: application/json" \
  -d '{
    "message": "Can you elaborate on that?",
    "history": [
      {"role": "user", "content": "Previous question"},
      {"role": "assistant", "content": "Previous answer"}
    ]
  }'
```

See [Api/README.md](Api/README.md) and [docs/reports/api-implementation.md](docs/reports/api-implementation.md) for complete API documentation.

## Docker/Kubernetes Deployment

### Indexer Dockerfile

The Indexer now has its own optimized multi-stage Dockerfile at `Indexer/Dockerfile` that builds only the required projects (Indexer + shared library) and publishes a self-contained deployment layer for faster, cache-friendly multi-arch builds.

```dockerfile
# syntax=docker/dockerfile:1.6
ARG DOTNET_VERSION=8.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS restore
WORKDIR /src
COPY docduck.sln ./
COPY Indexer/Indexer.csproj Indexer/
COPY Providers.Shared/Providers.Shared.csproj Providers.Shared/
RUN dotnet restore Indexer/Indexer.csproj

FROM restore AS build
COPY . .
RUN dotnet publish Indexer/Indexer.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:${DOTNET_VERSION} AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Indexer.dll"]
```

Build (locally):
```
docker build -f Indexer/Dockerfile -t docduck-indexer:dev .
```

### Example Kubernetes CronJob

```yaml
apiVersion: batch/v1
kind: CronJob
metadata:
  name: docduck-indexer
spec:
  schedule: "0 */6 * * *"  # Every 6 hours
  jobTemplate:
    spec:
      template:
        spec:
          containers:
          - name: indexer
            image: your-registry/docduck-indexer:latest
            env:
            - name: GRAPH_TENANT_ID
              valueFrom:
                secretKeyRef:
                  name: indexer-secrets
                  key: graph-tenant-id
            - name: GRAPH_CLIENT_ID
              valueFrom:
                secretKeyRef:
                  name: indexer-secrets
                  key: graph-client-id
            - name: GRAPH_CLIENT_SECRET
              valueFrom:
                secretKeyRef:
                  name: indexer-secrets
                  key: graph-client-secret
            - name: GRAPH_DRIVE_ID
              value: "your-drive-id"
            - name: OPENAI_API_KEY
              valueFrom:
                secretKeyRef:
                  name: indexer-secrets
                  key: openai-api-key
            - name: DB_CONNECTION_STRING
              valueFrom:
                secretKeyRef:
                  name: indexer-secrets
                  key: db-connection-string
          restartPolicy: OnFailure
```

## Architecture

### Core Services

**Document Providers** (implements `IDocumentProvider`):
- **OneDriveProvider**: Microsoft Graph API integration
- **S3Provider**: AWS S3 bucket access
- **LocalProvider**: Filesystem scanning

**Text Extraction** (implements `ITextExtractor`):
- **DocxTextExtractor**: OpenXML-based DOCX extraction
- **PlainTextExtractor**: Plain text, Markdown, CSV, JSON, etc.
- **PdfTextExtractor**: PDF extraction (requires additional library)
- **TextExtractionService**: Orchestrates format-specific extractors

**Processing Pipeline**:
- **TextChunker**: Character-based overlapping text chunking
- **OpenAiEmbeddingsClient**: OpenAI embeddings API client with batching
- **VectorRepository**: PostgreSQL + pgvector data access with upsert logic
- **MultiProviderIndexerService**: Main orchestration service coordinating the pipeline

### Data Flow

1. **List** .docx files from OneDrive via Microsoft Graph
2. **Check** ETag in `docs_files` table to skip unchanged files
3. **Download** file stream for new/changed files
4. **Extract** plain text from .docx using OpenXML SDK
5. **Chunk** text into overlapping segments
6. **Embed** chunk texts via OpenAI API (batched)
7. **Upsert** chunks with embeddings into `docs_chunks` table
8. **Track** file ETag and last modified timestamp in `docs_files`

### Exit Codes

- `0` - Success (files processed)
- `1` - Error or no files processed
- `130` - Cancelled (SIGTERM/SIGINT)

## Performance Tuning

- **Batch Size**: Increase `BATCH_SIZE` (e.g., 32) for faster embedding generation, but watch for rate limits
- **Connection Pool**: Adjust `MinPoolSize` and `MaxPoolSize` in connection string
- **Chunk Size**: Larger chunks (e.g., 1500) reduce total embeddings but may lose granularity
- **MAX_FILES**: Use during development to limit processing

## Troubleshooting

### "No .docx files found"
- Verify `GRAPH_FOLDER_PATH` is correct
- Check Graph API permissions (Files.Read.All or Sites.Read.All)
- Ensure Drive ID or Site ID is valid

### "Failed to generate embeddings"
- Verify `OPENAI_API_KEY` is valid
- Check rate limits on your OpenAI account
- Confirm `OPENAI_BASE_URL` is correct (especially for Azure OpenAI)

### "Failed to insert chunks"
- Ensure pgvector extension is installed: `CREATE EXTENSION vector;`
- Verify table schema matches expected structure
- Check connection string and database permissions

### Idempotency not working
- Ensure `docs_files` table exists
- Check that OneDrive files have stable ETags
- Verify unique constraint on `(doc_id, chunk_num)` in `docs_chunks`

## Development

### Project Structure

```
Indexer/
├── Models.cs                    # Data models (Chunk, ChunkRecord, DocItem)
├── Program.cs                   # Host configuration and DI setup
├── IndexerService.cs            # Main orchestration logic
├── Options/
│   ├── ChunkingOptions.cs
│   ├── DbOptions.cs
│   ├── GraphOptions.cs
│   └── OpenAiOptions.cs
└── Services/
    ├── DocxExtractor.cs
    ├── GraphClient.cs
    ├── OpenAiEmbeddingsClient.cs
    ├── TextChunker.cs
    └── VectorRepository.cs

Indexer.Tests/
└── UnitTest1.cs                 # Unit tests for chunking and extraction
```

### Running Tests

```bash
dotnet test
```

### Code Style

- C# 12 with file-scoped namespaces
- Nullable reference types enabled
- Structured logging with Microsoft.Extensions.Logging
- Async/await end-to-end with CancellationToken support
- Pragmatic SOLID: small, focused services with clear responsibilities

## Documentation

- Full docs live under `docs/`:
  - Guides: [docs/guides/quickstart.md](docs/guides/quickstart.md), [docs/guides/authentication.md](docs/guides/authentication.md), [docs/guides/developer-guide.md](docs/guides/developer-guide.md)
  - Database: [docs/database/pgvector.md](docs/database/pgvector.md), [docs/database/pgvector-quickref.md](docs/database/pgvector-quickref.md)
  - Architecture: [docs/architecture.md](docs/architecture.md)
  - Changelog: [docs/changelog.md](docs/changelog.md)

## License

MIT
