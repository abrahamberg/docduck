# Provider Overview

DocDuck supports multiple document sources ("providers") that surface raw documents for indexing and embedding.
Each provider implements a small contract so the Indexer can:

1. Enumerate available documents (List)
2. Fetch binary content (Download)
3. Supply metadata (ETag, size, modified time, MIME type) for change detection

Current providers:

| Provider | Type Key | Typical Use | Auth Mode |
|----------|----------|-------------|-----------|
| Local Filesystem | `local` | Development & on-prem ingestion | Path access (host filesystem) |
| AWS S3 | `s3` | Cloud object storage | Access keys / IAM role |
| Microsoft OneDrive Personal | `onedrive` | Individual user's files | Microsoft consumer tenant "consumers" + app registration |
| Microsoft OneDrive Business | `onedrive` | Organizational SharePoint/OneDrive | Azure AD tenant + app registration |

## Configuration Loading
Provider settings are loaded from `appsettings.yaml` (section `Providers`) with environment variable expansion (e.g. `${GRAPH_CLIENT_ID}`).

Minimal snippet:

```yaml
Providers:
  OneDrive:
    Enabled: true
    AccountType: "business"
    TenantId: "${GRAPH_TENANT_ID}"
    ClientId: "${GRAPH_CLIENT_ID}"
    ClientSecret: "${GRAPH_CLIENT_SECRET}"
    DriveId: "${GRAPH_DRIVE_ID}"
    FolderPath: "/Shared Documents/Docs"
```

## Document Filtering
Each provider declares `FileExtensions` to restrict indexing scope. ETags (cloud) or computed values (local) are used for incremental updates.

## Adding a New Provider
To add a provider:

1. Create a class implementing `IDocumentProvider` under `Indexer/Providers/`
2. Add a config POCO to `ProvidersConfiguration`
3. Extend YAML & bind options in `Program.cs`
4. Provide a page under this `providers/` docs folder with setup steps

See existing provider pages for patterns.
