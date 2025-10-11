# OneDrive Personal Provider Setup

This page describes how to configure DocDuck to index documents from a *personal* Microsoft OneDrive (consumer) account.
Personal accounts live in a special multi-tenant directory identified by the tenant ID `consumers`.

## Overview
The OneDrive provider uses the Microsoft Graph API with **client credentials (app registration + client secret)**. For consumer (personal) accounts, you still register an application, but you must target delegated permissions for interactive flows. Because the DocDuck indexer runs headless, you should create a separate flow to pre-consent and obtain refresh tokens if you require delegated access. For simple read-only indexing, the recommended approach is to use a **public client + device code flow**, store a refresh token, then exchange tokens at runtime. DocDuck currently implements client secret flow, so for personal accounts you must supply `TenantId = consumers` and rely on application permissions that are allowed. Microsoft Graph currently restricts application permissions for consumer accounts—many file scopes are only available via delegated permissions. If application permissions are insufficient for your scenario, consider switching to a business (Entra ID) account.

> NOTE: If you need delegated permissions, you would have to extend the provider to use `DeviceCodeCredential` instead of `ClientSecretCredential`.

## Prerequisites
- A personal Microsoft account (e.g. Outlook.com, Live.com)
- Python or browser access to perform a one-time device login if switching auth mode (not yet implemented by default)

## App Registration (Personal)
Personal accounts cannot create app registrations in Azure Portal the same way organizational tenants do. Instead:

1. Navigate to https://apps.dev.microsoft.com/ legacy portal (being deprecated) or the newer Azure App registrations by creating a free Entra ID tenant and linking your account. For pure personal use, limitations apply.
2. Prefer using a business tenant if you need scalable application permissions (`Files.Read.All`).

Because of platform limitations, the simplest supported configuration in DocDuck for personal is using `TenantId: consumers` plus a client ID and secret obtained from an application you created in an attached Entra ID tenant. This effectively treats the files as organizational once migrated. If you remain purely consumer, certain operations may fail.

## Configuration
In `appsettings.yaml` set:

```yaml
Providers:
  OneDrive:
    Enabled: true
    Name: "OneDrivePersonal"
    AccountType: "personal"
    TenantId: "consumers"            # Required for personal
    ClientId: "${GRAPH_CLIENT_ID}"   # From app registration
    ClientSecret: "${GRAPH_CLIENT_SECRET}" # From app registration
    DriveId: null                     # Optional; if null uses /me/drive
    SiteId: null                      # Not used for personal
    FolderPath: "/Documents"         # Adjust path inside personal drive
    FileExtensions:
      - ".docx"
      - ".pdf"
      - ".txt"
```

Environment variables must supply `GRAPH_CLIENT_ID`, `GRAPH_CLIENT_SECRET`.

## Obtaining Drive ID (Optional)
DocDuck will call `/me/drive` automatically. If you want to hard-code `DriveId`:

1. Acquire an access token for Graph.
2. Call `GET https://graph.microsoft.com/v1.0/me/drive`.
3. Copy the returned `id`.

## Folder Path
Use a path relative to the drive root. Personal OneDrive uses folders like `/Documents`, `/Pictures`. Subfolders can be specified, e.g. `/Documents/KnowledgeBase`.

## Permissions
For indexing you need read scopes. Application permissions like `Files.Read.All` are not granted in pure consumer context. If the provider throws authorization errors, migrate to business setup.

## Testing Connectivity
1. Enable provider and run the indexer.
2. Check logs for: `OneDrive provider 'OneDrivePersonal' initialized`.
3. If failures occur, verify secret validity and that the app registration supports Graph access for consumer accounts.

## Limitations
- Application permissions are limited for consumer accounts; delegated/device code may be required.
- Large file sets may require pagination (already handled by provider).
- Personal accounts do not support `SiteId`.

## Troubleshooting
| Symptom | Cause | Fix |
|---------|-------|-----|
| `Failed to retrieve personal OneDrive ID` | Token lacks scope or is invalid | Recreate secret, ensure Graph default scope `.default` resolves. |
| `Access denied` | Unsupported application permission | Switch to business account or implement delegated device code flow. |
| Empty document list | Wrong `FolderPath` | Confirm folder exists via Graph Explorer. |

## Cross-links & Next Steps
If personal limitations block your use case, proceed to [OneDrive Business](onedrive-business.md). For auth patterns and extending credential flows see the [Authentication Guide](../guides/authentication.md).
