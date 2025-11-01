# Search Workflow: From User Prompt to Results

This document traces the complete search logic flow from user input to output results.

---

## Overview

The search system supports two modes:

1. **Simple Mode** (depth=1): Direct vector search + AI answer generation
2. **Smart Mode** (depth=2-5): Multi-agent orchestration with query planning, parallel searches, evaluation, and refinement

Both modes can operate in **streaming** (SSE) or **non-streaming** mode.

---

## 1. Frontend: User Input

### Entry Points

**File:** `src/web/src/components/Ask.tsx`

```
User types question → TextField component
                    ↓
              [Submit Button] or [Enter key]
                    ↓
              submit() function
```

### Request Construction

```typescript
// Ask.tsx - submit()
const queryRequest = {
  question: question, // User's text input
  providerNames: providerNames, // Optional filters
  topK: topK, // Max results (default: 10)
  searchDepth: searchDepth, // 1-5 (default: 3)
  streamSteps: streamMode, // true/false
  history: messages, // Conversation context
};
```

### API Call

```typescript
if (streamMode) {
  await postQueryStream(queryRequest, handleStreamUpdate);
} else {
  const resp = await postQuery(queryRequest);
}
```

**File:** `src/web/src/api.ts`

- `postQuery()` → HTTP POST to `/query` (non-streaming)
- `postQueryStream()` → HTTP POST to `/query` with EventSource/SSE (streaming)

---

## 2. Backend: API Entry Point

### Endpoint Registration

**File:** `src/Api/Program.cs`

```csharp
app.MapPost("/query", async (
    HttpContext httpContext,
    QueryRequest request,
    QueryHandler queryHandler,
    CancellationToken ct) =>
{
    return await queryHandler.HandleQueryAsync(httpContext, request, ct);
});
```

### Request Model

**File:** `src/Api/Models/QueryModels.cs`

```csharp
public record QueryRequest(
    string Question,          // Required
    int? TopK,               // Optional (default from config)
    string? ProviderType,    // Optional filter
    string? ProviderName,    // Optional filter
    int? SearchDepth,        // Optional (default: 3)
    bool StreamSteps,        // Optional (default: false)
    List<ChatMessage>? History // Optional conversation context
);
```

---

## 3. Query Handler: Route to Simple or Smart Mode

**File:** `src/Api/Handlers/QueryHandler.cs`

```
HandleQueryAsync()
    ↓
Validate Question (not empty)
    ↓
Determine Depth = Clamp(request.SearchDepth ?? default, 1, maxDepth)
    ↓
┌─────────────────┐
│ if (depth == 1) │ → HandleSimpleQueryAsync()  [SIMPLE MODE]
│                 │
│ else            │ → HandleSmartQueryAsync()   [SMART MODE]
└─────────────────┘
```

---

## 4A. SIMPLE MODE (Depth = 1)

### Flow Diagram

```
HandleSimpleQueryAsync()
    ↓
[1] Generate Embedding
    │ aiService.EmbedAsync(question) → float[]
    ↓
[2] Vector Search
    │ searchService.SearchAsync(embedding, question, topK, ..., depth=1)
    ↓
[3] Check Results
    │ if (sources.Count == 0) → Return "no results" response
    │ else → continue
    ↓
[4] Generate Answer
    │ GenerateSimpleAnswerAsync(request, sources)
    │   ├─ Build context from source chunks
    │   ├─ Create system + user prompts
    │   └─ aiService.CompleteChatAsync() → ChatCompletionResult
    ↓
[5] Return QueryResponse
    └─ { Answer, Sources, TokensUsed }
```

### Vector Search Details

**File:** `src/Api/Services/VectorSearchService.cs`

```
SearchAsync(embedding, queryText, topK, filters, depth=1)
    ↓
┌─────────────────────────────────────────┐
│ Document-level filtering (optional)     │
│   - Query document embeddings (top-N)   │
│   - Get allowed doc_ids                 │
└─────────────────────────────────────────┘
    ↓
┌─────────────────────────────────────────┐
│ Parallel Searches:                      │
│   ├─ Vector Search (pgvector)           │
│   │   SQL: ORDER BY embedding <=> query │
│   └─ Lexical Search (PostgreSQL FTS)    │
│       SQL: ts_rank_cd(lexeme, query)    │
└─────────────────────────────────────────┘
    ↓
Combine & Rank Results
    │ - Merge vector + lexical scores
    │ - Weight: depth=1 → 100% lexical preferred
    │ - Sort by combined score
    ↓
Return List<Source>
```

