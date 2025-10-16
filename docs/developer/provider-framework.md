# Provider Framework

Implement new content sources by conforming to `IDocumentProvider`.

## Interface
```csharp
public interface IDocumentProvider {
  string ProviderType { get; }
  string ProviderName { get; }
  bool IsEnabled { get; }
  Task<IReadOnlyList<ProviderDocument>> ListDocumentsAsync(CancellationToken ct);
  Task<Stream> DownloadDocumentAsync(string documentId, CancellationToken ct);
  Task<ProviderMetadata> GetMetadataAsync(CancellationToken ct = default);
}
```

## Minimal Implementation Outline
1. Parse configuration (env vars)
2. Provide stable `ProviderType` (e.g. "gitlab")
3. Provide user-friendly `ProviderName` (e.g. "engineering-wiki")
4. `ListDocumentsAsync` returns `ProviderDocument` entries with:
   - `DocumentId` (stable)
   - `Filename`
   - `RelativePath` (optional)
   - `ETag` (hash / version token)
5. `DownloadDocumentAsync` streams file content
6. Optional: `GetMetadataAsync` enriches provider registry

## ETag Strategies
| Source | Strategy |
|--------|----------|
| APIs with ETag | Use native header |
| Filesystem | Hash of (mtime + size) or content hash |
| Object storage | ETag field from listing |

## Registration
- Add DI registration in `Program.cs` or provider catalog module
- Add enable toggle env var: `PROVIDER_<TYPE>_ENABLED`

## Testing a Provider
- Unit test `ListDocumentsAsync` against fixture
- Integration test indexing 1–2 files

## Pitfalls
- Non-stable IDs cause orphan churn
- Excessively large downloads (consider size guard)
- Missing or weak ETag ⇒ unnecessary reprocessing

## Adding Configuration Docs
Update provider docs under `docs/providers/` and reference new env vars.

## Example Skeleton (Pseudo)
```csharp
public sealed class GitLabProvider : IDocumentProvider {
  ... // ctor with options
  public string ProviderType => "gitlab";
  public string ProviderName => _options.InstanceName;
  public bool IsEnabled => _options.Enabled;
  public async Task<IReadOnlyList<ProviderDocument>> ListDocumentsAsync(...) { /* call API */ }
  public Task<Stream> DownloadDocumentAsync(string id, ...) { /* fetch raw */ }
  public Task<ProviderMetadata> GetMetadataAsync(...) => Task.FromResult(new ProviderMetadata(...));
}
```

## Next
- Text extraction internals: [Text Extraction](text-extraction.md)
