# Adding a Provider

This guide walks through adding a new document source provider to DocDuck. It complements the high-level concepts in `developer/provider-framework.md`.

## Overview
Providers are responsible for:
- Enumerating documents (list or discover items)
- Fetching raw content (bytes or text) for ingestion
- Providing minimal metadata (id, name, modified timestamp, content type, size)

The indexer uses providers to build normalized ingestion events which are then embedded and made searchable.

## When to Create a Provider
Create a new provider when you need to ingest documents from a system not already supported (e.g., SharePoint, Google Drive, custom S3 layout). If you only need to transform existing local files, prefer enhancing the existing `Local` provider.

## Core Steps
1. Define provider options (credentials, root path, filters) in a strongly typed options class.
2. Implement the provider contract (enumeration + content retrieval).
3. Register the provider with DI and configuration binding.
4. Add any necessary secret or auth setup to local dev docs.
5. Test with a minimal dataset.

## 1. Define Options
Create a record in `Providers.Shared/Options` (or appropriate folder):
```csharp
public sealed record MyProviderOptions
{
    public required string ApiBaseUrl { get; init; }
    public string? FolderFilter { get; init; }
}
```
Bind via configuration (environment variables or YAML) using the naming convention: `Providers:MyProvider:*`.

## 2. Implement Provider
Providers typically expose two async methods:
- `IAsyncEnumerable<ProviderDocument> ListDocumentsAsync(...)`
- `Task<ProviderContent> FetchContentAsync(string id, CancellationToken ct)`

Keep implementation focused; isolate API client logic into a small helper class if it grows.

## 3. Register with DI
In the provider registration section add:
```csharp
services.Configure<MyProviderOptions>(config.GetSection("Providers:MyProvider"));
services.AddSingleton<IMyProvider, MyProvider>();
```
Only introduce an interface if you foresee multiple implementations or need for mocking in tests.

## 4. Configuration & Secrets
Document required env vars in `docs/guides/configuration.md` and any auth steps in `docs/guides/installation.md`. Never hardcode secrets; rely on environment or secret managers.

## 5. Testing
Add unit tests covering:
- Empty enumeration (no documents)
- Basic enumeration (returns expected ids)
- Fetch content (returns non-empty stream or text)

Consider an integration test with a small fixture dataset if the provider interacts with external services.

## Edge Cases
Handle:
- Network timeouts (use CancellationToken)
- Rate limiting (simple retry/backoff if needed)
- Missing/404 documents (return a not found result or throw a clear exception)

## Logging
Use structured logging with provider name and document id for traceability:
```csharp
_logger.LogInformation("{Provider} fetched document {DocumentId}", "MyProvider", doc.Id);
```

## Performance Considerations
Start simple. Defer optimization (batching, concurrency) until profiling shows a need. Ensure streams are disposed properly.

## Next Steps
After adding your provider:
- Run the indexer locally to ingest a sample
- Verify embeddings are generated and searchable
- Open a PR including this guide update if provider is generic

For deeper architectural details see `developer/provider-framework.md`.
