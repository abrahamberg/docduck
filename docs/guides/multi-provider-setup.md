# Multi-Provider Document Indexing

DocDuck supports multiple document providers with a modular, plugin-based architecture. Index documents from OneDrive, local filesystem, S3, and easily add custom providers.

## Overview

The indexer pulls documents from multiple sources simultaneously:
- **OneDrive** (Microsoft Graph API)
- **Local Filesystem** 
- **AWS S3**
- Extensible for Dropbox, RSS feeds, web scraping, etc.

Each provider can be independently enabled/disabled and configured via YAML or environment variables.

## Configuration

### YAML Configuration (Recommended)

Create `appsettings.yaml` in the Indexer directory:

```yaml
# OpenAI Embedding Configuration
OpenAi:
  ApiKey: "${OPENAI_API_KEY}"
  BaseUrl: "https://api.openai.com/v1"
  EmbedModel: "text-embedding-3-small"
  BatchSize: 100

# Database Configuration
Database:
  ConnectionString: "${DB_CONNECTION_STRING}"

# Chunking Configuration
Chunking:
  ChunkSize: 500
  ChunkOverlap: 50
  MaxFiles: null  # null = all files, or set a limit

# Document Providers
Providers:
  # OneDrive Provider
  OneDrive:
    Enabled: true
    Name: "CompanyOneDrive"
    AuthMode: "ClientSecret"
    AccountType: "business"
    TenantId: "${GRAPH_TENANT_ID}"
    ClientId: "${GRAPH_CLIENT_ID}"
    ClientSecret: "${GRAPH_CLIENT_SECRET}"
    SiteId: "${GRAPH_SITE_ID}"
    DriveId: "${GRAPH_DRIVE_ID}"
    FolderPath: "/Shared Documents/Docs"
    FileExtensions: [".docx"]

  # Local Files Provider
  Local:
    Enabled: true
    Name: "LocalDocs"
    RootPath: "/data/documents"
    Recursive: true
    FileExtensions: [".docx", ".pdf", ".txt"]
    ExcludePatterns: [".git", "node_modules"]

  # AWS S3 Provider
  S3:
    Enabled: true
    Name: "DocumentsBucket"
    BucketName: "my-docs-bucket"
    Prefix: "documents/"
    Region: "us-east-1"
    UseInstanceProfile: false
    AccessKeyId: "${AWS_ACCESS_KEY_ID}"
    SecretAccessKey: "${AWS_SECRET_ACCESS_KEY}"
    FileExtensions: [".docx", ".pdf", ".txt"]
```

### Environment Variables (Alternative)

All settings support environment variable expansion using `${VAR_NAME}` syntax in YAML, or can be set directly as environment variables.

## Docker Compose Setup

### All-in-One (Quick Test)

```yaml
version: '3.8'
services:
  postgres:
    image: pgvector/pgvector:pg16
    environment:
      POSTGRES_DB: docduck
      POSTGRES_USER: docduck
      POSTGRES_PASSWORD: docduck123
    volumes:
      - ./schema.sql:/docker-entrypoint-initdb.d/schema.sql
      - pgdata:/var/lib/postgresql/data
    ports:
      - "5432:5432"

  indexer:
    build: ./Indexer
    environment:
      OPENAI_API_KEY: ${OPENAI_API_KEY}
      DB_CONNECTION_STRING: "Host=postgres;Database=docduck;Username=docduck;Password=docduck123"
    volumes:
      - ./Indexer/appsettings.yaml:/app/appsettings.yaml
      - /path/to/local/docs:/data/documents  # For local provider
    depends_on:
      - postgres

  api:
    build: ./Api
    environment:
      OPENAI_API_KEY: ${OPENAI_API_KEY}
      DB_CONNECTION_STRING: "Host=postgres;Database=docduck;Username=docduck;Password=docduck123"
    ports:
      - "8080:8080"
    depends_on:
      - postgres

volumes:
  pgdata:
```

### Kubernetes Deployment

See `k8s/` directory for full examples. Key points:

1. **ConfigMap** for `appsettings.yaml`
2. **Secrets** for sensitive credentials
3. **Volume mounts** for local files provider
4. **CronJob** for scheduled indexing

Example ConfigMap:

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: indexer-config
data:
  appsettings.yaml: |
    Providers:
      OneDrive:
        Enabled: true
        Name: "CompanyDrive"
        # ... config
      Local:
        Enabled: true
        RootPath: "/mnt/docs"
      S3:
        Enabled: true
        UseInstanceProfile: true  # Use IAM role
        BucketName: "company-documents"
