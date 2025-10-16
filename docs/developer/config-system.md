# Configuration System

Centrally environment-driven with fallback to `appsettings.json` where applicable.

## Precedence
1. Explicit environment variable
2. JSON configuration (`appsettings.json`, `appsettings.Development.json`)
3. Embedded defaults in options classes

## Binding
- API: `builder.Services.Configure<DbOptions>(...)`, similar for `SearchOptions`, `AdminAuthOptions`.
- Indexer: Options bound during host startup (see `Program.cs`).

## Validation
- Fail-fast: absence of DB connection string or admin secret throws at startup.
- Token lifetime validated as positive integer.

## Options Overview
| Options | Purpose | Key Fields |
|--------|---------|------------|
| `DbOptions` | Connection string | `ConnectionString` |
| `ChunkingOptions` | Chunk & indexing flags | `ChunkSize`, `Overlap`, `MaxFiles`, `ForceFullReindex` |
| `OpenAiOptions` | Embedding/completion config | `ApiKey`, `BaseUrl`, `EmbedModel` |
| `SearchOptions` | Query defaults | `DefaultTopK`, `MaxTopK` |
| `AdminAuthOptions` | Admin auth TTL & secret | `Secret`, `TokenLifetimeMinutes` |

## Adding a New Option
1. Create POCO with properties
2. Register `builder.Services.Configure<NewOptions>(...)`
3. Provide environment variable binding logic (manual if transformation needed)
4. Add validation (throw or use `IValidateOptions<T>`)
5. Document variables in config guide

## Dynamic Reload
Currently environment-driven only at process start (except seeded provider settings). Consider future file watcher or endpoint.

## Secrets Handling
- Expect container orchestration (K8s secrets / Docker env) to supply sensitive values.
- Avoid logging raw secrets (never log full connection string with password).

## Future Enhancements
- Central configuration table with versioning
- Hot reload of AI provider settings

## Next
- Coding standards: [Coding Standards](coding-standards.md)
