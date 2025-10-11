# Quick Start: Multi-Provider Indexer

Get DocDuck running with multiple document providers in 5 minutes.

## Prerequisites

- Docker and Docker Compose installed
- OpenAI API key
- Documents to index (local files, OneDrive access, or S3 bucket)

## Option 1: Local Files Only (Fastest)

### 1. Setup

```bash
# Clone or navigate to project
cd docduck

# Copy environment file
cp env.multi-provider.example .env

# Edit .env - set your OpenAI key
nano .env
# Set: OPENAI_API_KEY=sk-...
```

### 2. Prepare Documents

```bash
# Create sample documents folder
mkdir -p docs-sample
# Copy some .docx, .pdf, or .txt files into docs-sample/
```

### 3. Configure

Edit `Indexer/appsettings.yaml`:

```yaml
Providers:
  OneDrive:
    Enabled: false  # Disable for now
    
  Local:
    Enabled: true   # Enable local files
    Name: "LocalFiles"
    RootPath: "/data/documents"
    FileExtensions: [".docx", ".pdf", ".txt"]
    
  S3:
    Enabled: false  # Disable for now
```

### 4. Run

```bash
# Start everything
docker-compose -f docker-compose-multi-provider.yml up -d

# Wait for DB to initialize (30 seconds)
sleep 30

# Run indexer (one-time)
docker-compose -f docker-compose-multi-provider.yml up indexer

# Check API
curl http://localhost:8080/providers
curl -X POST http://localhost:8080/query \
  -H "Content-Type: application/json" \
  -d '{"question": "What documents do you have?"}'
```

## Option 2: OneDrive + Local Files

### 1. Azure App Registration

If using OneDrive, register an app in Azure AD:

1. Go to [Azure Portal](https://portal.azure.com) → Azure Active Directory → App registrations
2. New registration → Name: "DocDuck Indexer"
3. Supported account types: Single tenant
4. Redirect URI: Not needed (app-only auth)
5. After creation:
   - Copy **Application (client) ID**
   - Copy **Directory (tenant) ID**
   - Certificates & secrets → New client secret → Copy value
6. API permissions:
   - Microsoft Graph → Application permissions
   - Add: `Files.Read.All` and `Sites.Read.All`
   - Grant admin consent

### 2. Get SharePoint Site and Drive IDs

```bash
# Install Microsoft Graph CLI
# Or use Graph Explorer: https://developer.microsoft.com/graph/graph-explorer

# Get Site ID
curl -H "Authorization: Bearer YOUR_TOKEN" \
  "https://graph.microsoft.com/v1.0/sites/root:/sites/YOUR_SITE_NAME"

# Get Drive ID
curl -H "Authorization: Bearer YOUR_TOKEN" \
  "https://graph.microsoft.com/v1.0/sites/SITE_ID/drives"
```

### 3. Configure

Edit `.env`:

```bash
OPENAI_API_KEY=sk-...
DB_CONNECTION_STRING=Host=postgres;Database=docduck;Username=docduck;Password=docduck123

# OneDrive
GRAPH_TENANT_ID=your-tenant-id
GRAPH_CLIENT_ID=your-client-id
GRAPH_CLIENT_SECRET=your-client-secret
GRAPH_SITE_ID=your-site-id
GRAPH_DRIVE_ID=your-drive-id
GRAPH_FOLDER_PATH=/Shared Documents/Docs
```

Edit `Indexer/appsettings.yaml`:

```yaml
Providers:
  OneDrive:
    Enabled: true  # Enable OneDrive
    
  Local:
    Enabled: true  # Keep local enabled too
```

### 4. Run

```bash
docker-compose -f docker-compose-multi-provider.yml up -d
sleep 30
docker-compose -f docker-compose-multi-provider.yml up indexer

# Query both sources
curl -X POST http://localhost:8080/query \
  -H "Content-Type: application/json" \
  -d '{"question": "What documents do you have?"}'

# Query only OneDrive
curl -X POST http://localhost:8080/query \
  -H "Content-Type: application/json" \
  -d '{
    "question": "What documents do you have?",
    "providerType": "onedrive"
  }'
```

## Option 3: AWS S3

### 1. S3 Setup

```bash
# Create bucket (if needed)
aws s3 mb s3://my-docs-bucket

# Upload documents
aws s3 cp ./docs/ s3://my-docs-bucket/documents/ --recursive

# Create IAM user with S3 read access (or use instance profile in K8s)
```

### 2. Configure

Edit `.env`:

```bash
S3_BUCKET_NAME=my-docs-bucket
AWS_ACCESS_KEY_ID=AKIAxxxx
AWS_SECRET_ACCESS_KEY=secret
```

Edit `Indexer/appsettings.yaml`:

```yaml
Providers:
  S3:
    Enabled: true
    Name: "S3Documents"
    BucketName: "${S3_BUCKET_NAME}"
    Prefix: "documents/"
    Region: "us-east-1"
    UseInstanceProfile: false
```

### 3. Run

```bash
docker-compose -f docker-compose-multi-provider.yml up -d
sleep 30
docker-compose -f docker-compose-multi-provider.yml up indexer
```

## Testing

### Check Providers

```bash
curl http://localhost:8080/providers | jq
```

Expected output:
```json
{
  "providers": [
    {
      "providerType": "local",
      "providerName": "LocalFiles",
      "isEnabled": true,
      "registeredAt": "2025-01-01T10:00:00Z",
      "lastSyncAt": "2025-01-01T10:05:00Z"
    }
  ],
  "count": 1
}
```

### Query All Providers

```bash
curl -X POST http://localhost:8080/query \
  -H "Content-Type: application/json" \
  -d '{
    "question": "Summarize available documentation",
    "topK": 5
  }' | jq
```

### Query Specific Provider

```bash
curl -X POST http://localhost:8080/query \
  -H "Content-Type: application/json" \
  -d '{
    "question": "What files are in S3?",
    "providerType": "s3",
    "topK": 5
  }' | jq
```

### Chat with History

```bash
curl -X POST http://localhost:8080/chat \
  -H "Content-Type: application/json" \
  -d '{
    "message": "What documentation do you have?",
    "history": []
  }' | jq
```

## Scheduled Indexing

### Docker Compose (Cron)

Add to host crontab:

```cron
# Re-index every 6 hours
0 */6 * * * cd /path/to/docduck && docker-compose -f docker-compose-multi-provider.yml up indexer
```

### Kubernetes (CronJob)

```bash
kubectl apply -f k8s/indexer-multi-provider.yaml
```

The CronJob runs every 6 hours automatically.

## Troubleshooting

### No documents indexed

```bash
# Check indexer logs
docker-compose -f docker-compose-multi-provider.yml logs indexer

# Common issues:
# - Wrong file paths
# - Invalid credentials
# - FileExtensions filter doesn't match files
```

### Database connection failed

```bash
# Check if PostgreSQL is running
docker-compose -f docker-compose-multi-provider.yml ps postgres

# Check connection string in .env
# Verify: Host=postgres (not localhost when in Docker)
```

### API returns empty results

```bash
# Check if documents were indexed
docker exec -it docduck-postgres psql -U docduck -d docduck -c "SELECT COUNT(*) FROM docs_chunks;"

# If 0, check indexer logs
docker-compose -f docker-compose-multi-provider.yml logs indexer
```

### Provider not showing in /providers

```bash
# Check if Enabled: true in appsettings.yaml
# Run indexer at least once to register providers
# Check database:
docker exec -it docduck-postgres psql -U docduck -d docduck -c "SELECT * FROM providers;"
```

## Next Steps

- **Add more providers**: Enable S3, OneDrive, etc.
- **Customize chunking**: Adjust `ChunkSize` and `ChunkOverlap` in config
- **Build frontend**: Use `/providers` and `/query` endpoints
- **Schedule indexing**: Set up CronJob or scheduled task
- **Monitor**: Add logging and metrics

## Resources

- [Multi-Provider Setup Guide](./docs/guides/multi-provider-setup.md)
- [Migration Guide](./docs/guides/migration-multi-provider.md)
- [Implementation Details](./docs/reports/multi-provider-implementation.md)
