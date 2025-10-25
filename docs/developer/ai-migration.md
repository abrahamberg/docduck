# AI Provider Migration: Model-Agnostic Architecture

## Overview

The system has been migrated from OpenAI-specific SDK implementation to a **model-agnostic architecture** supporting any OpenAI-compatible API provider.

## Key Features

### 3-Tier Model System
- **Micro**: Smallest/fastest models for simple tasks
- **Mini**: Balanced models for moderate complexity
- **Full**: Most capable models for complex reasoning

All tiers are **optional** - configure only what you need. The system automatically falls back to available models.

### Selection Strategies
User can choose their preferred strategy via API:
- **Eco**: Cost-focused (prefers Micro → Mini → Full)
- **Standard**: Balanced (prefers Mini → Full → Micro)
- **Turbo**: Quality-focused (prefers Full → Mini → Micro)

### Task Complexity Classification
The system automatically classifies tasks:
- **Simple**: Query refinement, rephrasing (uses Micro/Mini)
- **Moderate**: Evaluation with tools, standard answers
- **Complex**: Large context answers requiring maximum capability

### Model Selection Logic
Selection is based on:
1. Task complexity
2. User strategy preference
3. Estimated context size
4. Function calling requirements
5. Available enabled models

## Architecture

### Core Components

#### `AiModelTier` (enum)
Defines tiers: Micro, Mini, Full

#### `ModelSelectionStrategy` (enum)
Defines strategies: Eco, Standard, Turbo

#### `AiModelAssignment`
Configuration for a single chat model:
- Model ID, base URL, API key
- Context/output token limits
- Function calling support
- Cost factor, enabled status
- Custom headers, timeout

#### `AiEmbeddingModelAssignment`
Configuration for embedding model:
- Model ID, base URL, API key
- Embedding dimensions
- Enabled status

#### `AiProviderConfiguration`
Complete AI configuration:
- Optional MicroModel, MiniModel, FullModel
- Required EmbeddingModel
- Default strategy, temperature
- Enabled status

#### `GenericAiHttpClient`
OpenAI-compatible HTTP client:
- Supports OpenAI, Azure Foundry, local servers (llama.cpp, vllm, ollama)
- Chat completions with streaming
- Embeddings (single and batch)
- Function calling
- Token tracking

#### `AiModelSelector`
Intelligent model selection:
- Complexity × Strategy matrix
- Token limit validation
- Function calling requirement check
- Automatic fallback

#### `ModelAgnosticAiService`
Unified service:
- Configuration reload
- Client pooling and caching
- EmbedAsync, EmbedBatchAsync, CompleteChatAsync
- Automatic fallback on model failure

#### `AiProviderConfigurationStore`
Database persistence:
- Stores configuration in `ai_provider_settings` table
- JSONB column with key `"ai_provider_v2"`

#### `AiConfigurationSeeder`
Environment-based seeding:
- Auto-creates config from OPENAI_API_KEY on first run
- Sets up default Mini model + embedding model

### Admin API

All endpoints under `/admin/ai/*`:

#### `GET /admin/ai/config`
Retrieve current AI configuration (API keys masked)

#### `PUT /admin/ai/config`
Update AI configuration

#### `POST /admin/ai/probe`
Test connectivity to a model assignment

#### `POST /admin/ai/check-embedding-change`
Check impact of changing embedding model (returns chunk count that would be affected)

## Configuration

### Environment Variables
Used for automatic seeding on first run:

```bash
# Required - Enables full 3-tier OpenAI configuration
OPENAI_API_KEY=sk-...

# Optional - Override defaults
OPENAI_BASE_URL=https://api.openai.com/v1  # Base URL for all models
OPENAI_MICRO_MODEL=gpt-4o-mini              # Default: gpt-4o-mini
OPENAI_MINI_MODEL=gpt-4o                    # Default: gpt-4o
OPENAI_FULL_MODEL=o1                        # Default: o1
OPENAI_EMBEDDING_MODEL=text-embedding-3-small  # Default: text-embedding-3-small
OPENAI_EMBEDDING_DIMENSIONS=1536            # Default: 1536
```

**Seeding Behavior:**
- If `OPENAI_API_KEY` is set: Creates full 3-tier configuration (Micro/Mini/Full + Embedding)
- If not set: Creates disabled placeholder (configure via admin API later)
- Individual models can be overridden via environment variables
- Seeding only happens if no configuration exists in database

### Database Configuration
Stored in `ai_provider_settings` table:

```sql
SELECT setting_value FROM ai_provider_settings WHERE setting_key = 'ai_provider_v2';
```

Returns JSONB configuration with structure:
```json
{
  "enabled": true,
  "defaultSelectionStrategy": "Standard",
  "miniModel": {
    "id": "default-mini",
    "displayName": "GPT-4o Mini",
    "modelId": "gpt-4o-mini",
    "baseUrl": "https://api.openai.com/v1",
    "apiKey": "sk-...",
    "maxContextTokens": 128000,
    "maxOutputTokens": 16000,
    "supportsFunctionCalling": true,
    "enabled": true
  },
  "embeddingModel": {
    "id": "default-embedding",
    "displayName": "Text Embedding 3 Small",
    "modelId": "text-embedding-3-small",
    "baseUrl": "https://api.openai.com/v1",
    "apiKey": "sk-...",
    "dimensions": 1536,
    "enabled": true
  },
  "defaultTemperature": 0.0
}
```

### Admin Configuration Examples

