# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Added - Query API (ASP.NET Core Minimal API) (2025-10-11)

#### 🔍 Complete RAG API Implementation
- **Minimal API** with lightweight endpoints
  - `GET /health` - Health check with database statistics
  - `POST /query` - Simple question answering
  - `POST /chat` - Conversational chat with history support
  - `GET /` - API information
  
- **RAG Pipeline Components**
  - **OpenAiClient** - Embeddings and chat completion
  - **VectorSearchService** - pgvector similarity search with cosine distance
  - Configurable TopK for context retrieval
  - Source citations with document references
  
- **Production Features**
  - Memory optimized for ≤512 MiB runtime footprint
  - Structured logging
  - Health checks with database metrics
  - Proper error handling and validation
  - CORS support
  
#### 🐳 Deployment
- **Docker Support**
  - Multi-stage Dockerfile with Alpine base
  - Non-root user
  - Optimized image size (~100 MB)
  
- **Kubernetes Manifests**
  - Deployment with resource limits (256-512 MiB)
  - Service (ClusterIP)
  - Ingress with TLS
  - Liveness and readiness probes
  
#### 📚 Documentation
- **docs/reports/api-implementation.md** - Complete feature summary
- **Api/README.md** - Detailed API documentation
  - Endpoint specifications
  - Configuration guide
  - Performance tuning
  - Troubleshooting
  - Testing guide
  
#### 🛠️ Development Tools
- **test-api.sh** - Automated API testing script
  - Tests all endpoints
  - Error handling validation
  - Color-coded output

### Performance Characteristics
- Cold start: < 2 seconds
- /health: < 50ms
- /query: < 2 seconds (with 8 chunks)
- Memory: 200-400 MiB
- Concurrent requests: 10-20

### Added - PostgreSQL + pgvector Complete Implementation (2025-10-11)

#### 🗄️ Database Layer
- **VectorRepository** service with full pgvector integration
  - Insert/upsert chunks with embeddings
  - File tracking with ETag-based delta indexing
  - Idempotent operations on `(doc_id, chunk_num)`
  - Full async/await with CancellationToken support
  
- **Comprehensive SQL Resources**
  - `sql/01-init-schema.sql` - Database initialization script
  - `sql/02-queries.sql` - 20+ analysis and search queries
  - `sql/03-maintenance.sql` - Maintenance procedures and health checks
  
- **Database Management CLI** (`db-util.sh`)
  - Test database connections
  - Initialize schema
  - View statistics and recent documents
  - Run health checks and maintenance
  - Create backups
  - Color-coded output with safety confirmations

#### 📚 Documentation
- **docs/database/pgvector.md** - Complete implementation guide
- **docs/reports/pgvector-implementation.md** - Feature summary

#### 🧪 Testing
- **Indexer.Tests/VectorRepositoryTests.cs** - Integration test suite
  - Insert/upsert operations
  - Duplicate handling
  - Multi-chunk batching
  - Document existence checking
  - File tracking updates
  - Cancellation support
  - Edge case handling

#### 📝 Documentation Updates
- Updated README.md with database management section
- Enhanced database setup instructions
- Added links to new SQL resources
- Documented integration testing

### Performance Improvements
- IVFFlat index for fast approximate nearest-neighbor search
- Optimized connection pooling settings
- Batch processing with progress tracking
- Automatic optimal index parameter calculation

### Reliability Enhancements
- Idempotent indexing operations
- Delta indexing with ETag tracking
- Per-document error handling
- Transaction safety with upsert logic
