# OneDrive Provider Migration: App Registration Only

**Date**: October 11, 2025  
**Status**: ✅ Complete

## Summary

Migrated the OneDrive provider to use **App registration (Client Secret) exclusively** for both personal and business accounts, removing the deprecated `UsernamePasswordCredential` authentication method.

## Changes Made

### 1. Code Changes

#### `Indexer/Providers/OneDriveProvider.cs`
- **Removed**: `UsernamePasswordCredential` support and related code
- **Simplified**: `CreateCredential()` method to only support `ClientSecretCredential`
- **Added**: Comprehensive paging support using `PageIterator` for handling large file lists
- **Unified**: Drive ID resolution logic in `GetDriveIdAsync()` method
  - Supports personal accounts via `/me/drive`
  - Supports business accounts via `DriveId` or `SiteId`
- **Improved**: Clean handling of both personal and business accounts without unnecessary code duplication

#### `Indexer/Options/ProvidersConfiguration.cs`
- **Removed**: `AuthMode`, `Username`, and `Password` properties
- **Updated**: Comments to clarify usage for personal vs business accounts
- **Simplified**: Configuration to require only `TenantId`, `ClientId`, and `ClientSecret`

#### `Indexer/appsettings.yaml`
- **Removed**: `AuthMode` configuration option
- **Updated**: Comments to explain App registration setup
- **Clarified**: Use of `TenantId: "consumers"` for personal accounts

### 2. Testing

#### `Indexer.Tests/Unit/Providers/OneDriveProviderTests.cs`
- **Created**: Comprehensive test suite with 11 test cases
- **Coverage**:
  - Constructor validation (null checks, missing credentials)
  - Personal account configuration
  - Business account configuration (with DriveId and SiteId)
  - Metadata retrieval
  - All tests passing ✅

### 3. Documentation

#### `docs/guides/authentication.md`
- **Removed**: Username/Password authentication sections
- **Simplified**: Configuration examples for both personal and business
- **Updated**: Azure AD setup instructions
- **Clarified**: Personal accounts use `TenantId: "consumers"`
- **Improved**: Troubleshooting section for App registration issues

## Key Features

### ✅ Paging Support
The provider now uses `PageIterator` to handle large file lists automatically, ensuring all files are discovered even when the OneDrive folder contains hundreds or thousands of documents.

### ✅ Clean Code
- Single authentication method (no branching for auth modes)
- Unified drive resolution logic
- No unnecessary separation between personal and business logic
- Clear, maintainable code structure

### ✅ Both Account Types Supported
- **Personal OneDrive**: Use `TenantId: "consumers"`, optionally specify `DriveId`
- **Business OneDrive**: Use organization `TenantId`, specify `DriveId` or `SiteId`

## Configuration Examples

### Personal OneDrive
```yaml
OneDrive:
  Enabled: true
  Name: "PersonalOneDrive"
  AccountType: "personal"
  TenantId: "consumers"
  ClientId: "${GRAPH_CLIENT_ID}"
  ClientSecret: "${GRAPH_CLIENT_SECRET}"
  FolderPath: "/Documents"
  FileExtensions: [".docx", ".pdf"]
```

### Business OneDrive
```yaml
OneDrive:
  Enabled: true
  Name: "BusinessOneDrive"
  AccountType: "business"
  TenantId: "${GRAPH_TENANT_ID}"
  ClientId: "${GRAPH_CLIENT_ID}"
  ClientSecret: "${GRAPH_CLIENT_SECRET}"
  DriveId: "${GRAPH_DRIVE_ID}"  # Or use SiteId
  FolderPath: "/Shared Documents/Docs"
  FileExtensions: [".docx"]
```

## Breaking Changes

Users relying on `AuthMode: "UserPassword"` must:
1. Create an Azure AD App registration
2. Configure `ClientSecret` authentication
3. Update environment variables (remove `GRAPH_USERNAME`, `GRAPH_PASSWORD`, `GRAPH_AUTH_MODE`)
4. Add `GRAPH_CLIENT_SECRET`

## Benefits

1. **Security**: Client Secret auth is more secure and supports MFA
2. **Reliability**: No issues with password expiration or MFA prompts
3. **Production-ready**: Microsoft's recommended approach for automation
4. **Maintainability**: Single auth path reduces code complexity
5. **Scalability**: Proper paging support for large file sets

## Testing Results

```
Total tests: 163
Passed: 163 ✅
Failed: 0
Skipped: 0
```

All tests pass, including the new OneDrive provider tests.

## Migration Guide

See [Authentication Guide](../guides/authentication.md) for detailed setup instructions.
