using Microsoft.Extensions.Logging;
using Npgsql;

namespace DocDuck.Providers.Configuration;

/// <summary>
/// Ensures the database tables required for provider settings exist.
/// </summary>
public sealed class ProviderSchemaInitializer
{
    private readonly string _connectionString;
    private readonly ILogger<ProviderSchemaInitializer> _logger;

    private const string Sql = @"
CREATE TABLE IF NOT EXISTS provider_settings (
    provider_type TEXT NOT NULL,
    provider_name TEXT NOT NULL,
    settings JSONB NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (provider_type, provider_name)
);

CREATE TABLE IF NOT EXISTS ai_provider_settings (
    provider_type TEXT PRIMARY KEY,
    settings JSONB NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS admin_users (
    id UUID PRIMARY KEY,
    username TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    is_admin BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS admin_users_username_lower_idx ON admin_users ((LOWER(username)));
";

    public ProviderSchemaInitializer(string connectionString, ILogger<ProviderSchemaInitializer> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Advisory lock prevents concurrent schema initialization races between services.
        await using var lockCmd = new NpgsqlCommand("SELECT pg_advisory_lock(@lockId);", conn);
        lockCmd.Parameters.AddWithValue("lockId", 6145798500635220170L); // arbitrary stable value to avoid DDL races
        await lockCmd.ExecuteNonQueryAsync(ct);

        try
        {
            await using var cmd = new NpgsqlCommand(Sql, conn);
            await cmd.ExecuteNonQueryAsync(ct);

            _logger.LogInformation("Provider settings schema ensured");
        }
        finally
        {
            await using var unlockCmd = new NpgsqlCommand("SELECT pg_advisory_unlock(@lockId);", conn);
            unlockCmd.Parameters.AddWithValue("lockId", 6145798500635220170L);
            await unlockCmd.ExecuteNonQueryAsync(ct);
        }
    }
}
