# AI Configuration Guide

DocDuck uses a **model-agnostic, JSON-based configuration system** that supports any AI provider through flexible templates and mappings.

## Overview

The system stores AI configuration in the database with these key properties:

- ✅ **Any AI Provider**: OpenAI, Anthropic, Azure OpenAI, local models (Ollama, LM Studio), custom endpoints
- ✅ **No Hardcoded Parameters**: All model settings stored as JSON (temperature, max_tokens, etc.)
- ✅ **Configurable Everything**: Full control over URLs, headers, request/response structure
- ✅ **Template System**: Dynamic request building with placeholders
- ✅ **Admin UI Management**: Configure via web interface at `/admin/ai`

## Configuration Structure

Each AI model (chat or embedding) has these fields:

| Field | Type | Description |
|-------|------|-------------|
| `url` | string | Full API endpoint URL |
| `headers` | object | HTTP headers (e.g., `{"Authorization": "Bearer sk-..."}`) |
| `requestTemplate` | object | JSON template with placeholders like `{MODEL_ID}`, `{MESSAGES}` |
| `responseMapping` | object | JSONPath expressions to extract response data |
| `defaultParams` | object | Model-specific parameters (e.g., `{"temperature": 0.7}`) |

## Initial Seeding

On first startup, the system seeds configuration from environment variables:

```bash
OPENAI_API_KEY=sk-yourkey
OPENAI_BASE_URL=https://api.openai.com/v1  # optional
OPENAI_MICRO_MODEL=gpt-4o-mini
OPENAI_MINI_MODEL=gpt-4o-mini
OPENAI_FULL_MODEL=gpt-4o
OPENAI_EMBEDDING_MODEL=text-embedding-3-small
```

**After seeding**, all configuration is managed via the admin UI. Environment variables are **not** used for ongoing operation.

## Admin UI Configuration

Access the AI configuration UI at: `http://localhost:8080/admin/ai`

### Adding a Chat Model

1. Click "Add Model"
2. Fill in the form:
   - **Display Name**: Human-readable name (e.g., "Claude 3.5 Sonnet")
   - **Model ID**: API model identifier (e.g., `claude-3-5-sonnet-20241022`)
   - **URL**: API endpoint (e.g., `https://api.anthropic.com/v1/messages`)
   - **Headers**: JSON with authentication and other headers
   - **Request Template**: JSON template for API requests
   - **Response Mapping**: JSONPath expressions to extract content
   - **Default Parameters**: Model-specific settings as JSON

### Adding an Embedding Model

1. Click "Add Embedding Model"
2. Similar to chat models, plus:
   - **Dimensions**: Vector dimensionality (e.g., 1536 for OpenAI, 768 for some models)
   - **Batch Size**: How many texts to embed in one API call

## Provider Examples

### OpenAI (Default)

**Chat Model:**
```json
{
  "url": "https://api.openai.com/v1/chat/completions",
  "headers": {
    "Authorization": "Bearer sk-proj-...",
    "Content-Type": "application/json"
  },
  "requestTemplate": {
    "model": "{MODEL_ID}",
    "messages": "{MESSAGES}"
  },
  "responseMapping": {
    "content": "$.choices[0].message.content",
    "role": "$.choices[0].message.role",
    "usage.prompt_tokens": "$.usage.prompt_tokens",
    "usage.completion_tokens": "$.usage.completion_tokens",
    "usage.total_tokens": "$.usage.total_tokens"
  },
  "defaultParams": {}
}
```

**Embedding Model:**
```json
{
  "url": "https://api.openai.com/v1/embeddings",
  "headers": {
    "Authorization": "Bearer sk-proj-...",
    "Content-Type": "application/json"
  },
  "requestTemplate": {
    "model": "{MODEL_ID}",
    "input": "{INPUT}",
    "encoding_format": "float"
  },
  "responseMapping": {
    "embedding": "$.data[0].embedding",
    "usage.total_tokens": "$.usage.total_tokens"
  },
  "dimensions": 1536
}
```

### Anthropic Claude

```json
{
  "url": "https://api.anthropic.com/v1/messages",
  "headers": {
    "x-api-key": "sk-ant-...",
    "anthropic-version": "2023-06-01",
    "Content-Type": "application/json"
  },
  "requestTemplate": {
    "model": "{MODEL_ID}",
    "messages": "{MESSAGES}",
    "max_tokens": 4096
  },
  "responseMapping": {
    "content": "$.content[0].text",
    "role": "$.role",
    "usage.input_tokens": "$.usage.input_tokens",
    "usage.output_tokens": "$.usage.output_tokens"
  },
  "defaultParams": {
    "temperature": 1.0
  }
}
```

### Azure OpenAI

