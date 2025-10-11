# 🎉 Dual Authentication Support Added!

## Summary of Changes

Your Indexer now supports **BOTH** authentication methods, allowing users to choose based on their needs:

### ✅ What's New

1. **Two Authentication Modes**
   - 🏢 **Client Secret** (Business OneDrive - app-only, production-ready)
   - 👤 **Username/Password** (Personal OneDrive - development/testing)

2. **Flexible Account Types**
   - **Business** accounts (SharePoint, corporate OneDrive)
   - **Personal** accounts (@outlook.com, @hotmail.com)

3. **Smart Endpoint Selection**
   - Automatically uses correct Microsoft Graph API endpoints based on account type
   - Personal: `/me/drive` → `/drives/{id}`
   - Business: `/drives/{id}` or `/sites/{id}/drive`

---

## New Environment Variables

```bash
# Choose your authentication method
GRAPH_AUTH_MODE=ClientSecret          # or "UserPassword"
GRAPH_ACCOUNT_TYPE=business           # or "personal"

# For UserPassword mode (personal OneDrive)
GRAPH_USERNAME=yourname@outlook.com   # Optional
GRAPH_PASSWORD=your-password          # Optional

# Existing variables still work for ClientSecret mode
GRAPH_TENANT_ID=...
GRAPH_CLIENT_ID=...
GRAPH_CLIENT_SECRET=...
```

---

## Configuration Examples

### 🏢 Business OneDrive (Recommended for Production)
```bash
GRAPH_AUTH_MODE=ClientSecret
GRAPH_ACCOUNT_TYPE=business
GRAPH_TENANT_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
GRAPH_CLIENT_ID=yyyyyyyy-yyyy-yyyy-yyyy-yyyyyyyyyyyy
GRAPH_CLIENT_SECRET=your~secret~here
GRAPH_DRIVE_ID=b!abc123...
GRAPH_FOLDER_PATH=/Shared Documents/Docs
```

### 👤 Personal OneDrive (For Personal Use)
```bash
GRAPH_AUTH_MODE=UserPassword
GRAPH_ACCOUNT_TYPE=personal
GRAPH_CLIENT_ID=yyyyyyyy-yyyy-yyyy-yyyy-yyyyyyyyyyyy
GRAPH_USERNAME=yourname@outlook.com
GRAPH_PASSWORD=YourPassword123!
GRAPH_FOLDER_PATH=/Documents
```

---

## Files Modified

### Code Changes
1. **`Indexer/Options/GraphOptions.cs`**
   - Added `AuthMode` property (ClientSecret/UserPassword)
   - Added `AccountType` property (business/personal)
   - Added `Username` and `Password` properties
   - Kept all existing properties for backward compatibility

2. **`Indexer/Services/GraphClient.cs`**
   - New `CreateCredential()` method with conditional logic
   - Supports both `ClientSecretCredential` and `UsernamePasswordCredential`
   - Auto-detects personal vs business account endpoints
   - Uses `/me/drive` for personal accounts
   - Improved logging for auth mode and account type

3. **`Indexer/Program.cs`**
   - Reads new environment variables: `GRAPH_AUTH_MODE`, `GRAPH_ACCOUNT_TYPE`, `GRAPH_USERNAME`, `GRAPH_PASSWORD`
   - Backward compatible with existing configs

### Documentation Updates
4. **`.env.example`** - Updated with both auth modes
5. **`README.md`** - Added authentication options section
6. **`QUICKSTART.md`** - Separate setup guides for both modes
7. **`AUTHENTICATION.md`** - NEW comprehensive auth guide with troubleshooting

---

## Backward Compatibility

✅ **Existing configurations still work!** If you don't set `GRAPH_AUTH_MODE`, it defaults to `ClientSecret`.

Your old `.env` file will work exactly as before:
```bash
# Old config - still works!
GRAPH_TENANT_ID=...
GRAPH_CLIENT_ID=...
GRAPH_CLIENT_SECRET=...
GRAPH_DRIVE_ID=...
```

---

## Usage

### Quick Test with Personal OneDrive
```bash
export GRAPH_AUTH_MODE=UserPassword
export GRAPH_ACCOUNT_TYPE=personal
export GRAPH_CLIENT_ID=your-app-id
export GRAPH_USERNAME=yourname@outlook.com
export GRAPH_PASSWORD=your-password
export GRAPH_FOLDER_PATH=/Documents
export OPENAI_API_KEY=sk-...
export DB_CONNECTION_STRING="Host=localhost;..."
export MAX_FILES=3

cd Indexer
dotnet run
```

### Production with Business OneDrive
```bash
export GRAPH_AUTH_MODE=ClientSecret
export GRAPH_ACCOUNT_TYPE=business
export GRAPH_TENANT_ID=...
export GRAPH_CLIENT_ID=...
export GRAPH_CLIENT_SECRET=...
export GRAPH_DRIVE_ID=...
export OPENAI_API_KEY=sk-...
export DB_CONNECTION_STRING="Host=localhost;..."

cd Indexer
dotnet run
```

---

## Important Notes

### ⚠️ Username/Password Limitations
- **Does NOT work** with MFA-enabled accounts
- Deprecated by Microsoft (we log a warning)
- Best for personal accounts or testing
- Not recommended for production

### ✅ Client Secret Advantages
- Works with MFA-enabled accounts
- Recommended by Microsoft
- More secure for production
- Better for unattended jobs/CronJobs

---

## Testing

All tests still pass ✅:
```bash
cd Indexer.Tests
dotnet test
# Test summary: total: 6, failed: 0, succeeded: 6
```

Build succeeds with 1 expected warning (deprecated UsernamePasswordCredential):
```bash
cd Indexer
dotnet build
# Build succeeded with 1 warning(s)
```

---

## Documentation

📚 Read the new **AUTHENTICATION.md** for:
- Detailed comparison table
- Azure AD setup for both modes
- Configuration examples
- Troubleshooting guide
- Security best practices
- Common error messages and solutions

---

## Next Steps

1. Choose your authentication mode based on your needs
2. Update your `.env` file or environment variables
3. Follow the setup guide in `AUTHENTICATION.md`
4. Test with `MAX_FILES=3` first
5. Deploy to production!

Happy indexing! 🚀
