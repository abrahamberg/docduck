# Comprehensive Test Plan for DocDuck Indexer

## Test Strategy

### Unit Tests
Test individual components in isolation with mocked dependencies.

### Integration Tests
Test component interactions with real dependencies (database, file system).
- Use test containers for PostgreSQL (different port)
- Use conditional execution based on environment variables for provider credentials
- Minimize resource usage with small test datasets

---

## Unit Tests Plan

### 1. Text Extraction Tests (`TextExtraction/`)

#### 1.1 PlainTextExtractor Tests
- ✅ Extract from .txt file
- ✅ Extract from .md file
- ✅ Extract from .json file
- ✅ Extract from .csv file
- ✅ Handle UTF-8 encoding
- ✅ Handle empty files
- ✅ Handle null stream (should throw)
- ✅ Handle null filename (should throw)
- ✅ Verify supported extensions list

#### 1.2 DocxTextExtractor Tests
- ✅ Extract from valid .docx file
- ✅ Extract multi-paragraph document
- ✅ Handle empty .docx
- ✅ Handle corrupted .docx (should throw)
- ✅ Handle null stream (should throw)
- ✅ Verify supported extensions (.docx only)
- ✅ Handle cancellation token

#### 1.3 PdfTextExtractor Tests
- ✅ Extract from single-page PDF
- ✅ Extract from multi-page PDF
- ✅ Handle empty PDF
- ✅ Handle corrupted PDF (should throw)
- ✅ Handle null stream (should throw)
- ✅ Verify page separation (blank lines between pages)
- ✅ Handle cancellation token mid-extraction

#### 1.4 TextExtractionService Tests
- ✅ IsSupported returns true for registered extensions
- ✅ IsSupported returns false for unregistered extensions
- ✅ ExtractTextAsync delegates to correct extractor (.txt → PlainTextExtractor)
- ✅ ExtractTextAsync delegates to correct extractor (.docx → DocxTextExtractor)
- ✅ ExtractTextAsync delegates to correct extractor (.pdf → PdfTextExtractor)
- ✅ ExtractTextAsync throws for unsupported extension
- ✅ Multiple extractors for same extension (first wins)
- ✅ Extension mapping is case-insensitive

### 2. MimeTypeHelper Tests

#### 2.1 GetMimeType Tests
- ✅ Returns correct MIME for .docx
- ✅ Returns correct MIME for .pdf
- ✅ Returns correct MIME for .txt, .md, .csv
- ✅ Returns correct MIME for .json, .xml, .yaml
- ✅ Returns correct MIME for code files (.sql, .sh, .cs, .py)
- ✅ Returns default for unknown extension
- ✅ Handles extension with leading dot
- ✅ Handles extension without leading dot
- ✅ Case-insensitive matching (.PDF == .pdf)
- ✅ Handles null input (should throw)

### 3. TextChunker Tests