**Database Tables:**

- `docs_chunks` - chunk text, embeddings, metadata
- `docs_files` - document-level avg embeddings (optional filtering)

---

## 4B. SMART MODE (Depth = 2-5)

### High-Level Flow

```
HandleSmartQueryAsync()
    ↓
Create MultiStepSearchRequest
    ↓
if (streamSteps)
    ├─ HandleStreamingOrchestrationAsync()
    │   └─ Real-time SSE updates
else
    └─ HandleNonStreamingOrchestrationAsync()
        └─ Return final result only
```

Both paths call: **SearchOrchestrationService.ExecuteSearchAsync()**

---

## 5. Multi-Agent Search Orchestration

**File:** `src/Api/Services/Agents/SearchOrchestrationService.cs`

### Main Loop

```
ExecuteSearchAsync(request)
    ↓
Initialize: searchId, steps[], thinkingSteps[]
    ↓
┌──────────────────────────────────────────────┐
│ LOOP: currentDepth = 1 to maxDepth           │
│                                              │
│ ┌────────────────────────────────────────┐  │
│ │ Step 1: QUERY PLANNING                 │  │
│ │   - QueryPlannerAgent.PlanSearchAsync()│  │
│ │   - Extract keywords                   │  │
│ │   - Refine phrase with AI              │  │
│ │   - Detect doc type, language          │  │
│ │   → SearchPlan                          │  │
│ └────────────────────────────────────────┘  │
│     ↓                                        │
│ ┌────────────────────────────────────────┐  │
│ │ Step 2: PARALLEL SEARCHES              │  │
│ │   - SearcherAgent.SearchAsync()        │  │
│ │     ├─ Vector Search                   │  │
│ │     └─ Keyword Search (FTS + pattern)  │  │
│ │   → List<RawSearchResult>              │  │
│ └────────────────────────────────────────┘  │
│     ↓                                        │
│ ┌────────────────────────────────────────┐  │
│ │ Step 3: EVALUATION                     │  │
│ │   - EvaluatorAgent.EvaluateAsync()     │  │
│ │   - Aggregate chunks → documents       │  │
│ │   - Calculate strength scores          │  │
│ │   → List<SearchFinding>                │  │
│ └────────────────────────────────────────┘  │
│     ↓                                        │
│ ┌────────────────────────────────────────┐  │
│ │ Step 4: REFINEMENT DECISION            │  │
│ │   - RefinementAgent.ShouldRefineAsync()│  │
│ │   - Check if results are sufficient    │  │
│ │   → { Continue?, RefinedQuery? }       │  │
│ └────────────────────────────────────────┘  │
│     ↓                                        │
│ if (should continue && depth < max)         │
│     ├─ Update currentQuery                  │
│     └─ LOOP again                           │
│ else                                        │
│     └─ BREAK                                │
│                                              │
└──────────────────────────────────────────────┘
    ↓
┌────────────────────────────────────────┐
│ Final Step: AGGREGATION                │
│   - AggregatorAgent.AggregateAsync()   │
│   - Merge results from all steps       │
│   - Deduplicate by doc_id              │
│   - Re-rank by combined strength       │
│   → List<SearchFinding> (final)        │
└────────────────────────────────────────┘
    ↓
Return MultiStepSearchResponse
```

---

## 6. Agent Details

### Agent 1: Query Planner

**File:** `src/Api/Services/Agents/QueryPlannerAgent.cs`

```
PlanSearchAsync(query)
    ↓
[1] Extract Keywords
    │ - Preserve exact phrases in quotes
    │ - Keep ALL-CAPS terms
    │ - Maintain original language
    ↓
[2] AI Refinement
    │ - System prompt: "multilingual search optimizer"
    │ - Input: user query
    │ - Output: natural language search phrase
    │ - aiService.CompleteChatAsync()
    ↓
[3] Detect Metadata
    │ - Document type (pdf, md, docx, etc.)
    │ - Language (Swedish, English, etc.)
    ↓
Return SearchPlan {
    OriginalQuery,
    Keywords,        // ["contract", "status", "progress"]
    Phrase,          // "contract status be in progress"
    DocType,         // "pdf" | null
    Language,        // "Swedish" | null
    LookingFor       // Description
}
```

### Agent 2: Searcher

**File:** `src/Api/Services/Agents/SearcherAgent.cs`

