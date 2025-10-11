# Indexer - OneDrive to Postgres Vector Search

A .NET 8 console application that indexes Microsoft OneDrive `.docx` documents into PostgreSQL with pgvector embeddings via OpenAI.

## Features

- **Microsoft Graph Integration**: Authenticates via client credentials to enumerate and download OneDrive files
- **Text Extraction**: Extracts plain text from `.docx` files using OpenXML SDK
- **Smart Chunking**: Splits text into overlapping chunks with configurable size and overlap
- **OpenAI Embeddings**: Generates vector embeddings using OpenAI API (default: text-embedding-3-small, 1536 dimensions)
- **PostgreSQL Storage**: Stores chunks and embeddings in Postgres with pgvector extension
- **Idempotency**: Tracks file ETags to skip unchanged documents
- **K8s Ready**: Handles SIGTERM gracefully, runs as a CronJob/Job, exits with proper codes

## Prerequisites

- .NET 8 SDK
- PostgreSQL with pgvector extension installed
- Microsoft Azure AD app registration with Graph API permissions
- OpenAI API key

## Database Setup

Create the required tables in your PostgreSQL database:

```sql
-- Enable pgvector extension
CREATE EXTENSION IF NOT EXISTS vector;

-- Table for document chunks with embeddings
CREATE TABLE docs_chunks (
    id BIGSERIAL PRIMARY KEY,
    doc_id TEXT NOT NULL,
    filename TEXT NOT NULL,
    chunk_num INT NOT NULL,
    text TEXT NOT NULL,
    metadata JSONB,
    embedding vector(1536),
    created_at TIMESTAMPTZ DEFAULT now(),
    UNIQUE (doc_id, chunk_num)
);

-- Table for tracking file state (ETag, last modified)
CREATE TABLE docs_files (
    doc_id TEXT PRIMARY KEY,
    filename TEXT NOT NULL,
    etag TEXT NOT NULL,
    last_modified TIMESTAMPTZ NOT NULL
);

-- Optional: Create index for vector similarity search
CREATE INDEX ON docs_chunks USING ivfflat (embedding vector_cosine_ops) WITH (lists = 100);
```

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

### 1. Set environment variables

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

### 2. Build and run

```bash
cd Indexer
dotnet build
dotnet run
```

### 3. Run tests

```bash
cd Indexer.Tests
dotnet test
```

## Docker/Kubernetes Deployment

### Example Dockerfile

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Indexer/Indexer.csproj Indexer/
RUN dotnet restore Indexer/Indexer.csproj
COPY Indexer/ Indexer/
RUN dotnet publish Indexer/Indexer.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Indexer.dll"]
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

### Services

- **GraphClient**: Microsoft Graph API wrapper for file enumeration and download
- **DocxExtractor**: OpenXML-based text extraction from .docx files
- **TextChunker**: Character-based overlapping text chunking
- **OpenAiEmbeddingsClient**: OpenAI embeddings API client with batching
- **VectorRepository**: PostgreSQL + pgvector data access with upsert logic
- **IndexerService**: Main orchestration service coordinating the pipeline

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

## License

MIT