#### 3.1 Chunking Logic Tests
- ✅ Chunk text at exact size boundary
- ✅ Chunk text smaller than chunk size (single chunk)
- ✅ Chunk text with overlap
- ✅ Respect sentence boundaries (don't split mid-sentence)
- ✅ Handle empty text (return empty list)
- ✅ Handle null text (should throw)
- ✅ Handle very long sentences (longer than chunk size)
- ✅ Chunk preserves paragraph structure
- ✅ Overlap calculation is correct
- ✅ Edge case: chunk size = overlap size

### 4. OpenAiEmbeddingsClient Tests

#### 4.1 Embedding Generation Tests (Mocked HTTP)
- ✅ Generate embedding for single text
- ✅ Generate embeddings for batch of texts
- ✅ Handle API errors (retry logic)
- ✅ Handle rate limiting (429 responses)
- ✅ Verify correct model is used
- ✅ Verify API key is sent in headers
- ✅ Handle null/empty input
- ✅ Batch size limits respected
- ✅ Cancellation token honored

### 5. VectorRepository Tests

#### 5.1 Database Operations Tests (Mocked Npgsql)
- ✅ UpsertChunksAsync inserts new chunks
- ✅ UpsertChunksAsync updates existing chunks (same doc_id + chunk_num)
- ✅ UpsertProviderAsync inserts new provider
- ✅ UpsertProviderAsync updates existing provider
- ✅ UpsertDocumentAsync inserts new document
- ✅ UpsertDocumentAsync updates ETag on existing document
- ✅ CleanupOrphanedDocumentsAsync deletes correct documents
- ✅ DeleteAllProviderDocumentsAsync deletes only specified provider
- ✅ SearchSimilarAsync returns correct results
- ✅ SearchSimilarAsync filters by provider
- ✅ SearchSimilarAsync limits results correctly
- ✅ Handle null embedding vector (should throw)
- ✅ Handle connection errors gracefully

### 6. Provider Tests (Unit - Mocked Dependencies)

#### 6.1 LocalProvider Tests
- ✅ ListDocumentsAsync finds files with correct extensions
- ✅ ListDocumentsAsync skips unsupported extensions
- ✅ ListDocumentsAsync generates stable document IDs
- ✅ ListDocumentsAsync generates correct ETags
- ✅ DownloadDocumentAsync returns file stream
- ✅ DownloadDocumentAsync handles missing file (throws)
- ✅ GetMetadataAsync returns correct metadata
- ✅ Handles non-existent root path (throws on construction)
- ✅ Recursive directory traversal works
- ✅ Respects FileExtensions configuration

#### 6.2 S3Provider Tests (Mocked AWS SDK)
- ✅ ListDocumentsAsync paginates correctly
- ✅ ListDocumentsAsync filters by prefix
- ✅ ListDocumentsAsync filters by extensions
- ✅ ListDocumentsAsync skips folders (keys ending in /)
- ✅ DownloadDocumentAsync returns S3 stream
- ✅ Handles S3 errors (bucket not found, access denied)
- ✅ UseInstanceProfile mode uses correct credentials
- ✅ AccessKey mode uses correct credentials
- ✅ SessionToken credentials work
- ✅ ETag trimming works (removes quotes)

#### 6.3 OneDriveProvider Tests (Mocked Graph API)
- ✅ ListDocumentsAsync for personal account
- ✅ ListDocumentsAsync for business account with DriveId
- ✅ ListDocumentsAsync for business account with SiteUrl
- ✅ Filters by extensions correctly
- ✅ DownloadDocumentAsync for personal account
- ✅ DownloadDocumentAsync for business account
- ✅ Handles authentication errors
- ✅ Handles Graph API errors (item not found, throttling)

### 7. MultiProviderIndexerService Tests

#### 7.1 Orchestration Tests (Mocked Providers & Services)
- ✅ ProcessAllProvidersAsync processes all enabled providers
- ✅ Skips disabled providers
- ✅ Calls cleanup when CleanupOrphanedDocuments = true
- ✅ Skips cleanup when CleanupOrphanedDocuments = false
- ✅ Force reindex deletes all documents first
- ✅ Skips documents with unchanged ETags
- ✅ Processes documents with new/changed ETags
- ✅ Handles provider errors gracefully (continues to next)
- ✅ Chunks text correctly
- ✅ Generates embeddings for each chunk
- ✅ Upserts chunks to database
- ✅ Skips unsupported file formats
- ✅ Logs progress correctly
- ✅ Cancellation token propagates through pipeline

---

## Integration Tests Plan

### Environment Variables for Tests
```bash
# Required for integration tests
TEST_POSTGRES_ENABLED=true

# Optional - only needed for provider integration tests
TEST_ONEDRIVE_ENABLED=true
TEST_ONEDRIVE_CLIENT_ID=xxx
TEST_ONEDRIVE_USERNAME=xxx
TEST_ONEDRIVE_PASSWORD=xxx

TEST_S3_ENABLED=true
TEST_S3_ACCESS_KEY=xxx
TEST_S3_SECRET_KEY=xxx
TEST_S3_BUCKET=test-bucket
TEST_S3_REGION=us-east-1

TEST_OPENAI_ENABLED=true
TEST_OPENAI_API_KEY=xxx
```

### 1. Database Integration Tests

#### 1.1 VectorRepository Integration Tests
- ✅ **Setup**: Start PostgreSQL container on port 5433, run migrations
- ✅ **Teardown**: Stop and remove container
- ✅ Insert and retrieve chunks with pgvector
- ✅ Vector similarity search returns correct results
- ✅ Upsert updates existing records
- ✅ Cleanup orphaned documents works end-to-end
- ✅ Force delete provider documents works
- ✅ Provider filtering in search works
- ✅ Transaction rollback on errors
- ✅ Connection pooling works correctly

### 2. Provider Integration Tests

#### 2.1 LocalProvider Integration Tests
- ✅ **Setup**: Create temp directory with test files
- ✅ **Teardown**: Delete temp directory
- ✅ List and download real files
- ✅ Verify ETags change when files modified
- ✅ Verify ETags stable when files unchanged

#### 2.2 S3Provider Integration Tests (Conditional)
- ✅ **Condition**: TEST_S3_ENABLED=true
- ✅ **Setup**: Use real S3 bucket (or LocalStack container)
- ✅ List documents from S3
- ✅ Download document from S3
- ✅ Pagination works with >1000 objects

#### 2.3 OneDriveProvider Integration Tests (Conditional)
- ✅ **Condition**: TEST_ONEDRIVE_ENABLED=true
- ✅ List documents from OneDrive
- ✅ Download document from OneDrive

### 3. Text Extraction Integration Tests

#### 3.1 Real File Extraction Tests
- ✅ **Setup**: Create test files (.txt, .docx, .pdf)
- ✅ Extract from real .txt file
- ✅ Extract from real .docx file
- ✅ Extract from real .pdf file
- ✅ Verify extracted text accuracy

### 4. End-to-End Integration Tests

#### 4.1 Full Pipeline Tests (Conditional - requires OpenAI)
- ✅ **Condition**: TEST_OPENAI_ENABLED=true
- ✅ **Setup**: PostgreSQL container + temp files + OpenAI API
- ✅ Index documents from LocalProvider
- ✅ Search indexed documents
- ✅ Verify search results are relevant
- ✅ Reindex with unchanged files (should skip)
- ✅ Reindex with changed files (should update)
- ✅ Delete provider documents and verify cleanup

---

## Test Project Structure

```
Indexer.Tests/
├── Unit/
│   ├── TextExtraction/
│   │   ├── PlainTextExtractorTests.cs
│   │   ├── DocxTextExtractorTests.cs
│   │   ├── PdfTextExtractorTests.cs
│   │   └── TextExtractionServiceTests.cs
│   ├── Providers/
│   │   ├── MimeTypeHelperTests.cs
│   │   ├── LocalProviderTests.cs
│   │   ├── S3ProviderTests.cs
│   │   └── OneDriveProviderTests.cs
│   ├── Services/
│   │   ├── TextChunkerTests.cs
│   │   ├── OpenAiEmbeddingsClientTests.cs
│   │   ├── VectorRepositoryTests.cs
│   │   └── MultiProviderIndexerServiceTests.cs
├── Integration/
│   ├── Database/
│   │   └── VectorRepositoryIntegrationTests.cs
│   ├── Providers/
│   │   ├── LocalProviderIntegrationTests.cs
│   │   ├── S3ProviderIntegrationTests.cs (conditional)
│   │   └── OneDriveProviderIntegrationTests.cs (conditional)
│   ├── TextExtraction/
│   │   └── RealFileExtractionTests.cs
│   └── EndToEnd/
│       └── FullPipelineTests.cs (conditional)
├── TestHelpers/
│   ├── PostgresTestContainer.cs
│   ├── TestFileGenerator.cs
│   ├── MockProviderFactory.cs
│   └── TestConfiguration.cs
└── TestData/
    ├── sample.txt
    ├── sample.docx
    ├── sample.pdf
    └── sample.md
```

---

## Testing Tools & Libraries

### Required NuGet Packages
```xml
<PackageReference Include="xunit" Version="2.9.3" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
<PackageReference Include="Moq" Version="4.20.72" />
<PackageReference Include="FluentAssertions" Version="7.0.0" />
<PackageReference Include="Testcontainers" Version="3.12.0" />
<PackageReference Include="Testcontainers.PostgreSql" Version="3.12.0" />
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.9" />
<PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="9.0.9" />
```

---

## Test Execution Strategy

### Phase 1: Unit Tests (Fast, Always Run)
```bash
dotnet test --filter Category=Unit
```
- No external dependencies
- Run in CI/CD pipeline on every commit
- Should complete in < 10 seconds

### Phase 2: Database Integration Tests
```bash
dotnet test --filter Category=DatabaseIntegration
```
- Uses PostgreSQL test container
- Run in CI/CD pipeline
- Should complete in < 30 seconds

### Phase 3: Provider Integration Tests (Conditional)
```bash
dotnet test --filter Category=ProviderIntegration
```
- Requires environment variables
- Run locally with credentials
- Optional in CI/CD (or use test accounts)

### Phase 4: End-to-End Tests (Conditional)
```bash
dotnet test --filter Category=E2E
```
- Requires all services (DB + OpenAI API)
- Run manually or nightly
- Should complete in < 2 minutes

---

## Success Criteria

- ✅ Unit tests: **>90% code coverage** on business logic
- ✅ All unit tests pass
- ✅ Database integration tests pass with clean container
- ✅ LocalProvider integration tests pass
- ✅ All tests run in **< 3 minutes total**
- ✅ No resource leaks (connections, streams, containers cleaned up)
- ✅ Tests are deterministic (no flaky tests)
- ✅ Clear test failure messages

---

## Implementation Order

1. Setup test project structure
2. Implement MimeTypeHelper unit tests (simplest)
3. Implement TextExtraction unit tests
4. Implement TextChunker unit tests
5. Implement Provider unit tests (mocked)
6. Implement VectorRepository unit tests (mocked)
7. Implement MultiProviderIndexerService unit tests (mocked)
8. Setup PostgreSQL test container helper
9. Implement database integration tests
10. Implement LocalProvider integration tests
11. Implement real file extraction tests
12. Implement conditional provider integration tests
13. Implement end-to-end tests

---

**Total Estimated Tests: ~120+ test cases**
