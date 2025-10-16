# Providers Overview

Providers supply documents for indexing. Each provider implementation yields a list of documents with IDs, filenames, ETags, and optional last-modified timestamps.

## Supported Providers

| Type | Description | Notes |
|------|-------------|-------|
| `local` | Local filesystem directory tree | Fast iteration, path based IDs |
| `onedrive` | Microsoft OneDrive / SharePoint (Graph API) | App-only or delegated auth |
| `s3` | AWS S3 bucket/prefix | Minimal permissions recommended |

## Provider Concepts
- **Provider Type**: Logical category (`onedrive`, `s3`, `local`).
- **Provider Name**: Instance label (e.g. `finance-drive`). Helps segregate multiple drives/buckets.
- **Document ID**: Stable identifier (path, drive item ID, S3 key).
- **ETag**: Change detector; drives incremental indexing.

## Lifecycle
1. Enumerate documents
2. Filter unchanged (ETag match)
3. Download new/updated
4. Extract → Chunk → Embed → Upsert
5. Cleanup orphaned docs (optional)

## Index Registration
On each run, enabled providers are registered in the database with last sync time. The API exposes active providers at `/providers`.

## Adding Providers
High-level steps (see developer guide for full detail):
1. Implement `IDocumentProvider`
2. Expose enabling env vars (e.g. `PROVIDER_MYTYPE_ENABLED`)
3. Register with provider catalog
4. Provide metadata (display name, description)
5. Optionally add configuration seeding

See: [Provider Framework](../developer/provider-framework.md)

## Next
- OneDrive specifics: [OneDrive](onedrive.md)
- S3 specifics: [S3](s3.md)
- Local specifics: [Local Files](local.md)
