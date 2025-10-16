# Installation

Choose the path matching your environment maturity. Start with Docker Compose for evaluation.

## Options

| Scenario | Recommended Path |
|----------|------------------|
| Local evaluation | Docker Compose (API + Indexer + Postgres) |
| Existing Postgres, manual control | Run binaries / `dotnet run` |
| Production / scheduled indexing | Kubernetes (CronJob + Deployment) |

## 1. Docker Compose (Example)

(Provide a compose file — placeholder snippet below; adapt to your final stack.)

```yaml
version: '3.9'
services:
  db:
    image: pgvector/pgvector:pg16
    environment:
      POSTGRES_PASSWORD: postgres
      POSTGRES_DB: docduck
    ports: ["5432:5432"]
  api:
    build: ./Api
    environment:
      DB_CONNECTION_STRING: Host=db;Database=docduck;Username=postgres;Password=postgres
      OPENAI_API_KEY: ${OPENAI_API_KEY}
    depends_on: [db]
    ports: ["5000:5000"]
  indexer:
    build: ./Indexer
    environment:
      DB_CONNECTION_STRING: Host=db;Database=docduck;Username=postgres;Password=postgres
      OPENAI_API_KEY: ${OPENAI_API_KEY}
      PROVIDER_LOCAL_ENABLED: "true"
      PROVIDER_LOCAL_NAME: localdocs
      PROVIDER_LOCAL_ROOT_PATH: /data/docs
    volumes:
      - ./sample-docs:/data/docs:ro
    depends_on: [db]
```

Then:
```bash
docker compose up --build
```

Run indexer on demand:
```bash
docker compose run --rm indexer
```

## 2. Manual (.NET SDK)

```bash
# Restore & build
 dotnet build
# Run indexer
 (cd Indexer && DB_CONNECTION_STRING=... OPENAI_API_KEY=... dotnet run)
# Run API
 (cd Api && DB_CONNECTION_STRING=... OPENAI_API_KEY=... dotnet run)
```

## 3. Kubernetes

- Indexer as CronJob (every N hours)
- API as Deployment + Service + optional Ingress

See example CronJob in [Indexer Operation](indexer.md).

## Post-Install Checklist

- [ ] `/health` returns `status=healthy`
- [ ] `/providers` lists at least one enabled provider
- [ ] `/query` returns an answer with sources

## Upgrading

1. Stop indexer runs
2. Deploy new containers
3. If schema changes (rare): apply new SQL migrations (future: migration tooling)
4. Run indexer once with `FORCE_FULL_REINDEX=true` if embedding model dimensions changed

## Uninstall

- Drop database (if dedicated)
- Remove environment secrets
- Delete container images