```
SearchAsync(plan, topK, filters)
    ↓
[1] Generate Embedding
    │ embedding = aiService.EmbedAsync(plan.Phrase)
    ↓
[2] Parallel Execution
    ├─ vectorTask = ExecuteVectorSearchAsync(phrase, embedding, topK)
    │   └─ Calls VectorSearchService.SearchAsync()
    │       └─ PostgreSQL: SELECT ... ORDER BY embedding <=> query
    │
    └─ keywordTask = ExecuteKeywordSearchAsync(keywords, embedding, topK)
        └─ Calls KeywordSearchService.SearchAsync()
            ├─ Full-text search (PostgreSQL FTS)
            └─ Pattern matching (ILIKE queries)
    ↓
[3] Combine Results
    └─ Return List<RawSearchResult> (vector + keyword)
```

**RawSearchResult:**

```csharp
{
    DocId,
    Filename,
    ProviderType,
    ProviderName,
    ChunkNum,
    Text,
    Distance,
    SearchStrategy,      // "vector" | "keyword" | "pattern"
    MatchedKeywords      // ["contract", "progress"]
}
```

### Agent 3: Evaluator

**File:** `src/Api/Services/Agents/EvaluatorAgent.cs`

```
EvaluateAsync(plan, rawResults)
    ↓
[1] Aggregate by Document
    │ aggregationService.AggregateByDocumentAsync(rawResults)
    │   ├─ Group chunks by doc_id
    │   ├─ Fetch context chunks (window ±2)
    │   ├─ Calculate strength score (0-100)
    │   │   - Vector score (distance)
    │   │   - Keyword matches
    │   │   - Filename relevance
    │   │   - Context quality
    │   └─ Create SearchFinding per document
    ↓
[2] Enhance Findings
    │ - Adjust strength based on doc type match
    │ - Adjust strength based on language match
    │ - Add contextual comments
    ↓
Return List<SearchFinding> {
    DocId,
    Filename,
    ProviderType,
    ProviderName,
    Chunks[],          // Aggregated chunks from this doc
    Strength,          // 0-100 score
    ChunkCount,
    Comment            // "High relevance; matches 'pdf' type"
}
```

### Agent 4: Refinement

**File:** `src/Api/Services/Agents/RefinementAgent.cs`

```
ShouldRefineAsync(originalQuery, steps, currentDepth, maxDepth)
    ↓
Check Conditions:
    ├─ if (currentDepth >= maxDepth) → STOP
    ├─ if (no results in last step) → STOP
    ├─ if (high-quality results) → STOP
    └─ else → CONTINUE with refined query
    ↓
Return RefinementDecision {
    ShouldContinue,    // true/false
    RefinedQuery,      // "alternative search phrase"
    Reason             // "Poor coverage, refining..."
}
```

### Agent 5: Aggregator

**File:** `src/Api/Services/Agents/AggregatorAgent.cs`

```
AggregateAsync(steps[])
    ↓
[1] Collect All Findings
    │ - Merge findings from all search steps
    │ - Step 1 (initial), Step 2 (refinement 1), etc.
    ↓
[2] Deduplicate by DocId
    │ - Keep best chunks per document
    │ - Combine strength scores
    ↓
[3] Re-rank
    │ - Sort by combined strength (high → low)
    │ - Apply bonus for appearing in multiple steps
    ↓
Return List<SearchFinding> (deduplicated, ranked)
```

---

## 7. Response Construction

### Smart Mode Response Path

```
SearchOrchestrationService returns MultiStepSearchResponse
    ↓
QueryHandler converts to QueryResponse format
    ├─ Extract FinalFindings
    ├─ Sort by Strength
    ├─ Convert SearchFinding → Source
    │   - Best chunk as representative
    │   - Combined chunk texts
    └─ Generate summary answer from findings
    ↓
if (streaming)
    └─ Send final SSE event with ChatResponse
else
    └─ Return Results.Ok(QueryResponse)
```

### Response Model

**File:** `src/Api/Models/QueryModels.cs`

```csharp
public record QueryResponse(
    string Answer,              // Summary or AI-generated answer
    List<Source> Sources,       // Ranked results
    int TokensUsed,            // AI token count
    List<string>? Steps,       // Thinking steps (smart mode)
    List<DocumentResult>? Files, // Doc-level results (optional)
    List<ChatMessage>? History, // Conversation history
    List<ModelUsageInfo>? ModelUsage // Token breakdown
);
```

**Source:**

```csharp
{
    DocId,
    Filename,
    ChunkNum,
    Text,              // Chunk content or combined chunks
    Distance,          // Similarity score (0-2, lower = better)
    Citation,          // "[provider/name:file#chunk5]"
    ProviderType,
    ProviderName
}
```

---

## 8. Frontend: Results Display

