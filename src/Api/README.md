# DocDuck Query API

ASP.NET Core minimal API that provides RAG (Retrieval-Augmented Generation) endpoints for querying indexed documents using vector similarity search and OpenAI.

## Features

- **Vector Similarity Search**: Uses pgvector for fast kNN search with cosine distance
- **RAG Pipeline**: Embed → Search → Generate with citations
- **Dual Endpoints**: 
  - `/query` - Simple question answering
  - `/chat` - Conversational with history support
- **Memory Efficient**: Optimized for ≤512 MiB runtime footprint
- **Production Ready**: Health checks, structured logging, proper error handling

## Architecture

```
Request → Embed Question → kNN Search (pgvector) → Build Context → 
OpenAI Generation → Response with Citations
```

### Components

1. **OpenAiClient** - Handles embeddings and chat completion
2. **VectorSearchService** - pgvector similarity search
3. **Program.cs** - Minimal API endpoints

## Prerequisites

- .NET 8 SDK
- PostgreSQL with pgvector extension and indexed documents
- OpenAI API key

## Configuration

All configuration via environment variables:

### Required

```bash
# Database
DB_CONNECTION_STRING="Host=localhost;Database=vectors;Username=postgres;Password=password;MinPoolSize=1;MaxPoolSize=5"

# OpenAI
OPENAI_API_KEY="sk-..."
```

### Optional

```bash
# OpenAI Configuration
OPENAI_BASE_URL="https://api.openai.com/v1"  # Or Azure OpenAI endpoint
OPENAI_EMBED_MODEL="text-embedding-3-small"
OPENAI_CHAT_MODEL="gpt-4o-mini"
OPENAI_MAX_TOKENS="1000"
OPENAI_TEMPERATURE="0.7"

# Search Configuration
DEFAULT_TOP_K="8"   # Default number of chunks to retrieve
MAX_TOP_K="20"      # Maximum allowed TopK value
```

## Running Locally

### 1. Set environment variables

```bash
export DB_CONNECTION_STRING="Host=localhost;Database=vectors;Username=postgres;Password=password"
export OPENAI_API_KEY="sk-..."
```

### 2. Run the API

```bash
cd Api
dotnet run
```

The API will start on `http://localhost:5000` (or configured port).

### 3. Test endpoints

```bash
# Health check
curl http://localhost:5000/health

# Query
curl -X POST http://localhost:5000/query \
  -H "Content-Type: application/json" \
  -d '{"question": "What is the project about?"}'

# Chat
curl -X POST http://localhost:5000/chat \
  -H "Content-Type: application/json" \
  -d '{
    "message": "Tell me about the requirements",
    "history": []
  }'
```

## API Endpoints

### GET /health

Health check with database statistics.

**Response:**
```json
{
  "status": "healthy",
  "timestamp": "2025-10-11T10:30:00Z",
  "chunks": 1234,
  "documents": 42
}
```

### POST /query

Simple question answering without conversation history.

**Request:**
```json
{
  "question": "What are the main features?",
  "topK": 8  // Optional, defaults to DEFAULT_TOP_K
}
```

**Response:**
```json
{
  "answer": "The main features include...",
  "sources": [
    {
      "docId": "abc123",
      "filename": "requirements.docx",
      "chunkNum": 5,
      "text": "Chunk text content...",
      "distance": 0.234,
      "citation": "[requirements.docx#chunk5]"
    }
  ],
  "tokensUsed": 456
}
```

### POST /chat

Conversational chat with history support.

**Request:**
```json
{
  "message": "Can you elaborate on that?",
  "history": [
    {
      "role": "user",
      "content": "What are the requirements?"
    },
    {
      "role": "assistant",
      "content": "The requirements are..."
    }
  ],
  "topK": 8  // Optional
}
```

**Response:**
```json
{
  "answer": "Sure! The requirements elaborate...",
  "sources": [...],
  "tokensUsed": 523,
  "history": [
    // Previous messages plus new exchange
  ]
}
```

## Performance Tuning

### Memory Optimization

The API is optimized for low memory footprint:

```bash
# Database connection pool
DB_CONNECTION_STRING="...;MinPoolSize=1;MaxPoolSize=5"

# Kubernetes resource limits
resources:
  requests:
    memory: 256Mi
    cpu: 250m
  limits:
    memory: 512Mi
    cpu: 500m
```

### Search Tuning

- **TopK**: Start with 8, increase if results lack context
- **Temperature**: 0.7 for balanced creativity, lower (0.3) for factual
- **MaxTokens**: 1000 default, adjust based on answer length needs

### pgvector Index

Ensure vector index is optimized:

```sql
-- Check index usage
SELECT idx_scan FROM pg_stat_user_indexes 
WHERE indexrelname = 'docs_chunks_embedding_idx';

-- Adjust probes for accuracy
SET ivfflat.probes = 10;  -- Default, good balance
```

