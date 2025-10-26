# Embeddings & AI Layer

Responsible for generating embeddings and synthesizing answers via model-agnostic architecture.

## Model-Agnostic Architecture

The system uses a **fully flexible, JSON-based configuration** that supports any AI provider (OpenAI, Anthropic, Azure OpenAI, local models, etc.):

- **No hardcoded parameters**: All model settings (temperature, max_tokens, etc.) stored as JSON
- **Configurable endpoints**: Full URL, headers, request/response templates per model
- **Template system**: Request bodies use placeholders like `{MODEL_ID}`, `{MESSAGES}`, `{INPUT}`
- **Dynamic parameter merging**: DefaultParams JSON merged into requests at runtime

### Configuration Structure

Each model (chat or embedding) has:
- `Url`: Full API endpoint URL
- `Headers`: Dictionary (e.g., `{"Authorization": "Bearer sk-..."}`)
- `RequestTemplate`: JSON template with placeholders
- `ResponseMapping`: JSONPath expressions to extract response fields
- `DefaultParams`: Model-specific parameters (e.g., `{"temperature": 0.7}`)

## Embeddings

- Configurable embedding model via admin UI or seeding from environment
- Default: `text-embedding-3-small` (1536 dims) when `OPENAI_API_KEY` is set
- Supports **any embedding API** via flexible configuration
- Batched embedding generation with configurable batch size
- Request template example:
  ```json
  {
    "model": "{MODEL_ID}",
    "input": "{INPUT}",
    "encoding_format": "float"
  }
  ```

## Answer Generation

- `ModelAgnosticAiService` provides multi-tier chat completion (Micro/Mini/Full)
- Strategy selection: Eco (cost), Standard (balanced), Turbo (quality)
- Automatic fallback between tiers when model unavailable
- Supports **any chat completion API** via flexible configuration
- Request template example:
  ```json
  {
    "model": "{MODEL_ID}",
    "messages": {MESSAGES}
  }
  ```
- Uses `max_completion_tokens` for newer OpenAI models (GPT-4o, GPT-5)

## Chat

- `ChatService` sequences: embed last user message → retrieve context → build incremental answer
- Streaming: server-sent events emitting step updates & final answer

## Extending Models

| Goal | Strategy |
|------|----------|
| New embedding model | Add config in admin UI with URL, headers, template, dimensions; reindex |
| Multi-model | Configure multiple models in registry; system selects by tier/strategy |
| Local model proxy | Set URL to local endpoint (e.g., `http://localhost:8080/v1/embeddings`) |
| Different AI provider | Configure custom URL, headers, request/response templates |
| Custom parameters | Add to DefaultParams JSON (e.g., `{"temperature": 0.5, "top_p": 0.9}`) |

## Configuration Examples

### Adding Anthropic Claude
```json
{
  "url": "https://api.anthropic.com/v1/messages",
  "headers": {
    "x-api-key": "sk-ant-...",
    "anthropic-version": "2023-06-01"
  },
  "requestTemplate": {
    "model": "{MODEL_ID}",
    "messages": "{MESSAGES}",
    "max_tokens": 4096
  },
  "responseMapping": {
    "content": "$.content[0].text",
    "role": "$.role"
  }
}
```

### Adding Local Ollama Model
```json
{
  "url": "http://localhost:11434/api/embeddings",
  "headers": {},
  "requestTemplate": {
    "model": "{MODEL_ID}",
    "prompt": "{INPUT}"
  },
  "responseMapping": {
    "embedding": "$.embedding"
  }
}
```

## Prompt Strategy (Simplified)

- System-style instruction (implicit)
- Context concatenation (ordered by similarity)
- User question appended
- Model asked to answer citing sources implicitly (source chunk mapping done externally)

## Considerations

| Concern | Mitigation |
|---------|------------|
| Context overflow | Limit chunk sizes / reduce top-K |
| Hallucination | Provide direct chunk content; consider answer validation |
| Cost | Batch embeddings; right-size chunk length; use Eco strategy |
| API compatibility | Use RequestTemplate and ResponseMapping to adapt any API |

## Future Enhancements

- Per-provider model routing
- Reranking stage (e.g. cross encoder)
- Source citation markers referencing chunk IDs
- Auto-detection of response structure

## Next

- Search & ranking internals: [Search & RAG](search-rag.md)
- AI configuration guide: [Configuration System](config-system.md)
