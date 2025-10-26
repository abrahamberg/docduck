using Npgsql;
using System.Text.Json;
using DocDuck.Providers.Configuration;

namespace DocDuck.Providers.Ai;

/// <summary>
/// Handles loading AI models from database rows.
/// Reduces complexity in AiProviderConfigurationStore by separating data loading logic.
/// </summary>
internal static class AiModelLoader
{
    public static async Task LoadChatModelsAsync(
        NpgsqlConnection conn,
        AiProviderConfiguration config,
        CancellationToken ct)
    {
        const string chatSql = @"
            SELECT provider_id, settings, test_status, last_tested_at, last_test_message,
                   url, headers, request_template, response_mapping, default_params
            FROM ai_provider_settings 
            WHERE provider_type = 'chat'";

        await using var chatCmd = new NpgsqlCommand(chatSql, conn);
        await using var reader = await chatCmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var model = await ReadChatModelFromReaderAsync(reader, ct);
            if (model != null)
            {
                config.ModelRegistry.Add(model);
            }
        }
    }

    public static async Task LoadEmbeddingModelsAsync(
        NpgsqlConnection conn,
        AiProviderConfiguration config,
        CancellationToken ct)
    {
        const string embeddingSql = @"
            SELECT provider_id, url, headers, request_template, response_mapping, default_params, settings, test_status, last_tested_at, last_test_message 
            FROM ai_provider_settings 
            WHERE provider_type = 'embedding'";

        await using var embeddingCmd = new NpgsqlCommand(embeddingSql, conn);
        await using var reader = await embeddingCmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var embedding = await ReadEmbeddingModelFromReaderAsync(reader, ct);
            if (embedding != null)
            {
                config.EmbeddingRegistry.Add(embedding);
            }
        }
    }

    private static async Task<AiModelAssignment?> ReadChatModelFromReaderAsync(
        NpgsqlDataReader reader,
        CancellationToken ct)
    {
        var providerId = reader.GetString(0);
        using var payload = await reader.GetFieldValueAsync<JsonDocument>(1, ct);
        var model = payload.RootElement.Deserialize<AiModelAssignment>(ConfigurationJson.Default);

        if (model == null)
        {
            return null;
        }

        model.Id = providerId;
        model.TestStatus = Enum.Parse<ModelTestStatus>(reader.GetString(2));
        model.LastTestedAt = await GetNullableDateTimeOffsetAsync(reader, 3, ct);
        model.LastTestMessage = await GetNullableStringAsync(reader, 4, ct);

        await PopulateFlexibleFieldsAsync(reader, model, ct);

        return model;
    }

    private static async Task<AiEmbeddingModelAssignment?> ReadEmbeddingModelFromReaderAsync(
        NpgsqlDataReader reader,
        CancellationToken ct)
    {
        var providerId = reader.GetString(0);
        var url = reader.GetString(1);

        using var headersDoc = await reader.GetFieldValueAsync<JsonDocument>(2, ct);
        var headers = headersDoc.RootElement.Deserialize<Dictionary<string, string>>(ConfigurationJson.Default)
            ?? new Dictionary<string, string>();

        var requestTemplate = await GetNullableJsonDocumentAsync(reader, 3, ct);

        using var responseMappingDoc = await reader.GetFieldValueAsync<JsonDocument>(4, ct);
        var responseMapping = responseMappingDoc.RootElement.Deserialize<Dictionary<string, string>>(ConfigurationJson.Default)
            ?? new Dictionary<string, string>();

        using var defaultParamsDoc = await reader.GetFieldValueAsync<JsonDocument>(5, ct);
        var defaultParams = defaultParamsDoc.RootElement.Deserialize<Dictionary<string, object>>(ConfigurationJson.Default)
            ?? new Dictionary<string, object>();

        using var settingsPayload = await reader.GetFieldValueAsync<JsonDocument>(6, ct);
        var settings = settingsPayload.RootElement.Deserialize<AiEmbeddingModelAssignment>(ConfigurationJson.Default);

        if (settings == null)
        {
            return null;
        }

        settings.Id = providerId;
        settings.Url = url;
        settings.Headers = headers;
        settings.RequestTemplate = requestTemplate;
        settings.ResponseMapping = responseMapping;
        settings.DefaultParams = defaultParams;
        settings.TestStatus = Enum.Parse<ModelTestStatus>(reader.GetString(7));
        settings.LastTestedAt = await GetNullableDateTimeOffsetAsync(reader, 8, ct);
        settings.LastTestMessage = await GetNullableStringAsync(reader, 9, ct);

        return settings;
    }

    private static async Task PopulateFlexibleFieldsAsync(
        NpgsqlDataReader reader,
        AiModelAssignment model,
        CancellationToken ct)
    {
        // Load new flexible columns (indices 5-9)
        if (!await reader.IsDBNullAsync(5, ct))
        {
            model.Url = reader.GetString(5);
        }

        if (!await reader.IsDBNullAsync(6, ct))
        {
            using var headersDoc = await reader.GetFieldValueAsync<JsonDocument>(6, ct);
            model.Headers = headersDoc.RootElement.Deserialize<Dictionary<string, string>>(ConfigurationJson.Default)
                ?? new Dictionary<string, string>();
        }

        if (!await reader.IsDBNullAsync(7, ct))
        {
            var templateDoc = await reader.GetFieldValueAsync<JsonDocument>(7, ct);
            model.RequestTemplate = templateDoc;
        }

        if (!await reader.IsDBNullAsync(8, ct))
        {
            using var mappingDoc = await reader.GetFieldValueAsync<JsonDocument>(8, ct);
            model.ResponseMapping = mappingDoc.RootElement.Deserialize<ResponseMapping>(ConfigurationJson.Default);
        }

        if (!await reader.IsDBNullAsync(9, ct))
        {
            using var paramsDoc = await reader.GetFieldValueAsync<JsonDocument>(9, ct);
            model.DefaultParams = paramsDoc.RootElement.Deserialize<Dictionary<string, JsonElement>>(ConfigurationJson.Default)
                ?? new Dictionary<string, JsonElement>();
        }
    }

    private static async Task<DateTimeOffset?> GetNullableDateTimeOffsetAsync(
        NpgsqlDataReader reader,
        int ordinal,
        CancellationToken ct)
    {
        return await reader.IsDBNullAsync(ordinal, ct)
            ? null
            : await reader.GetFieldValueAsync<DateTimeOffset>(ordinal, ct);
    }

    private static async Task<string?> GetNullableStringAsync(
        NpgsqlDataReader reader,
        int ordinal,
        CancellationToken ct)
    {
        return await reader.IsDBNullAsync(ordinal, ct)
            ? null
            : reader.GetString(ordinal);
    }

    private static async Task<JsonDocument?> GetNullableJsonDocumentAsync(
        NpgsqlDataReader reader,
        int ordinal,
        CancellationToken ct)
    {
        if (await reader.IsDBNullAsync(ordinal, ct))
        {
            return null;
        }

        var templateDoc = await reader.GetFieldValueAsync<JsonDocument>(ordinal, ct);
        return templateDoc.RootElement.ValueKind != JsonValueKind.Null
            ? templateDoc
            : null;
    }
}
