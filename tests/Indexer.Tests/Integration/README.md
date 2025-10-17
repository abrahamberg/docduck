# Integration Tests for Document Providers

This directory contains integration tests for each document provider implementation. These tests validate the core functionality against real provider services and require proper configuration to run.

## Overview

The integration tests cover three main aspects for each provider:

1. **List Documents with Metadata** - Verifies the provider can list files with proper metadata
2. **Download Documents** - Tests downloading individual files by document ID
3. **Paging Support** - For providers that support paging, tests that all documents are returned correctly across multiple pages

## Providers Tested

### Local Provider (`LocalProviderIntegrationTests`)
- **Paging**: Not applicable (lists all files at once)
- **Setup**: Automatically creates test files in a temporary directory
- **Requirements**: None (runs automatically)

### S3 Provider (`S3ProviderIntegrationTests`)
- **Paging**: ✅ Supported via continuation tokens
- **Setup**: Requires AWS credentials and S3 bucket
- **Requirements**: Environment variables (see below)

### OneDrive Provider (`OneDriveProviderIntegrationTests`) 
- **Paging**: ✅ Supported via Microsoft Graph PageIterator
- **Setup**: Requires OneDrive app registration
- **Requirements**: Environment variables (see below)

## Running Tests

### All Tests
```bash
dotnet test --filter "Category!=Integration" # Skip integration tests
dotnet test --filter "Category=Integration"   # Run only integration tests
dotnet test                                   # Run all tests (unit + integration)
```

### Specific Provider Tests
```bash
# Local provider (no setup required)
dotnet test --filter "ClassName~LocalProviderIntegrationTests"

# S3 provider (requires AWS setup)
dotnet test --filter "ClassName~S3ProviderIntegrationTests"

# OneDrive provider (requires OneDrive setup)  
dotnet test --filter "ClassName~OneDriveProviderIntegrationTests"
```

## Environment Configuration

### S3 Provider Setup

Create an S3 bucket and IAM user with appropriate permissions, then set:

```bash
export AWS_ACCESS_KEY_ID="your-access-key-id"
export AWS_SECRET_ACCESS_KEY="your-secret-access-key"  
export AWS_TEST_BUCKET="your-test-bucket-name"
export AWS_REGION="us-east-1"  # Optional, defaults to us-east-1
```

**Required S3 Permissions:**
```json
{
    "Version": "2012-10-17",
    "Statement": [
        {
            "Effect": "Allow",
            "Action": [
                "s3:GetObject",
                "s3:PutObject", 
                "s3:DeleteObject",
                "s3:ListBucket"
            ],
            "Resource": [
                "arn:aws:s3:::your-test-bucket-name",
                "arn:aws:s3:::your-test-bucket-name/*"
            ]
        }
    ]
}
```

### OneDrive Provider Setup

1. **Register an App in Azure AD:**
   - Go to Azure Portal > App registrations
   - Create a new registration
   - Note the Application (client) ID and Directory (tenant) ID
   - Create a client secret under "Certificates & secrets"

2. **Configure API Permissions:**
   - Add Microsoft Graph permissions:
     - `Files.Read.All` (Application permission)
     - `Sites.Read.All` (Application permission)  
   - Grant admin consent for your organization

3. **Set Environment Variables:**
```bash
export ONEDRIVE_TENANT_ID="your-tenant-id"
export ONEDRIVE_CLIENT_ID="your-client-id"
export ONEDRIVE_CLIENT_SECRET="your-client-secret"

# Optional - specify target location
export ONEDRIVE_DRIVE_ID="your-drive-id"           # Direct drive access
export ONEDRIVE_SITE_ID="your-site-id"             # Or site-based access  
export ONEDRIVE_FOLDER_PATH="/Shared Documents/Docs"  # Folder path (default shown)
```

**Finding OneDrive IDs:**
```bash
# Get site ID (for SharePoint/OneDrive for Business)
curl -X GET "https://graph.microsoft.com/v1.0/sites/root" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN"

# Get drive ID  
curl -X GET "https://graph.microsoft.com/v1.0/sites/{site-id}/drive" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN"
```

## Test Details

### What Each Test Validates

#### Core Functionality (All Providers)
- `ListDocuments_ShouldReturnAllValidFiles()` - Basic document listing with metadata
- `DownloadDocument_WithValidId_ShouldReturnFileStream()` - Document download
- `DownloadDocument_WithInvalidId_ShouldThrowException()` - Error handling
- `GetMetadata_ShouldReturnExpectedInformation()` - Provider metadata
- `Provider_Properties_ShouldMatchConfiguration()` - Configuration mapping

#### Paging Tests (S3 & OneDrive)
- `ListDocuments_WithManyFiles_ShouldHandlePaging()` - Tests with >10 files
- `ListDocuments_WithPaging_ShouldReturnAllDocuments()` - Verifies no duplicates/missing files

#### Provider-Specific Tests

**Local Provider:**
- File exclusion pattern testing
- Recursive directory traversal
- ETag generation and change detection

**S3 Provider:**  
- Prefix-based filtering
- Extension filtering
- Pagination with continuation tokens

**OneDrive Provider:**
- Consistent document ID generation
- Last modified date validation
- Empty folder handling

### Test Data Management

- **Local Provider**: Creates temporary test files automatically
- **S3 Provider**: Uploads test files to a unique prefix, cleans up after tests
- **OneDrive Provider**: Tests against existing files (read-only)

### Debugging Failed Tests

1. **Check Environment Variables**: Ensure all required variables are set
2. **Verify Permissions**: Confirm service accounts have necessary access  
3. **Network Connectivity**: Ensure test environment can reach cloud services
4. **Enable Logging**: Set log level to Debug to see detailed provider operations

```bash
export LOGGING__LOGLEVEL__DEFAULT=Debug
dotnet test --logger "console;verbosity=detailed"
```

## Continuous Integration

To run integration tests in CI/CD:

1. **Skip by Default**: Integration tests are skipped when credentials aren't available
2. **Secure Secrets**: Store credentials as encrypted secrets in your CI system
3. **Test Isolation**: Each test run uses unique prefixes/paths to avoid conflicts
4. **Cleanup**: Tests clean up their test data automatically

Example GitHub Actions configuration:
```yaml
- name: Run Integration Tests
  env:
    AWS_ACCESS_KEY_ID: ${{ secrets.AWS_ACCESS_KEY_ID }}
    AWS_SECRET_ACCESS_KEY: ${{ secrets.AWS_SECRET_ACCESS_KEY }}  
    AWS_TEST_BUCKET: ${{ secrets.AWS_TEST_BUCKET }}
    ONEDRIVE_TENANT_ID: ${{ secrets.ONEDRIVE_TENANT_ID }}
    ONEDRIVE_CLIENT_ID: ${{ secrets.ONEDRIVE_CLIENT_ID }}
    ONEDRIVE_CLIENT_SECRET: ${{ secrets.ONEDRIVE_CLIENT_SECRET }}
  run: dotnet test --filter "Category=Integration"
```