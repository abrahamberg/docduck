# DocDuck - Multi-Provider Document Q&A System

Semantic search and question-answering over documents from multiple sources using pgvector, OpenAI embeddings, and RAG.

## Features

- **Multi-Provider Architecture**: Index documents from OneDrive, Local filesystem, S3, and more
- **Semantic Search**: pgvector-powered similarity search
- **RAG-based Q&A**: Context-aware answers using OpenAI
- **Provider Filtering**: Query specific document sources
- **Modular Design**: Easy to add custom providers
- **Production Ready**: Docker, Kubernetes, scheduled indexing

## Quick Start

### 1. Prerequisites

- Docker & Docker Compose
- OpenAI API key
- Documents to index (local folder, OneDrive access, or S3 bucket)

### 2. Setup

```bash
git clone <your-repo>
cd docduck

# Copy and configure environment
cp env.multi-provider.example .env
# Edit .env and set OPENAI_API_KEY

# Place documents
mkdir docs-sample
# Add .docx, .pdf, or .txt files to docs-sample/
```

### 3. Configure Providers

Edit `Indexer/appsettings.yaml`:

```yaml
Providers:
  Local:
    Enabled: true
    RootPath: "/data/documents"
    FileExtensions: [".docx", ".pdf", ".txt"]
```

### 4. Run

```bash
# Start all services
docker-compose -f docker-compose-multi-provider.yml up -d

# Wait for DB initialization
sleep 30

# Run indexer
docker-compose -f docker-compose-multi-provider.yml up indexer

# Test API
curl http://localhost:8080/providers
curl -X POST http://localhost:8080/query \
  -H "Content-Type: application/json" \
  -d '{"question": "What documents are available?"}'
```

## Architecture

```
┌─────────────────────────────────────────────────┐
│                   API (Port 8080)                │
│  /query  /chat  /providers  /health             │
└────────────┬────────────────────────────────────┘
             │
             ▼
┌────────────────────────────────────────────────┐
│           PostgreSQL + pgvector                │
│  • providers table                             │
│  • docs_chunks (with embeddings)               │
│  • docs_files (tracking)                       │
└────────────▲───────────────────────────────────┘
             │
             │ Indexer (CronJob)
             │
┌────────────┴───────────────────────────────────┐
│          Multi-Provider Indexer                │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐       │
│  │ OneDrive │ │  Local   │ │    S3    │       │
│  │ Provider │ │ Provider │ │ Provider │       │
│  └──────────┘ └──────────┘ └──────────┘       │
└────────────────────────────────────────────────┘
```

## Supported Providers

| Provider | Status | Configuration |
|----------|--------|---------------|
| **Local Filesystem** | ✅ Ready | `RootPath`, `FileExtensions`, `ExcludePatterns` |
| **OneDrive** | ✅ Ready | Azure AD app, Site/Drive IDs |
| **AWS S3** | ✅ Ready | Bucket name, region, credentials/IAM role |
| **Dropbox** | 🔧 Extendable | Implement `IDocumentProvider` |
| **Google Drive** | 🔧 Extendable | Implement `IDocumentProvider` |
| **RSS/Web** | 🔧 Extendable | Implement `IDocumentProvider` |

## API Endpoints

### `GET /providers`
List all active document providers.

```bash
curl http://localhost:8080/providers
```

### `POST /query`
Ask questions with optional provider filtering.

```bash
curl -X POST http://localhost:8080/query \
  -H "Content-Type: application/json" \
  -d '{
    "question": "How do I deploy the API?",
    "topK": 5,
    "providerType": "local",
    "providerName": "LocalDocs"
  }'
```

### `POST /chat`
Conversational chat with history.

```bash
curl -X POST http://localhost:8080/chat \
  -H "Content-Type: application/json" \
  -d '{
    "message": "What services are available?",
    "history": []
  }'
```

## Configuration

### YAML (Recommended)

`Indexer/appsettings.yaml`:

```yaml
OpenAi:
  ApiKey: "${OPENAI_API_KEY}"
  EmbedModel: "text-embedding-3-small"

Database:
  ConnectionString: "${DB_CONNECTION_STRING}"

Providers:
  OneDrive:
    Enabled: true
    Name: "CompanyDrive"
    TenantId: "${GRAPH_TENANT_ID}"
    # ... more config
    
  Local:
    Enabled: true
    Name: "LocalFiles"
    RootPath: "/data/documents"
    FileExtensions: [".docx", ".pdf", ".txt"]
    
  S3:
    Enabled: true
    Name: "S3Bucket"
    BucketName: "${S3_BUCKET_NAME}"
    Region: "us-east-1"
    UseInstanceProfile: true
```

### Environment Variables

All settings support `${VAR_NAME}` expansion in YAML or direct env vars.

## Deployment

### Docker Compose

```bash
docker-compose -f docker-compose-multi-provider.yml up -d
```

### Kubernetes

```bash
kubectl apply -f k8s/indexer-multi-provider.yaml
```

CronJob runs every 6 hours automatically.

## Adding Custom Providers

1. **Implement interface**: `IDocumentProvider`
2. **Add config model**: Extend `ProvidersConfiguration`
3. **Register**: Add to `Program.cs` DI container

Example:

```csharp
public class DropboxProvider : IDocumentProvider
{
    public string ProviderType => "dropbox";
    public string ProviderName { get; }
    public bool IsEnabled { get; }
    
    public Task<IReadOnlyList<ProviderDocument>> ListDocumentsAsync(CT ct) { }
    public Task<Stream> DownloadDocumentAsync(string id, CT ct) { }
    public Task<ProviderMetadata> GetMetadataAsync(CT ct) { }
}
```

## Project Structure

```
docduck/
├── Api/                      # Query API service
│   ├── Models/              # Request/Response models
│   ├── Services/            # VectorSearchService
│   └── Program.cs           # API endpoints
├── Indexer/                 # Multi-provider indexer
│   ├── Providers/           # Provider implementations
│   │   ├── IDocumentProvider.cs
│   │   ├── OneDriveProvider.cs
│   │   ├── LocalProvider.cs
│   │   └── S3Provider.cs
│   ├── Services/            # Core services
│   ├── Options/             # Configuration models
│   ├── MultiProviderIndexerService.cs
│   ├── Program.cs           # DI setup
│   └── appsettings.yaml     # Configuration
├── docs/                    # Documentation
│   ├── guides/             
│   │   └── multi-provider-setup.md
│   └── reports/
│       └── multi-provider-implementation.md
├── k8s/                     # Kubernetes configs
├── schema.sql               # Database schema
└── docker-compose-multi-provider.yml
```

## Documentation

- [Multi-Provider Setup Guide](./docs/guides/multi-provider-setup.md) - Complete configuration guide
- [Quick Start](./QUICKSTART-MULTI-PROVIDER.md) - 5-minute setup
- [Implementation Details](./docs/reports/multi-provider-implementation.md) - Architecture deep dive

## Tech Stack

- **Backend**: ASP.NET Core 8, Minimal APIs
- **Database**: PostgreSQL 16 with pgvector
- **Embeddings**: OpenAI `text-embedding-3-small`
- **LLM**: OpenAI GPT-4o-mini
- **Document Processing**: DocumentFormat.OpenXml
- **Cloud**: AWS S3 SDK, Microsoft Graph API
- **Container**: Docker, Kubernetes

## License

[Your License]

## Contributing

PRs welcome! To add a new provider:
1. Implement `IDocumentProvider`
2. Add configuration model
3. Register in DI
4. Submit PR with tests
