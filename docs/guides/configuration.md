# Configuration

DocDuck is configured primarily via environment variables. Some values may also be supplied in `appsettings.json`, but environment variables take precedence.

## Core
| Variable | Description | Required |
|----------|-------------|----------|
| `DB_CONNECTION_STRING` | PostgreSQL connection string incl. pooling settings | Yes |
| `OPENAI_API_KEY` | OpenAI (or Azure OpenAI compatible) API key | Yes* |
| `OPENAI_BASE_URL` | Override base URL (Azure / proxy) | No |

`*` Only required for indexing embeddings and answering queries.

## Chunking & Indexer Behavior
| Variable | Purpose | Default |
|----------|---------|---------|
| `CHUNK_SIZE` | Approx chars per chunk | 1000 |
| `CHUNK_OVERLAP` | Overlap between chunks | 200 |
| `MAX_FILES` | Limit processed files (debug) | unset |
| `FORCE_FULL_REINDEX` | If `true`, deletes provider data before indexing | false |
| `CLEANUP_ORPHANS` | If `true`, remove missing docs (may map internally to option) | true |
| `EMBED_BATCH_SIZE` | Embedding request batch size | 16 |

## Search API
| Variable | Purpose | Default |
|----------|---------|---------|
| `DEFAULT_TOP_K` | Default result count | 8 |
| `MAX_TOP_K` | Upper limit on requested results | 32 |

## Admin/Auth
| Variable | Purpose | Required |
|----------|---------|----------|
| `ADMIN_AUTH_SECRET` | Secret used to sign admin tokens | Yes |
| `ADMIN_TOKEN_LIFETIME_MINUTES` | Token TTL | 60 |

## Provider Enabling (Pattern)
Each provider follows an `PROVIDER_<TYPE>_` prefix convention for toggling and configuration.

Example (Local):
```
PROVIDER_LOCAL_ENABLED=true
PROVIDER_LOCAL_NAME=localdocs
PROVIDER_LOCAL_ROOT_PATH=/data/docs
```

Example (OneDrive business - app auth):
```
PROVIDER_ONEDRIVE_ENABLED=true
PROVIDER_ONEDRIVE_NAME=corpdrive
GRAPH_AUTH_MODE=ClientSecret
GRAPH_ACCOUNT_TYPE=business
GRAPH_TENANT_ID=...
GRAPH_CLIENT_ID=...
GRAPH_CLIENT_SECRET=...
GRAPH_DRIVE_ID=...
GRAPH_FOLDER_PATH=/Shared Documents/Docs
```

Example (S3):
```
PROVIDER_S3_ENABLED=true
PROVIDER_S3_NAME=handbook
S3_BUCKET=my-bucket
S3_PREFIX=handbook/
AWS_REGION=us-east-1
AWS_ACCESS_KEY_ID=...
AWS_SECRET_ACCESS_KEY=...
```

## Environment Precedence
1. Explicit environment variables
2. `.env.local` (if sourced before start)
3. `appsettings.*.json`

## Sensitive Values
Store secrets in platform secret manager (Kubernetes Secret, AWS Parameter Store, Azure Key Vault) – never commit them.

## Validation
Startup will fail fast if required values (DB, admin secret) are missing.

## Next
- Providers: see [Providers Overview](../providers/index.md)
- Deep configuration internals: see [Configuration System](../developer/config-system.md)
