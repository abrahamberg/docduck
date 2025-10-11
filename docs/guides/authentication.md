# Authentication Configuration Guide

## OneDrive Authentication with App Registration

DocDuck uses **App registration (Client Secret)** for both personal and business OneDrive accounts. This approach provides secure, MFA-compatible authentication suitable for production use.

| Feature | App Registration (Client Secret) |
|---------|----------------------------------|
| **Supported Accounts** | Personal and Business |
| **MFA Support** | ✅ Yes |
| **Security** | ✅ High (app-only, client secret) |
| **Microsoft Recommendation** | ✅ Recommended |
| **Setup Complexity** | Medium (Azure AD app + permissions) |
| **Permissions Type** | Application (app-only) |

---

## Configuration Examples

### 1️⃣ Business OneDrive (Production)

**Use Case**: Corporate OneDrive/SharePoint, automated indexing jobs, production environments

**.env file:**
```bash
# Azure AD App Registration
GRAPH_TENANT_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
GRAPH_CLIENT_ID=yyyyyyyy-yyyy-yyyy-yyyy-yyyyyyyyyyyy
GRAPH_CLIENT_SECRET=your~client~secret~here

# OneDrive Location (use DriveId OR SiteId)
GRAPH_SITE_ID=contoso.sharepoint.com,site123,web456
GRAPH_DRIVE_ID=b!abc123...  # Optional if using SiteId
GRAPH_FOLDER_PATH=/Shared Documents/Docs

# OpenAI
OPENAI_API_KEY=sk-...
OPENAI_BASE_URL=https://api.openai.com/v1

# Database
DB_CONNECTION_STRING=Host=localhost;Database=vectors;Username=postgres;Password=pass;MinPoolSize=1;MaxPoolSize=3
```

**Azure AD Permissions Needed:**
- Microsoft Graph → Application permissions → `Files.Read.All`
- Grant admin consent

---

### 2️⃣ Personal OneDrive

**Use Case**: Personal Microsoft account (@outlook.com, @hotmail.com, etc.)

**.env file:**
```bash
# Azure AD App Registration
GRAPH_TENANT_ID=consumers  # Special tenant ID for personal accounts
GRAPH_CLIENT_ID=yyyyyyyy-yyyy-yyyy-yyyy-yyyyyyyyyyyy
GRAPH_CLIENT_SECRET=your~client~secret~here

# OneDrive Location (DriveId is optional, will use /me/drive if omitted)
GRAPH_DRIVE_ID=  # Leave empty to auto-detect
GRAPH_FOLDER_PATH=/Documents

# OpenAI
OPENAI_API_KEY=sk-...
OPENAI_BASE_URL=https://api.openai.com/v1

# Database
DB_CONNECTION_STRING=Host=localhost;Database=vectors;Username=postgres;Password=pass;MinPoolSize=1;MaxPoolSize=3
```

**Azure AD Permissions Needed:**
- Microsoft Graph → Application permissions → `Files.Read.All`
- App must support "Personal Microsoft accounts only" or "Accounts in any organizational directory and personal Microsoft accounts"

# User Credentials (must be business account without MFA)
GRAPH_USERNAME=user@company.com
GRAPH_PASSWORD=UserPassword123!

# OneDrive Location
GRAPH_DRIVE_ID=b!abc123...
GRAPH_FOLDER_PATH=/Shared Documents/Docs

# OpenAI & Database
OPENAI_API_KEY=sk-...
DB_CONNECTION_STRING=Host=localhost;Database=vectors;...
```

**Azure AD Permissions Needed:**
- Microsoft Graph → Delegated permissions → `Files.Read.All`
- **Important**: Business account **MUST NOT** have MFA enabled (rare in enterprises)

---

## Azure AD App Registration Setup

### For Personal OneDrive


---

## Setting Up Azure AD App Registration

### For Personal OneDrive

1. Go to https://portal.azure.com → Azure Active Directory → App registrations
2. Click "New registration"
   - Name: `DocDuck Indexer Personal`
   - Supported account types: **Personal Microsoft accounts only** (or "Accounts in any organizational directory and personal Microsoft accounts")
   - Redirect URI: (leave blank)
3. Note the **Application (client) ID** → use as `GRAPH_CLIENT_ID`
4. Go to "Certificates & secrets"
   - New client secret → Add description → Copy the **Value** → use as `GRAPH_CLIENT_SECRET`
5. Go to "API permissions"
   - Add permission → Microsoft Graph → Application permissions
   - Select: `Files.Read.All`
   - Click "Grant admin consent"

**Important**: Use `GRAPH_TENANT_ID=consumers` for personal accounts in your `.env` file.

### For Business OneDrive

1. Go to https://portal.azure.com → Azure Active Directory → App registrations
2. Click "New registration"
   - Name: `DocDuck Indexer Business`
   - Supported account types: **Accounts in this organizational directory only**
   - Redirect URI: (leave blank)
3. Note the **Application (client) ID** → use as `GRAPH_CLIENT_ID`
4. Note the **Directory (tenant) ID** → use as `GRAPH_TENANT_ID`
5. Go to "Certificates & secrets"
   - New client secret → Add description → Copy the **Value** → use as `GRAPH_CLIENT_SECRET`
6. Go to "API permissions"
   - Add permission → Microsoft Graph → Application permissions
   - Select: `Files.Read.All`
   - Click "Grant admin consent" (admin required)

---

## How to Get Your Drive ID and Site ID

### Personal OneDrive
You don't need to specify `GRAPH_DRIVE_ID` for personal accounts - it's fetched automatically from `/me/drive`.

### Business OneDrive
Use Graph Explorer to get your Drive ID or Site ID:

**Option 1: Get Drive ID directly**
1. Go to https://developer.microsoft.com/en-us/graph/graph-explorer
2. Sign in with your business account
3. Run: `GET https://graph.microsoft.com/v1.0/me/drive`
4. Copy the `id` field → use as `GRAPH_DRIVE_ID`