```

## API Enhancements

### New Endpoint: Get Providers

```bash
GET /providers
```

Response:
```json
{
  "providers": [
    {
      "providerType": "onedrive",
      "providerName": "CompanyOneDrive",
      "isEnabled": true,
      "registeredAt": "2025-01-01T10:00:00Z",
      "lastSyncAt": "2025-01-01T12:30:00Z",
      "metadata": {
        "AccountType": "business",
        "FolderPath": "/Shared Documents/Docs"
      }
    },
    {
      "providerType": "local",
      "providerName": "LocalDocs",
      "isEnabled": true,
      "registeredAt": "2025-01-01T10:00:00Z",
      "lastSyncAt": "2025-01-01T12:30:00Z",
      "metadata": {
        "RootPath": "/data/documents",
        "Recursive": "True"
      }
    }
  ],
  "count": 2,
  "timestamp": "2025-01-01T12:35:00Z"
}
```

### Enhanced Query Endpoint

Now supports provider filtering:

```bash
POST /query
{
  "question": "What is the deployment process?",
  "topK": 5,
  "providerType": "s3",      # Optional: filter by provider type
  "providerName": "DocsBucket"  # Optional: filter by provider name
}
```

### Enhanced Chat Endpoint

```bash
POST /chat
{
  "message": "How do I configure the API?",
  "history": [],
  "topK": 5,
  "providerType": "local"  # Search only local files
}
```

## Database Schema Updates

New tables:

```sql
-- Track registered providers
CREATE TABLE providers (
    provider_type TEXT NOT NULL,
    provider_name TEXT NOT NULL,
    is_enabled BOOLEAN NOT NULL DEFAULT true,
    registered_at TIMESTAMPTZ DEFAULT now(),
    last_sync_at TIMESTAMPTZ,
    metadata JSONB,
    PRIMARY KEY (provider_type, provider_name)
);

-- Updated chunks table with provider tracking
CREATE TABLE docs_chunks (
    id BIGSERIAL PRIMARY KEY,
    doc_id TEXT NOT NULL,
    filename TEXT NOT NULL,
    provider_type TEXT NOT NULL,  -- NEW
    provider_name TEXT NOT NULL,  -- NEW
    chunk_num INT NOT NULL,
    text TEXT NOT NULL,
    metadata JSONB,
    embedding vector(1536),
    created_at TIMESTAMPTZ DEFAULT now(),
    CONSTRAINT unique_doc_chunk UNIQUE (doc_id, chunk_num)
);

-- Updated files tracking with provider info
CREATE TABLE docs_files (
    doc_id TEXT NOT NULL,
    provider_type TEXT NOT NULL,   -- NEW
    provider_name TEXT NOT NULL,   -- NEW
    filename TEXT NOT NULL,
    etag TEXT NOT NULL,
    last_modified TIMESTAMPTZ NOT NULL,
    relative_path TEXT,            -- NEW
    PRIMARY KEY (doc_id, provider_type, provider_name)
);
```

## Adding Custom Providers

### 1. Implement the Interface

```csharp
public class DropboxProvider : IDocumentProvider
{
    public string ProviderType => "dropbox";
    public string ProviderName { get; }
    public bool IsEnabled { get; }

    public async Task<IReadOnlyList<ProviderDocument>> ListDocumentsAsync(CancellationToken ct)
    {
        // Implement Dropbox API calls
    }

    public async Task<Stream> DownloadDocumentAsync(string documentId, CancellationToken ct)
    {
        // Download from Dropbox
    }

    public Task<ProviderMetadata> GetMetadataAsync(CancellationToken ct)
    {
        // Return provider metadata
    }
}
```

### 2. Add Configuration

```yaml
Providers:
  Dropbox:
    Enabled: true
    Name: "CompanyDropbox"
    AccessToken: "${DROPBOX_ACCESS_TOKEN}"
    FolderPath: "/Documents"
```

### 3. Register in Program.cs

```csharp
if (providersConfig.Dropbox?.Enabled == true)
{
    builder.Services.AddSingleton<IDocumentProvider>(sp =>
        new DropboxProvider(
            providersConfig.Dropbox,
            sp.GetRequiredService<ILogger<DropboxProvider>>()));
}
```

## Best Practices

1. **Start with one provider** enabled for testing
2. **Use YAML** for cleaner configuration management
3. **Set MaxFiles** during testing to avoid long initial runs
4. **Monitor logs** for provider-specific errors
5. **Use provider filtering** in queries when you know the source
6. **Schedule indexing** via CronJob with appropriate frequency

## Troubleshooting

### Provider not appearing in /providers endpoint
- Check `Enabled: true` in config
- Verify no startup errors in indexer logs
- Ensure database schema is updated

### Files not being indexed
- Check provider credentials
- Verify file paths/bucket names
- Check FileExtensions filter
- Review ExcludePatterns for local provider

### Duplicate documents
- Each provider tracks files independently by (doc_id, provider_type, provider_name)
- Same file from different providers = different entries (intentional)
