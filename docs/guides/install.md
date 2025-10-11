# Advanced Install & Setup

This guide covers full multi-provider configuration, OneDrive (Business & Personal), S3, environment variables, indexing behavior, troubleshooting, and deployment options. For the fastest way to see the app running, use the **[Quickstart](quickstart.md)** guide instead.

## Contents
- OneDrive Business (Client Secret)
- OneDrive Personal (Username/Password)
- Local Provider (extended config)
- S3 Provider
- Environment Variables Reference
- Multi-Provider Docker Compose
- Indexing Lifecycle
- Troubleshooting & Diagnostics
- Performance & Scaling
- Kubernetes Deployment Notes
- Next Steps

---
## OneDrive Business (Client Secret)
Use for unattended indexing jobs and production workloads.

### Azure AD App Registration
1. Azure Portal → Azure Active Directory → App registrations → New registration
2. Record Application (client) ID and Directory (tenant) ID
3. Create a client secret (Certificates & secrets)
4. API Permissions: Microsoft Graph → Application → `Files.Read.All` (or `Sites.Read.All` for broader access)
5. Grant admin consent

### Drive ID
Use Graph Explorer:
```
GET https://graph.microsoft.com/v1.0/me/drive
```
Copy the `id` field.

### Required Variables
```
GRAPH_AUTH_MODE=ClientSecret
GRAPH_ACCOUNT_TYPE=business
GRAPH_TENANT_ID=your-tenant-id
GRAPH_CLIENT_ID=your-client-id
GRAPH_CLIENT_SECRET=your-client-secret
GRAPH_DRIVE_ID=drive-id-from-graph
GRAPH_FOLDER_PATH="/Shared Documents/Docs"
```

## OneDrive Personal (Username/Password)
Use only for development or personal accounts (no MFA).

```
GRAPH_AUTH_MODE=UserPassword
GRAPH_ACCOUNT_TYPE=personal
GRAPH_CLIENT_ID=your-client-id
GRAPH_USERNAME=yourname@outlook.com
GRAPH_PASSWORD=your-password
GRAPH_FOLDER_PATH="/Documents"
```
Notes:
- Account must not have MFA
- Azure AD app registration still required (CLIENT_ID)
- Tenant implicitly `consumers` when `GRAPH_ACCOUNT_TYPE=personal`
- Permissions: delegated `Files.Read.All` or `Files.ReadWrite.All`

## Local Provider (Extended)
Configured via `Indexer/appsettings.local.yaml`. Adjust file types, chunk sizes, or exclusion rules then rerun the indexer container.

## S3 Provider
(Placeholder – define bucket, prefix, access keys)
```
S3_ENABLED=true
S3_BUCKET=my-bucket
S3_PREFIX=docs/
AWS_ACCESS_KEY_ID=...
AWS_SECRET_ACCESS_KEY=...
AWS_REGION=us-east-1
```
Add provider block to your appsettings file. Re-index after enabling.

## Environment Variables Reference
```
OPENAI_API_KEY=sk-...                # Required for embeddings + chat
DB_CONNECTION_STRING=Host=...        # Postgres with pgvector
MAX_FILES=3                          # Optional limit for test runs
GRAPH_*                               # OneDrive settings (see above)
S3_*                                  # S3 settings
```

## Multi-Provider Docker Compose
Use `docker-compose-multi-provider.yml` for combining providers. Ensure each required env var is exported before `up`.

## Indexing Lifecycle
1. Indexer loads config (providers, file filters)
2. Lists candidate documents
3. Deduplicates / skips unchanged items (if hashing enabled)
4. Extracts text, chunks content
5. Embeds chunks and upserts into Postgres
6. Exits with code 0 when done
Re-run the indexer container to refresh data after changes.

## Troubleshooting
- No files found: validate path or permissions, reduce `MAX_FILES`
- OpenAI errors: network/firewall issues, temporary rate limit – retry later
- Failed chunk inserts: ensure `CREATE EXTENSION vector;` executed
- Performance slow: reduce embedding concurrency or increase Postgres resources

## Diagnostics
Check health:
```
curl http://localhost:8080/health | jq
```
`chunks` and `documents` should increase after indexing. `openAiKeyPresent` and `dbConnectionPresent` must be true.

## Performance & Scaling
- Increase Postgres resources (shared buffers, work_mem)
- Consider async batching for embeddings (future enhancement)
- Add caching layer for frequent queries (outside current scope)

## Kubernetes Notes
Use provided manifests in `k8s/` for API and scheduled indexer job. Customize resource requests and secrets via standard Kubernetes mechanisms.

## Next Steps
Return to **[Quickstart](quickstart.md)** for the minimal run scenario or integrate additional providers by extending your appsettings and environment variables.
