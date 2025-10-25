-- =============================================================================
-- Migration: Flexible AI Model Configuration
-- =============================================================================
-- This migration transforms the AI configuration system to support:
-- - Generic request/response templates for any OpenAI-compatible API
-- - Per-model parameter configuration (temperature, etc.)
-- - Auto-detected response structure mapping
-- - cURL import capability
--
-- BREAKING CHANGE: This removes DefaultTemperature and RefineSystemPrompt
-- from ai_provider_settings as these are now managed differently:
-- - Temperature is per-model in default_params
-- - System prompts are hardcoded in application code (SystemPrompts class)
-- =============================================================================

BEGIN;

-- Step 1: Create backup of existing ai_provider_settings
CREATE TABLE IF NOT EXISTS ai_provider_settings_backup AS 
SELECT * FROM ai_provider_settings;

-- Step 2: Add new columns to ai_provider_settings for flexible model config
-- These will be populated from existing ModelRegistry entries

-- Add url column (extracted from BaseUrl + endpoint)
ALTER TABLE ai_provider_settings 
ADD COLUMN IF NOT EXISTS url TEXT;

-- Add headers column (JSON object for HTTP headers)
ALTER TABLE ai_provider_settings 
ADD COLUMN IF NOT EXISTS headers JSONB DEFAULT '{"Content-Type": "application/json"}'::jsonb;

-- Add request_template column (JSON template with variable placeholders)
ALTER TABLE ai_provider_settings 
ADD COLUMN IF NOT EXISTS request_template JSONB;

-- Add response_mapping column (JSONPath expressions for extracting data)
ALTER TABLE ai_provider_settings 
ADD COLUMN IF NOT EXISTS response_mapping JSONB;

-- Add default_params column (model-specific parameters like temperature)
ALTER TABLE ai_provider_settings 
ADD COLUMN IF NOT EXISTS default_params JSONB DEFAULT '{}'::jsonb;

-- Step 3: Migrate existing data
-- For chat models (provider_type = 'chat'), extract model configurations from settings JSONB
UPDATE ai_provider_settings
SET 
    -- Default to OpenAI chat completions endpoint structure
    request_template = jsonb_build_object(
        'model', '{MODEL_ID}',
        'messages', '{MESSAGES}',
        'temperature', '{TEMPERATURE}',
        'max_tokens', '{MAX_TOKENS}',
        'stream', false
    ),
    
    -- Default OpenAI response mapping
    response_mapping = jsonb_build_object(
        'contentPath', 'choices[0].message.content',
        'rolePath', 'choices[0].message.role',
        'toolCallsPath', 'choices[0].message.tool_calls',
        'usagePromptTokensPath', 'usage.prompt_tokens',
        'usageCompletionTokensPath', 'usage.completion_tokens',
        'usageTotalTokensPath', 'usage.total_tokens',
        'autoDetected', false,
        'detectedAt', NOW()
    ),
    
    -- Migrate DefaultTemperature from settings to default_params
    default_params = jsonb_build_object(
        'temperature', COALESCE((settings->>'DefaultTemperature')::float, 0.7)
    )
WHERE provider_type = 'chat';

-- Step 4: Build URL from BaseUrl (extracted from ModelRegistry entries in settings)
-- This is a best-effort migration; manual review may be needed
UPDATE ai_provider_settings
SET url = COALESCE(
    settings->'ModelRegistry'->0->>'BaseUrl', 
    'https://api.openai.com/v1'
) || '/chat/completions'
WHERE provider_type = 'chat' AND url IS NULL;

-- Step 5: Extract API key into headers (Authorization: Bearer {key})
UPDATE ai_provider_settings
SET headers = jsonb_build_object(
    'Content-Type', 'application/json',
    'Authorization', 'Bearer ' || COALESCE(
        settings->'ModelRegistry'->0->>'ApiKey',
        ''
    )
)
WHERE provider_type = 'chat' 
  AND settings->'ModelRegistry'->0->>'ApiKey' IS NOT NULL
  AND settings->'ModelRegistry'->0->>'ApiKey' != '';

-- Step 6: For embedding models, similar migration
UPDATE ai_provider_settings
SET 
    url = COALESCE(
        settings->'EmbeddingRegistry'->0->>'BaseUrl', 
        'https://api.openai.com/v1'
    ) || '/embeddings',
    
    request_template = jsonb_build_object(
        'model', '{MODEL_ID}',
        'input', '{INPUT}'
    ),
    
    response_mapping = jsonb_build_object(
        'embeddingPath', 'data[0].embedding',
        'autoDetected', false,
        'detectedAt', NOW()
    ),
    
    default_params = '{}'::jsonb
WHERE provider_type = 'embedding';

-- Step 7: Add indexes for new columns
CREATE INDEX IF NOT EXISTS ai_provider_settings_url_idx 
ON ai_provider_settings(url);

CREATE INDEX IF NOT EXISTS ai_provider_settings_headers_idx 
ON ai_provider_settings USING GIN (headers);

CREATE INDEX IF NOT EXISTS ai_provider_settings_default_params_idx 
ON ai_provider_settings USING GIN (default_params);

-- Step 8: Document the migration
DO $$
BEGIN
    RAISE NOTICE '=============================================================================';
    RAISE NOTICE 'Flexible AI Model Migration Complete';
    RAISE NOTICE '=============================================================================';
    RAISE NOTICE 'Changes:';
    RAISE NOTICE '- Added: url, headers, request_template, response_mapping, default_params columns';
    RAISE NOTICE '- Migrated: DefaultTemperature → default_params.temperature';
    RAISE NOTICE '- Removed (app-level): RefineSystemPrompt (now in SystemPrompts.Refine constant)';
    RAISE NOTICE '';
    RAISE NOTICE 'Action Required:';
    RAISE NOTICE '1. Review migrated URLs and headers for accuracy';
    RAISE NOTICE '2. Test model configurations via admin API';
    RAISE NOTICE '3. System prompts are now managed in code (src/Providers.Shared/Ai/SystemPrompts.cs)';
    RAISE NOTICE '=============================================================================';
END $$;

COMMIT;
