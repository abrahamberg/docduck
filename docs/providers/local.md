# Local Files Provider

Index documents from a local directory tree (recursive). Ideal for development and small self-host use cases.

## Environment Variables
```
PROVIDER_LOCAL_ENABLED=true
PROVIDER_LOCAL_NAME=localdocs
PROVIDER_LOCAL_ROOT_PATH=/absolute/or/relative/path
```

## Behavior
- Uses relative path as document ID
- ETag may be derived from file timestamp/length (implementation specific)
- Skips unchanged files between runs

## Include / Exclude
Future enhancement: glob filters. For now place only relevant files or pre-filter externally.

## Supported Extensions
See: `TextExtractionService.GetSupportedExtensions()` at runtime; common: `.txt`, `.md`, `.docx`, `.pdf`*, `.odt`, `.rtf`.

`*` PDF requires optional extractor dependency.

## Tips
| Scenario | Suggestion |
|----------|------------|
| Large repository | Run first with `MAX_FILES=100` to verify |
| Force refresh | Set `FORCE_FULL_REINDEX=true` |

## Troubleshooting
- Path not found: ensure container mounts host directory (Docker volume)
- No text: binary files without extractor support are skipped

## Next
- All providers: [Providers Overview](index.md)
