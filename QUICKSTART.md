# Quick Start Guide

## Choose Your Authentication Method

### 🏢 Business OneDrive (Recommended)
Use **Client Secret** authentication for unattended jobs and production deployments.

### 👤 Personal OneDrive
Use **Username/Password** authentication for personal Microsoft accounts (requires account without MFA).

---

## Option 1: Business OneDrive (Client Secret Auth)

### 1. Database Setup

```bash
# Connect to your PostgreSQL database
psql -h localhost -U postgres -d vectors

# Run the schema
\i schema.sql

# Verify pgvector is installed
SELECT * FROM pg_available_extensions WHERE name = 'vector';
```

## 2. Azure AD App Registration

1. Go to Azure Portal → Azure Active Directory → App registrations
2. Create a new registration
3. Note the **Application (client) ID** and **Directory (tenant) ID**
4. Create a client secret under "Certificates & secrets"
5. Add API permissions:
   - Microsoft Graph → Application permissions → Files.Read.All (or Sites.Read.All)
6. Grant admin consent

## 3. Get OneDrive Drive ID

### Option A: Using Graph Explorer
1. Go to https://developer.microsoft.com/en-us/graph/graph-explorer
2. Sign in and run: `GET https://graph.microsoft.com/v1.0/me/drive`
3. Copy the `id` field

### Option B: Using PowerShell
```powershell
Connect-MgGraph -Scopes "Files.Read.All"
Get-MgUserDrive -UserId "your-email@domain.com" | Select-Object Id
```

## 4. Environment Configuration

### For Business OneDrive (Client Secret)

Copy `.env.example` to `.env` and configure:

```bash
cp .env.example .env
nano .env  # or your preferred editor
```

Set these values:
```bash
GRAPH_AUTH_MODE=ClientSecret
GRAPH_ACCOUNT_TYPE=business
GRAPH_TENANT_ID="your-tenant-id-from-step-2"
GRAPH_CLIENT_ID="your-client-id-from-step-2"
GRAPH_CLIENT_SECRET="your-client-secret-from-step-2"
GRAPH_DRIVE_ID="your-drive-id-from-step-3"
GRAPH_FOLDER_PATH="/Shared Documents/Docs"

OPENAI_API_KEY="sk-..."
DB_CONNECTION_STRING="Host=localhost;Database=vectors;Username=postgres;Password=password;MinPoolSize=1;MaxPoolSize=3"

# Optional: limit to 3 files for testing
MAX_FILES=3
```

### For Personal OneDrive (Username/Password)

```bash
GRAPH_AUTH_MODE=UserPassword
GRAPH_ACCOUNT_TYPE=personal
GRAPH_CLIENT_ID="your-client-id"
GRAPH_USERNAME="yourname@outlook.com"
GRAPH_PASSWORD="your-password"
GRAPH_FOLDER_PATH="/Documents"

OPENAI_API_KEY="sk-..."
DB_CONNECTION_STRING="Host=localhost;Database=vectors;Username=postgres;Password=password;MinPoolSize=1;MaxPoolSize=3"

# Optional
MAX_FILES=3
```

**⚠️ Personal OneDrive Notes:**
- Your Microsoft account **MUST NOT** have MFA enabled
- Username/password auth is deprecated and for development/personal use only
- You still need to create an Azure AD app registration (for CLIENT_ID)
- Use tenant ID `consumers` (handled automatically when `GRAPH_ACCOUNT_TYPE=personal`)
- Add delegated permissions: `Files.Read.All` or `Files.ReadWrite.All`

## 5. Run Locally

```bash
# Source environment variables (bash/zsh)
export $(cat .env | xargs)

# Build and run
cd Indexer
dotnet run
```

## 6. Expected Output

```
info: Program[0]
      Configuration loaded:
info: Program[0]
        Graph Tenant ID: Present
info: Program[0]
        OpenAI API Key: Present
info: Program[0]
        DB Connection: Present
info: Indexer.IndexerService[0]
      Starting indexer pipeline
info: Indexer.Services.GraphClient[0]
      Listing items from Drive ID: b!abc123..., Path: /Shared Documents/Docs
info: Indexer.Services.GraphClient[0]
      Found 5 .docx files
info: Indexer.IndexerService[0]
      Processing 3 files (total available: 5)
info: Indexer.IndexerService[0]
      Processing file: Document1.docx
info: Indexer.Services.VectorRepository[0]
      Upserted 12 chunks to database
info: Indexer.IndexerService[0]
      Processed Document1.docx: 12 chunks in 3.45s
...
info: Indexer.IndexerService[0]
      Indexing complete. Processed: 3, Skipped: 0, Total chunks: 35, Elapsed: 12.34s
info: Program[0]
      Indexer exiting with code 0
```

## 7. Verify Data

```sql
-- Check indexed documents
SELECT doc_id, filename, COUNT(*) as chunks
FROM docs_chunks
GROUP BY doc_id, filename
ORDER BY filename;

-- View sample chunks
SELECT filename, chunk_num, LEFT(text, 50) as text_preview
FROM docs_chunks
LIMIT 10;

-- Test similarity search (example)
SELECT filename, chunk_num, text
FROM docs_chunks
ORDER BY embedding <=> '[0.1, 0.2, ...]'::vector
LIMIT 5;
```

## Troubleshooting

### "No .docx files found"
- Check `GRAPH_FOLDER_PATH` matches your OneDrive structure
- Verify Graph API permissions are granted
- Test Drive ID: `curl -H "Authorization: Bearer $TOKEN" https://graph.microsoft.com/v1.0/drives/$GRAPH_DRIVE_ID`

### "OpenSSL SSL_connect: Connection reset by peer"
- Your firewall might be blocking OpenAI API
- Try with a different network or VPN

### "Failed to insert chunks"
- Ensure pgvector extension is installed: `CREATE EXTENSION vector;`
- Check connection string is correct
- Verify user has INSERT permissions

### Rate Limiting
- Reduce `BATCH_SIZE` (try 4 or 8)
- Add delay between batches (requires code modification)
- Use Azure OpenAI with dedicated capacity

## Next Steps

1. **Build Docker image**: `docker build -t docduck-indexer .`
2. **Deploy to Kubernetes**: `kubectl apply -f k8s/cronjob.yaml`
3. **Monitor logs**: `kubectl logs -l app=docduck-indexer --tail=100 -f`
4. **Build search API**: Query `docs_chunks` table with vector similarity