#### Multiple Providers
```json
{
  "enabled": true,
  "defaultSelectionStrategy": "Standard",
  "microModel": {
    "id": "local-llama",
    "displayName": "Local Llama 3.2 1B",
    "modelId": "llama3.2:1b",
    "baseUrl": "http://localhost:11434/v1",
    "apiKey": "not-required",
    "maxContextTokens": 8192,
    "enabled": true
  },
  "miniModel": {
    "id": "azure-gpt35",
    "displayName": "Azure GPT-3.5 Turbo",
    "modelId": "gpt-35-turbo",
    "baseUrl": "https://your-resource.openai.azure.com/openai/deployments/gpt-35-turbo",
    "apiKey": "...",
    "customHeaders": {
      "api-key": "..."
    },
    "maxContextTokens": 16385,
    "enabled": true
  },
  "fullModel": {
    "id": "openai-gpt4o",
    "displayName": "OpenAI GPT-4o",
    "modelId": "gpt-4o",
    "baseUrl": "https://api.openai.com/v1",
    "apiKey": "sk-...",
    "maxContextTokens": 128000,
    "supportsFunctionCalling": true,
    "enabled": true
  },
  "embeddingModel": {
    "id": "local-embedding",
    "displayName": "Local BGE Small",
    "modelId": "bge-small-en-v1.5",
    "baseUrl": "http://localhost:8080/v1",
    "apiKey": "not-required",
    "dimensions": 384,
    "enabled": true
  }
}
```

## Migration Summary

### Removed Files
**API Layer:**
- `OpenAiSdkService.cs`
- `OpenAiClient.cs`
- `AiServiceAdapter.cs`
- `Admin/OpenAiOptions.cs`
- `Admin/OpenAiDtos.cs`

**Providers.Shared Layer:**
- `Ai/OpenAiProviderSettings.cs`
- `Ai/OpenAiSettingsSeeder.cs`
- `Ai/AiProviderSettingsStore.cs`
- `Ai/AiConfigurationService.cs`

**Indexer Layer:**
- `Options/OpenAiOptions.cs`
- `Options/OpenAiOptionsProvider.cs`
- `Services/OpenAiEmbeddingsClient.cs`

### Added Files
**Providers.Shared Layer:**
- `Ai/AiModelTier.cs` - Enums for tiers and strategies
- `Ai/AiModelAssignment.cs` - Model configuration classes
- `Ai/AiProviderConfiguration.cs` - Complete configuration
- `Ai/GenericAiHttpClient.cs` - OpenAI-compatible HTTP client
- `Ai/AiModelSelector.cs` - Intelligent model selection
- `Ai/ModelAgnosticAiService.cs` - Unified service
- `Ai/AiProviderConfigurationStore.cs` - Database persistence
- `Ai/AiConfigurationSeeder.cs` - Environment seeding

**Admin Layer:**
- `Admin/AiConfigurationDtos.cs` - Admin API DTOs with API key masking

### Modified Files
- `Api/Services/ChatService.cs` - Updated to use ModelAgnosticAiService
- `Api/Admin/AdminEndpointExtensions.cs` - Added `/admin/ai/*` endpoints
- `Api/Program.cs` - Updated DI registrations and endpoints
- `Indexer/MultiProviderIndexerService.cs` - Updated to use ModelAgnosticAiService
- `Indexer/Program.cs` - Updated DI registrations
- `src/Indexer/appsettings.yaml` - Removed OpenAI section
- `src/Indexer/appsettings.local.yaml` - Removed OpenAI section

## Usage Patterns

### From API Code (ChatService)
```csharp
// Embed query
var embedding = await _aiService.EmbedAsync(query, ct);

// Simple chat completion
var messages = new List<ChatMessagePayload> 
{
    new("system", systemPrompt),
    new("user", userPrompt)
};
var result = await _aiService.CompleteChatAsync(
    messages,
    TaskComplexity.Simple,
    ModelSelectionStrategy.Standard,
    null,
    ct);

// With function calling
var options = new ChatCompletionOptions
{
    Tools = tools,
    ToolChoice = "auto"
};
var result = await _aiService.CompleteChatAsync(
    messages,
    TaskComplexity.Moderate,
    ModelSelectionStrategy.Turbo,
    options,
    ct);
```

### From Indexer Code
```csharp
// Batch embeddings
var embeddings = await _aiService.EmbedBatchAsync(chunkTexts, ct);
```

### From Admin Scripts
```bash
# Get current config
curl -H "Authorization: Bearer $ADMIN_TOKEN" \
  http://localhost:5000/admin/ai/config

# Update config
curl -X PUT \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d @ai-config.json \
  http://localhost:5000/admin/ai/config

# Check embedding change impact
curl -X POST \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"newDimensions": 768}' \
  http://localhost:5000/admin/ai/check-embedding-change
```

## Benefits

1. **Provider Flexibility**: Use any OpenAI-compatible API (OpenAI, Azure, local models)
2. **Cost Control**: Eco strategy minimizes costs, optional Micro tier for cheap operations
3. **Quality Control**: Turbo strategy maximizes quality, Full tier for complex tasks
4. **Operational Safety**: Embedding change warnings prevent accidental data loss
5. **Runtime Configuration**: Change models without redeployment via admin API
6. **Automatic Fallback**: System continues working if specific models fail
7. **No Vendor Lock-in**: Generic HTTP client works with any provider
8. **Production Ready**: Clean architecture, no backward compatibility cruft
