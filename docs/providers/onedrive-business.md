# OneDrive Business Provider Setup

This page explains configuring DocDuck to index files from a Microsoft 365 / Entra ID (Azure AD) tenant using OneDrive or SharePoint document libraries via Microsoft Graph.

## Overview
The provider uses Microsoft Graph with **client credentials** (application permissions) supplied via `ClientSecretCredential`. You can target either:

- A user personal drive (`DriveId` or default /me/drive with delegated auth — not used here)  
- A SharePoint site default drive (`SiteId`)  
- A specific drive (`DriveId`) such as a document library

You must grant the application appropriate application permissions and admin consent.

## Prerequisites
- Azure subscription / Microsoft 365 tenant (Entra ID tenant ID)
- Permissions to create App registrations and grant admin consent
- Desired SharePoint site or drive identifiers

## Azure App Registration Steps
1. Sign into Azure Portal: https://portal.azure.com
2. Navigate: Azure Active Directory > App registrations > New registration
3. Name: `docduck-onedrive-indexer`
4. Supported account types: Single tenant (recommended) or multi-tenant if needed.
5. Redirect URI: Not required for client credentials.
6. After creation, record:
   - `Application (client) ID`
   - `Directory (tenant) ID`
7. Certificates & secrets: Create a new client secret; record the value (not retrievable later).

## Required Application Permissions
Under API permissions > Add a permission > Microsoft Graph > Application permissions add:

| Permission | Purpose |
|------------|---------|
| `Files.Read.All` | Read all files user has access to (drive & SharePoint) |
| `Sites.Read.All` | Enumerate site drives when using `SiteId` |
| `User.Read.All` (optional) | Diagnostic user lookups if extended |

Click "Grant admin consent" for the tenant.

## Selecting Drive vs Site
- Use `DriveId` if you have the ID of a specific document library (fastest path).  
- Use `SiteId` to let DocDuck fetch the default drive of that site.  
- Do NOT set both unless targeting a specific drive ID; `SiteId` is ignored if `DriveId` is provided.

## Finding Identifiers
### Tenant ID
Azure Active Directory > Overview > Tenant ID.

### Site ID
Use Graph Explorer or REST:
```
GET https://graph.microsoft.com/v1.0/sites?search=YOUR_SITE_NAME
```
Pick the `id` (format usually: `{hostname},{siteId},{webId}`), or for known host:
```
GET https://graph.microsoft.com/v1.0/sites/{hostname}:/sites/{site-path}
```

### Drive ID
For a site:
```
GET https://graph.microsoft.com/v1.0/sites/{site-id}/drive
```
For a document library under a site (if multiple libraries):
```
GET https://graph.microsoft.com/v1.0/sites/{site-id}/drives
```

## Configuration
Example snippet in `appsettings.yaml`:
```yaml
Providers:
  OneDrive:
    Enabled: true
    Name: "OneDriveCorp"
    AccountType: "business"
    TenantId: "${GRAPH_TENANT_ID}"          # e.g. 11111111-2222-3333-4444-555555555555
    ClientId: "${GRAPH_CLIENT_ID}"          # App registration client ID
    ClientSecret: "${GRAPH_CLIENT_SECRET}"  # Client secret value
    SiteId: "${GRAPH_SITE_ID}"              # Optional if DriveId provided
    DriveId: "${GRAPH_DRIVE_ID}"            # Optional; if omitted will use SiteId drive
    FolderPath: "/Shared Documents/Docs"    # Path inside the selected drive
    FileExtensions:
      - ".docx"
      - ".pdf"
      - ".txt"
```
Supply environment variables for all `${...}` placeholders.

## Folder Path Guidance
Path must start with `/` and be relative to the drive root. For standard SharePoint document library the default root often contains `Shared Documents`. Validate via Graph Explorer:
```
GET https://graph.microsoft.com/v1.0/drives/{drive-id}/root/children
```

## Least Privilege
If you only need a subset, you can attempt `Files.Read.Selected` plus `Sites.Read.All` and grant permissions to specific sites/drives with `Granular delegated admin`—however for non-interactive indexing, broad permissions are simpler. Revisit least privilege after MVP.

## Running the Indexer
1. Export environment variables (or update Docker/K8s manifests).
2. Set `Enabled: true` in OneDrive section.
3. Start the indexer and check logs:
   - `OneDrive provider 'OneDriveCorp' initialized for business`
   - Listing output indicating drive & path.

## Common Errors
| Error | Cause | Resolution |
|-------|-------|------------|
| `Failed to retrieve Drive ID from Site` | Wrong `SiteId` | Re-query site via Graph Explorer; ensure not a hub site redirect. |
| `Access Denied` | Missing admin consent | Grant admin consent for all added Graph application permissions. |
| `No items found in OneDrive path` | Incorrect `FolderPath` | Confirm path exists; remove leading/trailing spaces. |
| `invalid_client` | Bad secret or client ID | Recreate secret; verify environment variable expansion. |

## Performance Notes
- Provider uses Graph pagination iterator; large libraries are streamed efficiently.
- ETag-based change detection reduces redundant processing.
- Consider narrowing `FileExtensions` for speed.

## Security Considerations
- Store `ClientSecret` in secret manager / K8s secret, not committed to source.
- Rotate client secret periodically.
- Use a dedicated app registration with only necessary permissions; avoid granting write scopes.

## Troubleshooting Checklist
1. Confirm environment variables present: `GRAPH_TENANT_ID`, `GRAPH_CLIENT_ID`, `GRAPH_CLIENT_SECRET`.
2. Validate permissions in Azure Portal show "Granted for <tenant>".
3. Test a Graph call with the OAuth token manually if needed.
4. Ensure network egress allows calls to `graph.microsoft.com`.

## Cross-links & Next Steps
Need consumer files? See [OneDrive Personal](onedrive-personal.md). Combine with [AWS S3](s3.md) or [Local Filesystem](local.md) for multi-source ingestion. Review broader auth concepts in the [Authentication Guide](../guides/authentication.md).