## Docker Deployment

### Dockerfile

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Api/Api.csproj Api/
RUN dotnet restore Api/Api.csproj
COPY src/Api/ Api/
RUN dotnet publish Api/Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Api.dll"]
```

### Build and run

```bash
# Build
docker build -t docduck-api -f src/Api/Dockerfile .

# Run
docker run -p 8080:8080 \
  -e DB_CONNECTION_STRING="Host=postgres;..." \
  -e OPENAI_API_KEY="sk-..." \
  docduck-api
```

## Kubernetes Deployment

See [k8s/api-deployment.yaml](../k8s/api-deployment.yaml) for complete manifest.

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: docduck-api
spec:
  replicas: 1
  template:
    spec:
      containers:
      - name: api
        image: your-registry/docduck-api:latest
        resources:
          requests:
            memory: 256Mi
            cpu: 250m
          limits:
            memory: 512Mi
            cpu: 500m
        env:
        - name: DB_CONNECTION_STRING
          valueFrom:
            secretKeyRef:
              name: docduck-secrets
              key: db-connection-string
        - name: OPENAI_API_KEY
          valueFrom:
            secretKeyRef:
              name: docduck-secrets
              key: openai-api-key
        readinessProbe:
          httpGet:
            path: /health
            port: 8080
          initialDelaySeconds: 5
          periodSeconds: 10
```

## Monitoring

### Logs

Structured JSON logging to stdout:

```bash
# View logs
kubectl logs -f deployment/docduck-api

# Grep for errors
kubectl logs deployment/docduck-api | grep ERROR
```

### Metrics

Monitor via health endpoint:

```bash
# Check health
curl http://api-service/health

# Response includes chunk/document counts
{
  "status": "healthy",
  "chunks": 1234,
  "documents": 42
}
```

## Troubleshooting

### "OpenAI API Key: Missing"

Set the environment variable:
```bash
export OPENAI_API_KEY="sk-..."
```

### "DB Connection: Missing"

Set the connection string:
```bash
export DB_CONNECTION_STRING="Host=localhost;..."
```

### Slow Queries

1. Check pgvector index: `\d docs_chunks`
2. Verify index usage: `EXPLAIN SELECT ... ORDER BY embedding <=> ...`
3. Adjust probes: `SET ivfflat.probes = 20`
4. Reduce TopK if too many chunks

### Out of Memory

1. Reduce `MaxPoolSize` in connection string
2. Lower `DEFAULT_TOP_K` value
3. Decrease `OPENAI_MAX_TOKENS`
4. Check Kubernetes limits

### Poor Answer Quality

1. Increase `DEFAULT_TOP_K` for more context
2. Check chunk relevance (review sources in response)
3. Adjust `OPENAI_TEMPERATURE` (lower for factual)
4. Re-index with better chunking strategy

## Development

### Project Structure

```
src/Api/
├── Program.cs              # Main API endpoints and DI setup
├── Models/
│   └── QueryModels.cs      # Request/response models
├── Options/
│   ├── DbOptions.cs        # Database configuration
│   ├── OpenAiOptions.cs    # OpenAI configuration
│   └── SearchOptions.cs    # Search configuration
└── Services/
  ├── OpenAiClient.cs     # OpenAI API client
  └── VectorSearchService.cs  # pgvector search
```

### Adding New Endpoints

```csharp
app.MapPost("/your-endpoint", async (
    YourRequest request,
    OpenAiClient openAiClient,
    VectorSearchService searchService) =>
{
    // Your logic here
    return Results.Ok(response);
});
```

## Security

- **API Keys**: Never commit keys, use environment variables
- **CORS**: Configured for development, restrict in production
- **Rate Limiting**: Consider adding middleware for production
- **Input Validation**: Basic validation included, enhance as needed

## Testing

### Manual Testing

Use `curl` or Postman/Insomnia:

```bash
# Simple query
curl -X POST http://localhost:5000/query \
  -H "Content-Type: application/json" \
  -d '{"question": "test question"}'

# With TopK override
curl -X POST http://localhost:5000/query \
  -H "Content-Type: application/json" \
  -d '{"question": "test question", "topK": 5}'
```

### Integration Testing

Ensure database has indexed documents:

```bash
# Check document count
./db-util.sh stats

# If empty, run indexer first
cd Indexer && dotnet run
```

## Performance Benchmarks

Target performance on 512 MiB container:

- Cold start: < 2 seconds
- /health: < 50ms
- /query (8 chunks): < 2 seconds
- /chat (with history): < 3 seconds
- Memory usage: 200-400 MiB
- Concurrent requests: 10-20

## License

MIT

## See Also

- [Main README](../README.md) - Overall project documentation
 - [pgvector docs](../docs/database/pgvector.md) - Database implementation details
- [Indexer README](../Indexer/README.md) - Indexing pipeline
