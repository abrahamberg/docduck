using Npgsql;
using System.Text.Json;

namespace DocDuck.Providers.Ai;

/// <summary>
/// Handles persistence of AI provider configuration (currently OpenAI).
/// </summary>
public sealed class AiProviderSettingsStore
{
    private readonly string _connectionString;

    public AiProviderSettingsStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<OpenAiProviderSettings?> GetOpenAiAsync(CancellationToken ct = default)
    {
        const string sql = "SELECT settings FROM ai_provider_settings WHERE provider_type = @provider_type";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("provider_type", OpenAiProviderSettings.ProviderType);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        using var payload = reader.GetFieldValue<JsonDocument>(0);
        return payload.RootElement.Deserialize<OpenAiProviderSettings>(Configuration.ConfigurationJson.Default);
    }

    public async Task UpsertAsync(OpenAiProviderSettings settings, CancellationToken ct = default)
    {
        settings.Validate();

        const string sql = @"
INSERT INTO ai_provider_settings(provider_type, settings, updated_at)
VALUES (@provider_type, @settings, now())
ON CONFLICT (provider_type)
DO UPDATE SET settings = EXCLUDED.settings, updated_at = now();";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("provider_type", OpenAiProviderSettings.ProviderType);
    var payload = JsonSerializer.Serialize(settings, Configuration.ConfigurationJson.Default);
    cmd.Parameters.Add("settings", NpgsqlTypes.NpgsqlDbType.Jsonb).Value = payload;

    await cmd.ExecuteNonQueryAsync(ct);
    }
}
