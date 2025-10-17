using DocDuck.Providers.Providers.Settings;
using Npgsql;
using System.Text.Json;

namespace DocDuck.Providers.Configuration;

/// <summary>
/// Direct database access for provider settings. Keeps things simple until we adopt a full migration framework.
/// </summary>
public sealed class ProviderSettingsStore
{
    private readonly string _connectionString;

    public ProviderSettingsStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<ProviderSettingsRecord>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = "SELECT provider_type, provider_name, settings, updated_at FROM provider_settings";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var results = new List<ProviderSettingsRecord>();
        while (await reader.ReadAsync(ct))
        {
            var payload = reader.GetFieldValue<JsonDocument>(2);
            results.Add(new ProviderSettingsRecord
            {
                ProviderType = reader.GetString(0),
                ProviderName = reader.GetString(1),
                Payload = payload,
                UpdatedAt = reader.GetFieldValue<DateTimeOffset>(3)
            });
        }

        return results;
    }

    public async Task<ProviderSettingsRecord?> GetAsync(string providerType, string providerName, CancellationToken ct = default)
    {
        const string sql = @"SELECT provider_type, provider_name, settings, updated_at
FROM provider_settings
WHERE provider_type = @type AND provider_name = @name";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("type", providerType);
        cmd.Parameters.AddWithValue("name", providerName);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var payload = reader.GetFieldValue<JsonDocument>(2);
        return new ProviderSettingsRecord
        {
            ProviderType = reader.GetString(0),
            ProviderName = reader.GetString(1),
            Payload = payload,
            UpdatedAt = reader.GetFieldValue<DateTimeOffset>(3)
        };
    }

    public async Task UpsertAsync(IProviderSettings settings, CancellationToken ct = default)
    {
        const string sql = @"
INSERT INTO provider_settings(provider_type, provider_name, settings, updated_at)
VALUES (@type, @name, @settings, now())
ON CONFLICT (provider_type, provider_name)
DO UPDATE SET settings = EXCLUDED.settings, updated_at = now();";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("type", settings.ProviderType);
        cmd.Parameters.AddWithValue("name", settings.Name);
        cmd.Parameters.AddWithValue("settings", JsonSerializer.SerializeToDocument(settings, settings.GetType(), ConfigurationJson.Default));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(string providerType, string providerName, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM provider_settings WHERE provider_type = @type AND provider_name = @name";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("type", providerType);
        cmd.Parameters.AddWithValue("name", providerName);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
