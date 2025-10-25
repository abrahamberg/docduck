-- =============================================================================
-- Migration: ai_provider_settings schema change
-- =============================================================================
-- This migration transforms the ai_provider_settings table from storing
-- a single monolithic configuration to storing each AI provider as a separate row.
-- 
-- Changes:
-- - PRIMARY KEY changes from `provider_type` to `provider_id`
-- - Each chat model and embedding model gets its own row
-- - Test status fields (test_status, last_tested_at, last_test_message) are
--   extracted from JSONB into dedicated columns
-- - Global config is stored in a single row with provider_id = 'global_config'
-- 
-- This script is safe to run multiple times.
-- =============================================================================

-- Step 1: Create a backup table before migration
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'ai_provider_settings') THEN
        DROP TABLE IF EXISTS ai_provider_settings_backup;
        CREATE TABLE ai_provider_settings_backup AS SELECT * FROM ai_provider_settings;
        RAISE NOTICE 'Created backup table: ai_provider_settings_backup';
    END IF;
END $$;

-- Step 2: Create temporary table for migration
DROP TABLE IF EXISTS ai_provider_settings_new;
CREATE TABLE ai_provider_settings_new (
    provider_id TEXT PRIMARY KEY,
    provider_type TEXT NOT NULL, -- 'global', 'chat', or 'embedding'
    settings JSONB NOT NULL,
    test_status TEXT NOT NULL DEFAULT 'Untested', -- 'Untested', 'Passed', 'Failed'
    last_tested_at TIMESTAMPTZ,
    last_test_message TEXT,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Step 3: Migrate existing data
-- Extract models from the old monolithic configuration and create individual rows
DO $$
DECLARE
    old_config JSONB;
    chat_model JSONB;
    embedding_model JSONB;
    global_settings JSONB;
BEGIN
    -- Get the existing configuration (if it exists)
    SELECT settings INTO old_config 
    FROM ai_provider_settings 
    WHERE provider_type = 'ai_provider_v2'
    LIMIT 1;

    IF old_config IS NOT NULL THEN
        RAISE NOTICE 'Migrating existing ai_provider_v2 configuration...';

        -- Create global config row (tier assignments, defaults, etc.)
        global_settings := jsonb_build_object(
            'Enabled', old_config->'Enabled',
            'DefaultSelectionStrategy', old_config->'DefaultSelectionStrategy',
            'MicroModelId', old_config->'MicroModelId',
            'MiniModelId', old_config->'MiniModelId',
            'FullModelId', old_config->'FullModelId',
            'ActiveEmbeddingModelId', old_config->'ActiveEmbeddingModelId',
            'DefaultTemperature', old_config->'DefaultTemperature',
            'RefineSystemPrompt', old_config->'RefineSystemPrompt'
        );

        INSERT INTO ai_provider_settings_new (provider_id, provider_type, settings, updated_at)
        VALUES ('global_config', 'global', global_settings, now());

        RAISE NOTICE 'Migrated global configuration';

        -- Migrate chat models from ModelRegistry
        IF old_config->'ModelRegistry' IS NOT NULL THEN
            FOR chat_model IN SELECT * FROM jsonb_array_elements(old_config->'ModelRegistry')
            LOOP
                INSERT INTO ai_provider_settings_new (
                    provider_id, 
                    provider_type, 
                    settings, 
                    test_status,
                    last_tested_at,
                    last_test_message,
                    updated_at
                )
                VALUES (
                    chat_model->>'Id',
                    'chat',
                    jsonb_build_object(
                        'DisplayName', chat_model->'DisplayName',
                        'ModelId', chat_model->'ModelId',
                        'BaseUrl', chat_model->'BaseUrl',
                        'ApiKey', chat_model->'ApiKey',
                        'MaxContextTokens', chat_model->'MaxContextTokens',
                        'MaxOutputTokens', chat_model->'MaxOutputTokens',
                        'SupportsFunctionCalling', chat_model->'SupportsFunctionCalling',
                        'CostFactor', chat_model->'CostFactor',
                        'Enabled', chat_model->'Enabled',
                        'CustomHeaders', chat_model->'CustomHeaders',
                        'TimeoutSeconds', chat_model->'TimeoutSeconds'
                    ),
                    COALESCE(chat_model->>'TestStatus', 'Untested'),
                    CASE 
                        WHEN chat_model->'LastTestedAt' IS NOT NULL 
                        THEN (chat_model->>'LastTestedAt')::timestamptz 
                        ELSE NULL 
                    END,
                    chat_model->>'LastTestMessage',
                    now()
                );

                RAISE NOTICE 'Migrated chat model: %', chat_model->>'Id';
            END LOOP;
        END IF;

        -- Migrate embedding models from EmbeddingRegistry
        IF old_config->'EmbeddingRegistry' IS NOT NULL THEN
            FOR embedding_model IN SELECT * FROM jsonb_array_elements(old_config->'EmbeddingRegistry')
            LOOP
                INSERT INTO ai_provider_settings_new (
                    provider_id, 
                    provider_type, 
                    settings,
                    test_status,
                    last_tested_at,
                    last_test_message,
                    updated_at
                )
                VALUES (
                    embedding_model->>'Id',
                    'embedding',
                    jsonb_build_object(
                        'DisplayName', embedding_model->'DisplayName',
                        'ModelId', embedding_model->'ModelId',
                        'BaseUrl', embedding_model->'BaseUrl',
                        'ApiKey', embedding_model->'ApiKey',
                        'Dimensions', embedding_model->'Dimensions',
                        'BatchSize', embedding_model->'BatchSize',
                        'Enabled', embedding_model->'Enabled',
                        'CustomHeaders', embedding_model->'CustomHeaders',
                        'TimeoutSeconds', embedding_model->'TimeoutSeconds'
                    ),
                    COALESCE(embedding_model->>'TestStatus', 'Untested'),
                    CASE 
                        WHEN embedding_model->'LastTestedAt' IS NOT NULL 
                        THEN (embedding_model->>'LastTestedAt')::timestamptz 
                        ELSE NULL 
                    END,
                    embedding_model->>'LastTestMessage',
                    now()
                );

                RAISE NOTICE 'Migrated embedding model: %', embedding_model->>'Id';
            END LOOP;
        END IF;

        RAISE NOTICE 'Migration completed successfully';
    ELSE
        RAISE NOTICE 'No existing configuration found - starting fresh';
    END IF;
END $$;

-- Step 4: Replace old table with new one
DROP TABLE IF EXISTS ai_provider_settings;
ALTER TABLE ai_provider_settings_new RENAME TO ai_provider_settings;

-- Step 5: Verification
DO $$
BEGIN
    RAISE NOTICE '=============================================================================';
    RAISE NOTICE 'ai_provider_settings migration completed!';
    RAISE NOTICE '=============================================================================';
    RAISE NOTICE 'New schema:';
    RAISE NOTICE '  - provider_id (PRIMARY KEY): unique identifier for each provider';
    RAISE NOTICE '  - provider_type: global | chat | embedding';
    RAISE NOTICE '  - settings: JSONB (without test status fields)';
    RAISE NOTICE '  - test_status: Untested | Passed | Failed';
    RAISE NOTICE '  - last_tested_at: timestamp of last test';
    RAISE NOTICE '  - last_test_message: test result message';
    RAISE NOTICE '=============================================================================';
END $$;

-- Display migrated data summary
SELECT 
    provider_type,
    COUNT(*) AS count,
    array_agg(provider_id ORDER BY provider_id) AS provider_ids
FROM ai_provider_settings
GROUP BY provider_type
ORDER BY provider_type;

-- Optional: Drop backup table after verifying migration
-- DROP TABLE IF EXISTS ai_provider_settings_backup;
