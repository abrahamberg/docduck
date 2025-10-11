# Authentication Configuration Guide

## Comparison: Business vs Personal OneDrive

| Feature | Business OneDrive (Client Secret) | Personal OneDrive (Username/Password) |
|---------|-----------------------------------|--------------------------------------|
| **Best For** | Production, CronJobs, CI/CD | Personal use, development, testing |
| **MFA Support** | ✅ Yes | ❌ No (account must not have MFA) |
| **Auth Mode** | `ClientSecret` | `UserPassword` |
| **Account Type** | `business` | `personal` |
| **Security** | ✅ High (app-only, client secret) | ⚠️ Lower (password in env vars) |
| **Microsoft Recommendation** | ✅ Recommended | ⚠️ Deprecated |
| **Setup Complexity** | Medium (Azure AD app + permissions) | Low (just username/password) |
| **Permissions Type** | Application (app-only) | Delegated (user context) |

---

## Configuration Examples

### 1️⃣ Business OneDrive with Client Secret (Production)

**Use Case**: Corporate OneDrive/SharePoint, automated indexing jobs, production environments

**.env file:**
```bash
# Authentication
GRAPH_AUTH_MODE=ClientSecret
GRAPH_ACCOUNT_TYPE=business

# Azure AD App Registration
GRAPH_TENANT_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
GRAPH_CLIENT_ID=yyyyyyyy-yyyy-yyyy-yyyy-yyyyyyyyyyyy
GRAPH_CLIENT_SECRET=your~client~secret~here

# OneDrive Location
GRAPH_DRIVE_ID=b!abc123...
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

### 2️⃣ Personal OneDrive with Username/Password (Development)

**Use Case**: Personal Microsoft account (@outlook.com, @hotmail.com, etc.), local testing, development

**.env file:**
```bash
# Authentication
GRAPH_AUTH_MODE=UserPassword
GRAPH_ACCOUNT_TYPE=personal

# Azure AD App (still needed for CLIENT_ID)
GRAPH_CLIENT_ID=yyyyyyyy-yyyy-yyyy-yyyy-yyyyyyyyyyyy

# Your Microsoft Account Credentials
GRAPH_USERNAME=yourname@outlook.com
GRAPH_PASSWORD=YourPassword123!

# OneDrive Location (personal OneDrive structure)
GRAPH_FOLDER_PATH=/Documents

# OpenAI
OPENAI_API_KEY=sk-...
OPENAI_BASE_URL=https://api.openai.com/v1

# Database
DB_CONNECTION_STRING=Host=localhost;Database=vectors;Username=postgres;Password=pass;MinPoolSize=1;MaxPoolSize=3
```

**Azure AD Permissions Needed:**
- Microsoft Graph → Delegated permissions → `Files.Read.All`
- Supports work and school accounts AND personal Microsoft accounts
- **Important**: Your personal account **MUST NOT** have MFA enabled

---

### 3️⃣ Business OneDrive with Username/Password (Delegated Access)

**Use Case**: Business account without app permissions, user-specific access

**.env file:**
```bash
# Authentication
GRAPH_AUTH_MODE=UserPassword
GRAPH_ACCOUNT_TYPE=business

# Azure AD
GRAPH_TENANT_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
GRAPH_CLIENT_ID=yyyyyyyy-yyyy-yyyy-yyyy-yyyyyyyyyyyy

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

1. Go to https://portal.azure.com → Azure Active Directory → App registrations
2. Click "New registration"
   - Name: `DocDuck Indexer Personal`
   - Supported account types: **Accounts in any organizational directory and personal Microsoft accounts**
   - Redirect URI: (leave blank for now)
3. Note the **Application (client) ID** → use as `GRAPH_CLIENT_ID`
4. Go to "API permissions"
   - Add permission → Microsoft Graph → Delegated permissions
   - Select: `Files.Read.All` or `Files.ReadWrite.All`
   - Click "Grant admin consent" (if you have admin rights)
5. Go to "Authentication"
   - Enable "Allow public client flows" → Yes (required for username/password)

### For Business OneDrive (Client Secret)

1. Follow steps 1-2 above, but choose: **Accounts in this organizational directory only**
2. Go to "Certificates & secrets"
   - New client secret → Add description → Copy the **Value** → use as `GRAPH_CLIENT_SECRET`
3. Go to "API permissions"
   - Add permission → Microsoft Graph → Application permissions
   - Select: `Files.Read.All`
   - Click "Grant admin consent" (admin required)

---

## How to Get Your Drive ID

### Personal OneDrive
You don't need to specify `GRAPH_DRIVE_ID` for personal accounts - it's fetched automatically from `/me/drive`.

### Business OneDrive
Use Graph Explorer or PowerShell:

**Option 1: Graph Explorer**
1. Go to https://developer.microsoft.com/en-us/graph/graph-explorer
2. Sign in
3. Run: `GET https://graph.microsoft.com/v1.0/me/drive`
4. Copy the `id` field

**Option 2: PowerShell**
```powershell
Connect-MgGraph -Scopes "Files.Read.All"
Get-MgUserDrive -UserId "user@company.com" | Select-Object Id
```

---

## Troubleshooting

### Personal OneDrive Issues

**"AADSTS50076: MFA is required"**
- Your account has MFA enabled
- Solution: Disable MFA on your personal account (not recommended) OR use a test account without MFA

**"AADSTS50126: Invalid username or password"**
- Double-check `GRAPH_USERNAME` and `GRAPH_PASSWORD`
- Ensure you used the correct email format
- Try signing in manually at https://outlook.com to verify credentials

**"AADSTS700016: Application not found"**
- `GRAPH_CLIENT_ID` is incorrect
- Verify the Client ID in Azure portal
- Ensure app registration allows personal accounts

**"No items found" or "Permission denied"**
- Check `GRAPH_FOLDER_PATH` - personal OneDrive uses `/Documents`, `/Pictures`, etc.
- Verify delegated permissions are granted
- Ensure "Allow public client flows" is enabled

### Business OneDrive Issues

**"AADSTS7000215: Invalid client secret"**
- `GRAPH_CLIENT_SECRET` is wrong or expired
- Generate a new client secret in Azure portal

**"Insufficient privileges"**
- Application permissions not granted or admin consent missing
- Requires admin to grant consent in Azure portal

---

## Security Best Practices

### ✅ Recommended (Production)
- Use **Client Secret** auth for business OneDrive
- Store secrets in Azure Key Vault or Kubernetes Secrets
- Rotate client secrets regularly
- Use managed identities when possible

### ⚠️ Acceptable (Development/Personal)
- Use **Username/Password** for personal OneDrive testing
- Keep credentials in `.env` file (never commit to git)
- Use dedicated test account, not your primary account
- Limit scope of permissions

### ❌ Not Recommended
- Username/password in production
- Storing passwords in code or config files in git
- Using primary Microsoft account with production data
- Disabling MFA on business accounts

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