```json
{
  "url": "https://your-resource.openai.azure.com/openai/deployments/gpt-4o/chat/completions?api-version=2024-02-15-preview",
  "headers": {
    "api-key": "your-azure-key",
    "Content-Type": "application/json"
  },
  "requestTemplate": {
    "messages": "{MESSAGES}"
  },
  "responseMapping": {
    "content": "$.choices[0].message.content",
    "role": "$.choices[0].message.role",
    "usage.prompt_tokens": "$.usage.prompt_tokens",
    "usage.completion_tokens": "$.usage.completion_tokens",
    "usage.total_tokens": "$.usage.total_tokens"
  }
}
```

### Local Ollama

**Chat:**
```json
{
  "url": "http://localhost:11434/api/chat",
  "headers": {
    "Content-Type": "application/json"
  },
  "requestTemplate": {
    "model": "{MODEL_ID}",
    "messages": "{MESSAGES}",
    "stream": false
  },
  "responseMapping": {
    "content": "$.message.content",
    "role": "$.message.role"
  }
}
```

**Embeddings:**
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
  },
  "dimensions": 768
}
```

## Template Placeholders

Request templates support these dynamic placeholders:

| Placeholder | Description | Used In |
|-------------|-------------|---------|
| `{MODEL_ID}` | Model identifier from configuration | Chat, Embedding |
| `{MESSAGES}` | Chat message array (system, user, assistant) | Chat only |
| `{INPUT}` | Text to embed (string or array) | Embedding only |
| `{TEMPERATURE}` | Temperature value from DefaultParams | Legacy (use DefaultParams) |
| `{MAX_TOKENS}` | Max tokens from DefaultParams | Legacy (use DefaultParams) |

**Note**: Use `defaultParams` for model parameters instead of template placeholders. The system merges `defaultParams` into the request JSON before substituting placeholders.

## Response Mapping

JSONPath expressions extract data from API responses:

**Common Paths:**
- OpenAI content: `$.choices[0].message.content`
- Anthropic content: `$.content[0].text`
- OpenAI embedding: `$.data[0].embedding`
- Token usage: `$.usage.total_tokens`

## Model Tiers

The system uses three chat model tiers:

| Tier | Use Case | Strategy |
|------|----------|----------|
| **Micro** | Simple tasks, cheap | Eco (cost-optimized) |
| **Mini** | Balanced tasks | Standard (balanced) |
| **Full** | Complex reasoning | Turbo (quality-focused) |

Configure tier assignments in the admin UI by selecting which model ID is active for each tier.

## Testing Models

Use the "Test" button in the admin UI to verify:
- ✅ API connectivity
- ✅ Authentication
- ✅ Request/response templates work
- ✅ Response mapping extracts data correctly

## Database Schema

AI configuration is stored in `ai_provider_settings` table:

```sql
CREATE TABLE ai_provider_settings (
    provider_id text PRIMARY KEY,
    provider_type text NOT NULL,  -- 'chat' or 'embedding' or 'global'
    settings jsonb NOT NULL,      -- legacy settings
    url text,
    headers jsonb DEFAULT '{"Content-Type": "application/json"}',
    request_template jsonb,
    response_mapping jsonb,
    default_params jsonb DEFAULT '{}',
    test_status text DEFAULT 'Untested',
    last_tested_at timestamptz,
    last_test_message text,
    updated_at timestamptz DEFAULT now()
);
```

## Troubleshooting

### Model Test Fails with 401/403
- Check API key in headers is correct and not masked
- Verify URL is correct (include version, deployment name for Azure)
- Check header format (some APIs use `x-api-key`, others `Authorization: Bearer`)

### Model Test Fails with 404
- Ensure `request_template` is populated (not null)
- Check URL includes full path (e.g., `/v1/chat/completions`)
- For Azure, include `api-version` query parameter

### Embedding Fails
- Verify `dimensions` matches the model's output
- Check `response_mapping.embedding` JSONPath is correct
- Ensure database vector column supports the dimensionality

### Response Parsing Error
- Use admin UI test to see raw API response
- Adjust `response_mapping` JSONPath expressions
- Check for API version differences (field names may vary)

## Best Practices

1. **Test Before Saving**: Always use the test button to verify configuration
2. **Start Simple**: Use minimal `defaultParams`, add only what you need
3. **Document Custom Models**: Use descriptive display names
4. **Secure API Keys**: Never commit keys to version control
5. **Monitor Costs**: Track usage via your AI provider's dashboard
6. **Version Compatibility**: Some APIs change - keep response mappings updated

## API Reference

For programmatic access to AI configuration:

```bash
# Get current configuration
curl -H "X-Admin-Token: your-secret" http://localhost:8080/api/admin/ai/config

# Update configuration
curl -X PUT -H "X-Admin-Token: your-secret" \
     -H "Content-Type: application/json" \
     -d @ai-config.json \
     http://localhost:8080/api/admin/ai/config

# Test a model
curl -X POST -H "X-Admin-Token: your-secret" \
     -H "Content-Type: application/json" \
     -d '{"modelId": "openai-full"}' \
     http://localhost:8080/api/admin/ai/test-model
```

## Next Steps

- [AI Layer Architecture](../developer/ai-layer.md)
- [Configuration System Internals](../developer/config-system.md)
- [API Usage Guide](api-usage.md)
