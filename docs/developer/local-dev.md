# Local Development

## Prerequisites
- .NET 8 SDK
- PostgreSQL + pgvector
- OpenAI API key (or compatible endpoint)

## Setup
```bash
git clone <repo>
cd docduck
dotnet restore
```

Initialize DB:
```bash
psql -h localhost -U postgres -c "CREATE DATABASE docduck;" || true
psql -h localhost -U postgres -d docduck -c "CREATE EXTENSION IF NOT EXISTS vector;"
psql -h localhost -U postgres -d docduck -f sql/01-init-schema.sql
```

Sample env (dev shell export or `.env.local`):
```
OPENAI_API_KEY=sk-...
DB_CONNECTION_STRING=Host=localhost;Database=docduck;Username=postgres;Password=postgres;MinPoolSize=1;MaxPoolSize=5
PROVIDER_LOCAL_ENABLED=true
PROVIDER_LOCAL_NAME=localdocs
PROVIDER_LOCAL_ROOT_PATH=./sample-docs
```

Create a test file:
```bash
mkdir -p sample-docs && echo "DocDuck local dev" > sample-docs/test.txt
```

Run indexer:
```bash
cd Indexer && dotnet run
```
Run API:
```bash
cd Api && dotnet run
```

Query:
```bash
curl -X POST http://localhost:5000/query -H 'Content-Type: application/json' -d '{"question":"What is DocDuck?"}'
```

## Iterating Code
- Modify code, rerun the specific project (no full solution rebuild needed)
- For containerized dev, rebuild only the changed service image

## Debugging
- Use `dotnet watch run` (optional) for API rapid iteration
- Add temporary structured logs at `LogDebug` level

## Tests
```bash
cd Indexer.Tests
dotnet test
```
(Extend tests for new providers/extractors.)

## Common Issues
| Symptom | Fix |
|---------|-----|
| Cannot load pgvector | Extension not installed | Run `CREATE EXTENSION vector;` |
| 500 on query | Missing OpenAI key | Export `OPENAI_API_KEY` |
| No docs indexed | Wrong provider path | Verify `ls -R` path correctness |

## Next
- Testing guidance: [Testing](testing.md)