### Streaming Updates

**File:** `src/web/src/components/Ask.tsx`

```typescript
handleStreamUpdate(update: ChatStreamUpdate) {
  if (update.type === 'step') {
    // Intermediate thinking step
    setStreamingAnswer(prev => prev + update.message + '\n');
  }
  else if (update.type === 'final') {
    // Final response
    setResponse({
      answer: update.final.answer,
      sources: update.final.sources,
      steps: update.final.steps,
      ...
    });
    setStreamingAnswer(''); // Clear thinking steps
  }
}
```

### UI Components

```
Response Display
    ├─ Answer Card
    │   ├─ Main answer text
    │   └─ Token usage / model info
    │
    ├─ Thinking Steps (if shown)
    │   └─ Collapsible section with step-by-step logs
    │
    └─ Source List
        └─ For each Source:
            ├─ Filename + Citation
            ├─ Distance score
            ├─ Chunk text preview
            └─ Provider metadata
```

---

## 9. Complete Flow Visualization

```
┌──────────────────────────────────────────────────────────────────────┐
│                          USER INPUT                                  │
│  Ask.tsx → TextField → "What is the deployment process?"            │
└──────────────────────────────────────────────────────────────────────┘
                              ↓
┌──────────────────────────────────────────────────────────────────────┐
│                      API REQUEST (HTTP POST)                         │
│  POST /query { question, searchDepth=3, streamSteps=true }          │
└──────────────────────────────────────────────────────────────────────┘
                              ↓
┌──────────────────────────────────────────────────────────────────────┐
│                     QUERY HANDLER (ROUTER)                           │
│  depth=3 → HandleSmartQueryAsync() → Orchestration Service           │
└──────────────────────────────────────────────────────────────────────┘
                              ↓
┌──────────────────────────────────────────────────────────────────────┐
│                   MULTI-AGENT ORCHESTRATION                          │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │ Step 1:                                                     │    │
│  │  [Query Planner] → SearchPlan                              │    │
│  │    phrase: "deployment process steps"                      │    │
│  │    keywords: ["deployment", "process", "steps"]            │    │
│  └─────────────────────────────────────────────────────────────┘    │
│                              ↓                                       │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │ Step 2:                                                     │    │
│  │  [Searcher Agent] → Parallel Searches                      │    │
│  │    ├─ Vector: Query DB with embedding                      │    │
│  │    │   SELECT ... ORDER BY embedding <=> @query            │    │
│  │    │   → 15 chunks from 8 files                            │    │
│  │    └─ Keyword: FTS + Pattern matching                      │    │
│  │        SELECT ... ts_rank_cd(lexeme, 'deployment')         │    │
│  │        → 10 chunks from 5 files                            │    │
│  └─────────────────────────────────────────────────────────────┘    │
│                              ↓                                       │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │ Step 3:                                                     │    │
│  │  [Evaluator Agent] → Aggregate & Score                     │    │
│  │    - Group 25 chunks → 10 documents                        │    │
│  │    - Calculate strength scores                             │    │
│  │    - Fetch context chunks                                  │    │
│  │    → SearchFinding[] (strength: 85, 72, 68, ...)          │    │
│  └─────────────────────────────────────────────────────────────┘    │
│                              ↓                                       │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │ Step 4:                                                     │    │
│  │  [Refinement Agent] → Decision                             │    │
│  │    - Check result quality                                  │    │
│  │    - High coverage detected                                │    │
│  │    → STOP (no refinement needed)                           │    │
│  └─────────────────────────────────────────────────────────────┘    │
│                              ↓                                       │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │ Final:                                                      │    │
│  │  [Aggregator Agent] → Deduplicate & Rank                   │    │
│  │    - Merge all steps                                       │    │
│  │    - Deduplicate by doc_id                                 │    │
│  │    - Final ranking by strength                             │    │
│  │    → 10 SearchFindings                                     │    │
│  └─────────────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────────────┘
                              ↓
┌──────────────────────────────────────────────────────────────────────┐
│                      RESPONSE CONSTRUCTION                           │
│  Convert SearchFindings → Sources                                    │
│  Generate answer summary                                             │
│  Package as QueryResponse                                            │
└──────────────────────────────────────────────────────────────────────┘
                              ↓
┌──────────────────────────────────────────────────────────────────────┐
│                       STREAMING OUTPUT (SSE)                         │
│  data: {"type":"step","message":"🤔 Analyzing query..."}            │
│  data: {"type":"step","message":"📋 Query plan created..."}         │
│  data: {"type":"step","message":"🔍 Executing searches..."}         │
│  data: {"type":"final","final":{answer,sources,steps}}              │
└──────────────────────────────────────────────────────────────────────┘
                              ↓
┌──────────────────────────────────────────────────────────────────────┐
│                          UI DISPLAY                                  │
│  ┌────────────────────────────────────────────────────────────┐     │
│  │ Answer:                                                    │     │
│  │ "Found 10 relevant documents with 25 matching sections:   │     │
│  │  • deployment-guide.md (strength: 85, 8 sections)         │     │
│  │  • ci-cd-pipeline.yaml (strength: 72, 4 sections)         │     │
│  │  • kubernetes-deploy.md (strength: 68, 3 sections)..."    │     │
│  └────────────────────────────────────────────────────────────┘     │
│                                                                      │
│  ┌────────────────────────────────────────────────────────────┐     │
│  │ Sources (10):                                              │     │
│  │  1. deployment-guide.md                                    │     │
│  │     Distance: 0.234 | filesystem/docs                     │     │
│  │     "Step 1: Build the Docker image..."                   │     │
│  │                                                            │     │
│  │  2. ci-cd-pipeline.yaml                                   │     │
│  │     Distance: 0.287 | github/main-repo                    │     │
│  │     "deploy: docker push..."                              │     │
│  └────────────────────────────────────────────────────────────┘     │
└──────────────────────────────────────────────────────────────────────┘
```

