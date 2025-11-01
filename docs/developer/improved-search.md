# Improved Search Architecture

## Overview

The improved search system implements a multi-agent orchestration workflow that enhances search quality through:

1. **Keyword-Based Search**: Exact match and phrase-based retrieval using PostgreSQL full-text search
2. **Document-Level Analysis**: Assessing entire documents instead of only matching chunks
3. **Intelligent Aggregation**: Combining results from multiple search strategies with deduplication
4. **Multi-Step Orchestration**: Agent-based workflow with query planning, searching, evaluation, and aggregation

## Key Improvements

### Document-Level Context

- Count how many chunks each document contributes to results
- Always include the first 2 chunks of each document for context (not just matching chunks)
- Assess documents by file type, filename, and overall relevance

### Keyword Matching

- Detect exact keyword matches in addition to semantic similarity
- Track which keywords appear in which documents
- Use keyword presence as a signal for relevance strength

### Deduplication & Aggregation

- Ensure each chunk appears only once in final results
- Merge candidates from multiple search strategies (vector, lexical, keyword)
- Calculate document-level strength scores (0-100) based on multiple signals

## Architecture

### State Object Schema

The search state captures all steps, findings, and metadata:

```json
{
  "state": {
    "originalPrompt": "string",
    "steps": [
      {
        "findings": [
          {
            "docId": "string",
            "filename": "string",
            "providerType": "string",
            "providerName": "string",
            "strength": 0,
            "comment": "string",
            "distance": 0.0,
            "keywords": ["string"],
            "chunkCount": 0,
            "chunks": [
              {
                "chunkId": "string",
                "chunkNum": 0,
                "distance": 0.0,
                "text": "string",
                "matchedKeywords": ["string"]
              }
            ],
            "contextChunks": [
              {
                "chunkId": "string",
                "chunkNum": 0,
                "text": "string"
              }
            ]
          }
        ],
        "language": "string",
        "lookingFor": "string",
        "keywords": ["string"],
        "phrase": "string",
        "docType": "string",
        "stepPrompt": "string",
        "stepName": "string"
      }
    ]
  }
}
```

## Multi-Agent Workflow

### Agent Chain (from images)

The system uses the following agent pipeline:

1. **Query Planner Agent**

   - Input: User's original prompt
   - Output: Structured search plan with keywords, phrase, document type, language
   - Function: Analyzes intent and extracts search parameters

2. **Searcher Agent**

   - Input: Search plan from planner
   - Output: Raw results from multiple search strategies
   - Function: Executes vector search, lexical search, and keyword search in parallel

3. **Evaluator Agent**

   - Input: Raw search results
   - Output: Scored and commented findings with strength ratings
   - Function: Assesses relevance, assigns strength scores (0-100), adds explanatory comments

4. **Aggregator Agent**
   - Input: Evaluated findings from multiple steps
   - Output: Final deduplicated and ranked results
   - Function: Merges chunks by document, adds context chunks (first 2), removes duplicates

### Orchestration Flow (from workflow diagram)

```
User Query
    ↓
[Query Planner Agent]
    ↓
Search Plan (keywords, phrase, docType)
    ↓
[Searcher Agent - Parallel Execution]
    ├─→ Vector Search (semantic similarity)
    ├─→ Lexical Search (full-text)
    └─→ Keyword Search (exact matches)
    ↓
Raw Results (chunks from all strategies)
    ↓
[Evaluator Agent]
    ↓
Scored Findings (strength, comments, document-level)
    ↓
[Aggregator Agent]
    ├─→ Group by document
    ├─→ Add first 2 chunks for context
    ├─→ Deduplicate chunks
    └─→ Calculate final strength
    ↓
Final Results with State
```

## Implementation Requirements

### 1. Database Changes

Create `search_states` table:

- `id` (UUID, PK)
- `original_prompt` (TEXT)
- `state_data` (JSONB) - stores complete state object
- `created_at` (TIMESTAMPTZ)

Add indexes:

- `search_states_created_at_idx` on `created_at`
- GIN index on `state_data` for JSONB queries

### 2. New Services

**KeywordSearchService**:

- Exact phrase matching using `websearch_to_tsquery`
- Keyword extraction and highlighting
- Track matched keywords per chunk

**DocumentAggregationService**:

- Group chunks by `doc_id`
- Fetch first 2 chunks of each document for context
- Deduplicate chunks across all findings
- Calculate document-level strength scores

**SearchOrchestrationService**:

- Implements the 4-agent workflow
- Manages search state through multiple steps
- Coordinates parallel search execution
- Persists state to database

### 3. Agent Implementations

**QueryPlannerAgent**:

- Extract 1-3 keywords from user query
- Generate optimized phrase for vectorization
- Detect document type hints (e.g., "invoice", "article")
- Identify language preference

**SearcherAgent**:

- Execute searches in parallel using `Task.WhenAll`
- Combine results from vector, lexical, and keyword searches
- Handle provider filtering

**EvaluatorAgent**:

- Score each document finding (0-100) based on:
  - Semantic distance (vector score)
  - Lexical rank
  - Keyword match count
  - Chunk count per document
  - File type relevance
- Generate explanatory comments (max 300 chars)

**AggregatorAgent**:

- Merge findings from all steps
- Ensure first 2 chunks always included
- Remove duplicate chunks (same doc_id + chunk_num)
- Sort by final strength score

### 4. Scoring Algorithm

Document strength (0-100) calculated as:

```
strength = (
    vectorScore * 40 +      // semantic relevance
    lexicalScore * 20 +     // keyword presence
    keywordBonus * 20 +     // exact keyword matches
    chunkCount * 10 +       // number of matching chunks
    contextBonus * 10       // file type/name relevance
)
```

### 5. Testing Requirements

- Unit tests for each agent
- Integration tests for full orchestration
- Test cases:
  - Empty results handling
  - Duplicate chunk detection
  - Context chunk inclusion
  - Keyword matching accuracy
  - Multi-step state persistence

## Migration Path

1. Add `search_states` table (backward compatible)
2. Implement new services alongside existing `VectorSearchService`
3. Add new `/search/multi-step` endpoint for improved search
4. Keep existing `/query` and `/docsearch` endpoints functional
5. After validation, migrate `/query` to use new orchestration
6. Remove old single-step implementation

## Performance Considerations

- Parallel search execution (vector + lexical + keyword)
- Limit context chunks to first 2 per document
- Cache search state in memory for session
- Index JSONB state data for analytics queries
