using Npgsql;
using System.Text.Json;

namespace DocDuck.Providers.Ai;

/// <summary>
/// Handles persistence of the new model-agnostic AI configuration.
/// Each AI provider (chat model or embedding model) is stored as a separate row in ai_provider_settings.
/// Test status fields are stored in dedicated columns rather than in the JSONB settings.
/// </summary>
public sealed class AiProviderConfigurationStore
{
    private readonly string _connectionString;
    private const string GlobalConfigKey = "global_config"; // Global settings like Enabled, DefaultSelectionStrategy, tier assignments, etc.
    private const string ProviderIdParam = "provider_id";

    public AiProviderConfigurationStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<AiProviderConfiguration?> GetAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var config = await GetGlobalConfigAsync(conn, ct);
        if (config == null)
        {
            return null;
        }

        await LoadChatModelsAsync(conn, config, ct);
        await LoadEmbeddingModelsAsync(conn, config, ct);

        return config;
    }

    private static async Task<AiProviderConfiguration?> GetGlobalConfigAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        const string globalSql = "SELECT settings FROM ai_provider_settings WHERE provider_id = @provider_id";
        await using var globalCmd = new NpgsqlCommand(globalSql, conn);
        globalCmd.Parameters.AddWithValue(ProviderIdParam, GlobalConfigKey);

        await using var reader = await globalCmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        using var payload = await reader.GetFieldValueAsync<JsonDocument>(0, ct);
        return payload.RootElement.Deserialize<AiProviderConfiguration>(Configuration.ConfigurationJson.Default);
    }

    private static async Task LoadChatModelsAsync(NpgsqlConnection conn, AiProviderConfiguration config, CancellationToken ct)
    {
        await AiModelLoader.LoadChatModelsAsync(conn, config, ct);
    }

    private static async Task LoadEmbeddingModelsAsync(NpgsqlConnection conn, AiProviderConfiguration config, CancellationToken ct)
    {
        await AiModelLoader.LoadEmbeddingModelsAsync(conn, config, ct);
    }

    public async Task UpsertAsync(AiProviderConfiguration config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Use a transaction to ensure atomicity
        await using var transaction = await conn.BeginTransactionAsync(ct);

        try
        {
            await UpsertGlobalConfigAsync(conn, transaction, config, ct);
            await UpsertChatModelsAsync(conn, transaction, config, ct);
            await UpsertEmbeddingModelsAsync(conn, transaction, config, ct);
            await DeleteRemovedModelsAsync(conn, transaction, config, ct);

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private static async Task UpsertGlobalConfigAsync(NpgsqlConnection conn, NpgsqlTransaction transaction, AiProviderConfiguration config, CancellationToken ct)
    {
        var globalConfig = new
        {
            config.Enabled,
            config.DefaultSelectionStrategy,
            config.MicroModelId,
            config.MiniModelId,
            config.FullModelId,
            config.ActiveEmbeddingModelId
        };

        const string globalSql = @"
            INSERT INTO ai_provider_settings(provider_id, provider_type, settings, updated_at)
            VALUES (@provider_id, 'global', @settings, now())
            ON CONFLICT (provider_id)
            DO UPDATE SET settings = EXCLUDED.settings, updated_at = now();";

        await using var cmd = new NpgsqlCommand(globalSql, conn, transaction);
        cmd.Parameters.AddWithValue(ProviderIdParam, GlobalConfigKey);
        var payload = JsonSerializer.Serialize(globalConfig, Configuration.ConfigurationJson.Default);
        cmd.Parameters.Add("settings", NpgsqlTypes.NpgsqlDbType.Jsonb).Value = payload;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task UpsertChatModelsAsync(NpgsqlConnection conn, NpgsqlTransaction transaction, AiProviderConfiguration config, CancellationToken ct)
    {
        const string chatSql = @"
            INSERT INTO ai_provider_settings(
                provider_id, provider_type, settings, test_status, last_tested_at, last_test_message,
                url, headers, request_template, response_mapping, default_params, updated_at
            )
            VALUES (
                @provider_id, 'chat', @settings, @test_status, @last_tested_at, @last_test_message,
                @url, @headers, @request_template, @response_mapping, @default_params, now()
            )
            ON CONFLICT (provider_id)
            DO UPDATE SET
                settings = EXCLUDED.settings,
                test_status = EXCLUDED.test_status,
                last_tested_at = EXCLUDED.last_tested_at,
                last_test_message = EXCLUDED.last_test_message,
                url = EXCLUDED.url,
                headers = EXCLUDED.headers,
                request_template = EXCLUDED.request_template,
                response_mapping = EXCLUDED.response_mapping,
                default_params = EXCLUDED.default_params,
                updated_at = now();";

        foreach (var model in config.ModelRegistry)
        {
            await using var cmd = new NpgsqlCommand(chatSql, conn, transaction);
            cmd.Parameters.AddWithValue(ProviderIdParam, model.Id);
            cmd.Parameters.AddWithValue("test_status", model.TestStatus.ToString());
            cmd.Parameters.AddWithValue("last_tested_at", (object?)model.LastTestedAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("last_test_message", (object?)model.LastTestMessage ?? DBNull.Value);

            // Store new flexible columns
            cmd.Parameters.AddWithValue("url", (object?)model.Url ?? DBNull.Value);

            if (model.Headers != null)
                cmd.Parameters.Add("headers", NpgsqlTypes.NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(model.Headers, Configuration.ConfigurationJson.Default);
            else
                cmd.Parameters.AddWithValue("headers", DBNull.Value);

            if (model.RequestTemplate != null)
                cmd.Parameters.Add("request_template", NpgsqlTypes.NpgsqlDbType.Jsonb).Value = model.RequestTemplate.RootElement.GetRawText();
            else
                cmd.Parameters.AddWithValue("request_template", DBNull.Value);

            if (model.ResponseMapping != null)
                cmd.Parameters.Add("response_mapping", NpgsqlTypes.NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(model.ResponseMapping, Configuration.ConfigurationJson.Default);
            else
                cmd.Parameters.AddWithValue("response_mapping", DBNull.Value);

            if (model.DefaultParams != null)
                cmd.Parameters.Add("default_params", NpgsqlTypes.NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(model.DefaultParams, Configuration.ConfigurationJson.Default);
            else
                cmd.Parameters.AddWithValue("default_params", DBNull.Value);

            // Store model settings without test status fields (those are now columns)
            // Keep backward-compatible fields in settings JSONB for now
            var modelSettings = new
            {
                model.DisplayName,
                model.ModelId,
                model.MaxContextTokens,
                model.MaxOutputTokens,
                model.SupportsFunctionCalling,
                model.CostFactor,
                model.Enabled,
                model.TimeoutSeconds
            };

            var payload = JsonSerializer.Serialize(modelSettings, Configuration.ConfigurationJson.Default);
            cmd.Parameters.Add("settings", NpgsqlTypes.NpgsqlDbType.Jsonb).Value = payload;
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task UpsertEmbeddingModelsAsync(NpgsqlConnection conn, NpgsqlTransaction transaction, AiProviderConfiguration config, CancellationToken ct)
    {
        const string embeddingSql = @"
            INSERT INTO ai_provider_settings(provider_id, provider_type, url, headers, request_template, response_mapping, default_params, settings, test_status, last_tested_at, last_test_message, updated_at)
            VALUES (@provider_id, 'embedding', @url, @headers, @request_template, @response_mapping, @default_params, @settings, @test_status, @last_tested_at, @last_test_message, now())
            ON CONFLICT (provider_id)
            DO UPDATE SET
                url = EXCLUDED.url,
                headers = EXCLUDED.headers,
                request_template = EXCLUDED.request_template,
                response_mapping = EXCLUDED.response_mapping,
                default_params = EXCLUDED.default_params,
                settings = EXCLUDED.settings,
                test_status = EXCLUDED.test_status,
                last_tested_at = EXCLUDED.last_tested_at,
                last_test_message = EXCLUDED.last_test_message,
                updated_at = now();";

        foreach (var model in config.EmbeddingRegistry)
        {
            await using var cmd = new NpgsqlCommand(embeddingSql, conn, transaction);
            cmd.Parameters.AddWithValue(ProviderIdParam, model.Id);
            cmd.Parameters.AddWithValue("test_status", model.TestStatus.ToString());
            cmd.Parameters.AddWithValue("last_tested_at", (object?)model.LastTestedAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("last_test_message", (object?)model.LastTestMessage ?? DBNull.Value);

            // New flexible fields
            cmd.Parameters.AddWithValue("url", model.Url);

            var headersJson = JsonSerializer.Serialize(model.Headers, Configuration.ConfigurationJson.Default);
            cmd.Parameters.Add("headers", NpgsqlTypes.NpgsqlDbType.Jsonb).Value = headersJson;

            var templateJson = model.RequestTemplate != null
                ? model.RequestTemplate.RootElement.GetRawText()
                : "null";
            cmd.Parameters.Add("request_template", NpgsqlTypes.NpgsqlDbType.Jsonb).Value = templateJson;

            var responseMappingJson = JsonSerializer.Serialize(model.ResponseMapping, Configuration.ConfigurationJson.Default);
            cmd.Parameters.Add("response_mapping", NpgsqlTypes.NpgsqlDbType.Jsonb).Value = responseMappingJson;

            var defaultParamsJson = JsonSerializer.Serialize(model.DefaultParams, Configuration.ConfigurationJson.Default);
            cmd.Parameters.Add("default_params", NpgsqlTypes.NpgsqlDbType.Jsonb).Value = defaultParamsJson;

            // Store embedding-specific settings
            var embeddingSettings = new
            {
                model.DisplayName,
                model.ModelId,
                model.Dimensions,
                model.BatchSize,
                model.Enabled,
                model.TimeoutSeconds
            };

            var payload = JsonSerializer.Serialize(embeddingSettings, Configuration.ConfigurationJson.Default);
            cmd.Parameters.Add("settings", NpgsqlTypes.NpgsqlDbType.Jsonb).Value = payload;
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task DeleteRemovedModelsAsync(NpgsqlConnection conn, NpgsqlTransaction transaction, AiProviderConfiguration config, CancellationToken ct)
    {
        const string deleteChatSql = @"
            DELETE FROM ai_provider_settings
            WHERE provider_type = 'chat'
            AND provider_id NOT IN (SELECT unnest(@ids::text[]))";

        await using (var cmd = new NpgsqlCommand(deleteChatSql, conn, transaction))
        {
            cmd.Parameters.AddWithValue("ids", config.ModelRegistry.Select(m => m.Id).ToArray());
            await cmd.ExecuteNonQueryAsync(ct);
        }

        const string deleteEmbeddingSql = @"
            DELETE FROM ai_provider_settings
            WHERE provider_type = 'embedding'
            AND provider_id NOT IN (SELECT unnest(@ids::text[]))";

        await using (var cmd = new NpgsqlCommand(deleteEmbeddingSql, conn, transaction))
        {
            cmd.Parameters.AddWithValue("ids", config.EmbeddingRegistry.Select(m => m.Id).ToArray());
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>
    /// Delete the AI configuration (disables AI features).
    /// </summary>
    public async Task DeleteAsync(CancellationToken ct = default)
    {
        const string sql = "DELETE FROM ai_provider_settings";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