---

## 10. Key Data Structures

### PostgreSQL Tables

```sql
-- Chunk-level table (main search target)
CREATE TABLE docs_chunks (
    doc_id TEXT,
    filename TEXT,
    provider_type TEXT,
    provider_name TEXT,
    chunk_num INTEGER,
    text TEXT,
    embedding vector(1536),           -- pgvector
    search_lexeme tsvector,           -- Full-text search
    metadata JSONB,
    PRIMARY KEY (doc_id, chunk_num)
);

-- Document-level table (for filtering)
CREATE TABLE docs_files (
    doc_id TEXT PRIMARY KEY,
    filename TEXT,
    provider_type TEXT,
    provider_name TEXT,
    avg_embedding vector(1536),       -- Document-level embedding
    chunk_count INTEGER,
    indexed_at TIMESTAMP
);
```

### Search Indexes

```sql
-- Vector similarity
CREATE INDEX idx_chunks_embedding ON docs_chunks
USING ivfflat (embedding vector_cosine_ops);

-- Full-text search
CREATE INDEX idx_chunks_lexeme ON docs_chunks
USING gin(search_lexeme);

-- Document filtering
CREATE INDEX idx_files_embedding ON docs_files
USING ivfflat (avg_embedding vector_cosine_ops);
```

---

## 11. Configuration

**Environment Variables:**

```bash
# Search behavior
DEFAULT_SEARCH_DEPTH=3        # Default depth (1-5)
MAX_SEARCH_DEPTH=5            # Maximum allowed depth
DEFAULT_TOP_K=10              # Default result count
MAX_TOP_K=50                  # Maximum result count

# Lexical search
ENABLE_LEXICAL_SEARCH=true    # Enable FTS
LEXICAL_SCORE_WEIGHT=0.3      # Weight (0.0-1.0)
MAX_LEXICAL_RESULTS=20        # Max FTS results
LEXICAL_CONFIGURATION=english # PostgreSQL text search config

# Document filtering
ENABLE_DOCUMENT_LEVEL_FILTERING=true
DOCUMENT_LEVEL_TOP_K=20       # Pre-filter to top-N docs

# AI configuration
DB_CONNECTION_STRING=postgresql://...
# AI models configured in database (flexible JSON)
```

---

## Summary

The search system follows this path:

1. **Frontend** collects user query → sends HTTP request
2. **API endpoint** receives request → routes to QueryHandler
3. **QueryHandler** determines mode:
   - **Simple** (depth=1): Embed → Search → Answer
   - **Smart** (depth≥2): Multi-agent orchestration
4. **Multi-agent flow** (if smart mode):
   - Plan query (keywords, phrase, metadata)
   - Parallel searches (vector + keyword)
   - Evaluate & aggregate chunks → documents
   - Decide on refinement
   - Aggregate across steps
5. **Database** executes:
   - Vector similarity (pgvector)
   - Full-text search (PostgreSQL FTS)
   - Document-level filtering
6. **Response** converted to QueryResponse format
7. **Frontend** displays results with sources

This architecture supports **multilingual**, **provider-agnostic** search with **adaptive depth** and **real-time streaming**.
