# Troubleshooting

## Indexer Issues
| Symptom | Cause | Fix |
|---------|-------|-----|
| "No documents found" | Wrong folder/prefix | Verify provider path vars |
| Skips all files | ETag unchanged | Force reindex or modify document |
| Crash early | Missing DB string | Set `DB_CONNECTION_STRING` |
| High cost | Too small chunks / high overlap | Tune `CHUNK_SIZE` / `CHUNK_OVERLAP` |

## Provider Specific
| Symptom | Provider | Fix |
|---------|----------|-----|
| 401 Unauthorized | OneDrive | Check AAD app creds & scopes |
| AccessDenied | S3 | IAM policy missing list/get |
| Empty index | Local | Wrong root path or unsupported extensions |

## OpenAI / Embeddings
| Symptom | Cause | Fix |
|---------|-------|-----|
| 401 / 403 | Invalid key or base URL | Re-export key; verify `OPENAI_BASE_URL` |
| Slow embedding | Rate limit / network | Lower batch size, retry later |

## Query API
| Symptom | Cause | Fix |
|---------|-------|-----|
| 500 error | Missing OpenAI key | Provide `OPENAI_API_KEY` |
| Empty answers | No relevant chunks | Confirm indexing; broaden query |
| High latency | Large top-K or slow model | Reduce `topK`, optimize prompt |

## Database
| Symptom | Cause | Fix |
|---------|-------|-----|
| Extension error | pgvector not installed | `CREATE EXTENSION vector;` |
| Poor recall | Index lists too low | Recreate vector index with higher lists |

## General Techniques
- Increase log verbosity (temporarily adjust level to Debug)
- Run minimal provider only to isolate
- Validate environment variables

## Next
- FAQ: [FAQ](faq.md)
