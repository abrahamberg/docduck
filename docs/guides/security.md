# Security & Privacy

## Surface Overview
| Component | Concern |
|-----------|--------|
| Providers | External credentials (AAD, AWS) |
| Indexer | Reads potentially sensitive docs |
| DB | Stores chunk text + embeddings |
| API | Exposes semantic content |

## Secrets
- Provide via environment / secret manager
- Never commit secrets (`.env.local` excluded)
- Rotate keys periodically

## Least Privilege
| Provider | Recommendation |
|----------|---------------|
| OneDrive | Restrict to required app scopes & drive/folder |
| S3 | Limit IAM to list/get on specific bucket/prefix |
| Local | Run under user with minimal FS access |

## Data Stored
- Raw text chunks (plain text)
- Embeddings (vector floats)
- Metadata (filenames, provider names, relative paths)

If storing sensitive documents, secure database at rest (disk encryption) and network (TLS, host-based firewalls).

## Network Exposure
- Keep API behind reverse proxy
- Consider adding auth middleware before public deployment (roadmap)

## Admin Secret
- `ADMIN_AUTH_SECRET` secures admin endpoints (future expansions)

## Logging Hygiene
- Filenames appear in logs
- Avoid logging full content or secrets

## Threat Considerations
| Threat | Mitigation |
|--------|-----------|
| Credential leak | Use secret manager + rotate |
| Unauthorized query | Add external auth/proxy layer |
| Data exfil via embeddings | Treat embeddings as sensitive derivative |

## Future Enhancements
- Pluggable auth (JWT/OIDC)
- Provider-level ACL filtering in search
- Redaction / PII scrubbing pre-embedding

## Next
- Configuration: [Configuration](configuration.md)
