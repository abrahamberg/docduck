# OneDrive Provider

Index documents from Microsoft OneDrive / SharePoint via Microsoft Graph API.

## Auth Modes
| Mode | Env `GRAPH_AUTH_MODE` | Use Case |
|------|-----------------------|----------|
| Client Secret | `ClientSecret` | App-only, production (business) |
| User Password | `UserPassword` | Personal / delegated dev (no MFA) |

## Required Variables (Client Secret)
```
PROVIDER_ONEDRIVE_ENABLED=true
PROVIDER_ONEDRIVE_NAME=corpdrive
GRAPH_AUTH_MODE=ClientSecret
GRAPH_ACCOUNT_TYPE=business
GRAPH_TENANT_ID=...
GRAPH_CLIENT_ID=...
GRAPH_CLIENT_SECRET=...
GRAPH_DRIVE_ID=...            # Prefer drive ID
# or GRAPH_SITE_ID=...        # SharePoint site alternative
GRAPH_FOLDER_PATH=/Shared Documents/Docs
```

## Required Variables (User/Password – Dev Only)
```
PROVIDER_ONEDRIVE_ENABLED=true
PROVIDER_ONEDRIVE_NAME=personal
GRAPH_AUTH_MODE=UserPassword
GRAPH_ACCOUNT_TYPE=personal
GRAPH_CLIENT_ID=...
GRAPH_USERNAME=user@outlook.com
GRAPH_PASSWORD=plaintext-or-secret-ref
GRAPH_FOLDER_PATH=/Documents
```

## Permissions
- App-only: Files.Read.All or Sites.Read.All
- Delegated: Files.Read or Files.Read.All

Follow least privilege. Narrow folder path if possible.

## Notes
- ETag used for change detection.
- Large files: ensure extraction memory limits are sufficient.
- Rate limits: consider spacing runs if hitting Graph throttling.

## Troubleshooting
| Issue | Cause | Fix |
|-------|-------|-----|
| 401 Unauthorized | Wrong secret or permission | Recreate secret / verify AAD app scopes |
| No files found | Wrong folder path | Check drive explorer / path case |
| MFA blocked | Using password mode with MFA account | Switch to ClientSecret mode |

## Next
- Back: [Providers Overview](index.md)
- Adding a provider: [Provider Framework](../developer/provider-framework.md)