**Option 2: Get Site ID (for SharePoint sites)**
1. Go to https://developer.microsoft.com/en-us/graph/graph-explorer
2. Run: `GET https://graph.microsoft.com/v1.0/sites/{hostname}:/{site-path}`
   - Example: `GET https://graph.microsoft.com/v1.0/sites/contoso.sharepoint.com:/sites/team-a`
3. Copy the `id` field → use as `GRAPH_SITE_ID`

**Option 3: PowerShell**
```powershell
Connect-MgGraph -Scopes "Files.Read.All"
Get-MgUserDrive -UserId "user@company.com" | Select-Object Id
```

---

## Troubleshooting

### Personal OneDrive Issues

**"AADSTS700016: Application not found"**
- `GRAPH_CLIENT_ID` is incorrect
- Verify the Client ID in Azure portal
- Ensure app registration allows personal accounts

**"AADSTS7000215: Invalid client secret"**
- `GRAPH_CLIENT_SECRET` is wrong or expired
- Generate a new client secret in Azure portal

**"No items found" or "Permission denied"**
- Check `GRAPH_FOLDER_PATH` - personal OneDrive uses `/Documents`, `/Pictures`, etc.
- Verify application permissions (`Files.Read.All`) are granted
- Ensure you've clicked "Grant admin consent" in Azure portal

### Business OneDrive Issues

**"AADSTS7000215: Invalid client secret"**
- `GRAPH_CLIENT_SECRET` is wrong or expired
- Generate a new client secret in Azure portal

**"Insufficient privileges"**
- Application permissions not granted or admin consent missing
- Requires admin to grant consent in Azure portal

**"Drive/Site not found"**
- Verify `GRAPH_DRIVE_ID` or `GRAPH_SITE_ID` is correct
- Check that the app has permissions to access the drive/site

---

## Security Best Practices

### ✅ Recommended (Production)
- Use **App registration with Client Secret** for all OneDrive types
- Store secrets in Azure Key Vault or Kubernetes Secrets
- Rotate client secrets regularly (every 6-12 months)
- Use managed identities when running in Azure
- Grant only `Files.Read.All` permission (not `Files.ReadWrite.All`)

### ⚠️ Development
- Keep credentials in `.env` file (never commit to git)
- Use dedicated test account or test drive
- Limit scope of permissions to minimum required

### ❌ Not Recommended
- Storing passwords or secrets in code or config files in git
- Using overly broad permissions (`Files.ReadWrite.All` when only read is needed)
- Sharing client secrets across multiple applications

---

## Example Output

### Successful Personal OneDrive Auth
```
info: Indexer.Services.GraphClient[0]
      Graph client initialized with UserPassword auth for personal account
info: Indexer.Services.GraphClient[0]
      Using username/password authentication for tenant: consumers
warn: Indexer.Services.GraphClient[0]
      Username/Password credential is deprecated and doesn't support MFA
info: Indexer.Services.GraphClient[0]
      Listing items from personal OneDrive, Path: /Documents
info: Indexer.Services.GraphClient[0]
      Found 5 .docx files
```

### Successful Business OneDrive Auth
```
info: Indexer.Services.GraphClient[0]
      Graph client initialized with ClientSecret auth for business account
info: Indexer.Services.GraphClient[0]
      Using client secret authentication
info: Indexer.Services.GraphClient[0]
      Listing items from Drive ID: b!abc123..., Path: /Shared Documents/Docs
info: Indexer.Services.GraphClient[0]
      Found 12 .docx files
```
