using Microsoft.Extensions.Logging;
using Npgsql;

namespace Api.Admin;

public sealed class AdminUserStore
{
    private readonly string _connectionString;
    private readonly ILogger<AdminUserStore> _logger;

    public AdminUserStore(string connectionString, ILogger<AdminUserStore> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task EnsureDefaultAdminAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string countSql = "SELECT COUNT(*) FROM admin_users";
        await using (var countCmd = new NpgsqlCommand(countSql, conn))
        {
            var existing = (long)(await countCmd.ExecuteScalarAsync(ct) ?? 0L);
            if (existing > 0)
            {
                return;
            }
        }

        var passwordHash = PasswordHasher.Hash("admin");

        const string insertSql = @"
INSERT INTO admin_users (id, username, password_hash, is_admin)
VALUES (@id, @username, @password_hash, TRUE);
";

        await using (var cmd = new NpgsqlCommand(insertSql, conn))
        {
            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("username", "admin");
            cmd.Parameters.AddWithValue("password_hash", passwordHash);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        _logger.LogInformation("Default admin user seeded with username 'admin'. Prompt to change the password immediately.");
    }

    public async Task<AdminUser?> GetByUsernameAsync(string username, CancellationToken ct)
    {
    ArgumentException.ThrowIfNullOrEmpty(username);
    username = username.Trim();

        const string sql = @"
SELECT id, username, is_admin, created_at, updated_at, password_hash
FROM admin_users
WHERE LOWER(username) = LOWER(@username)
LIMIT 1;
";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("username", username);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapUser(reader);
        }

        return null;
    }

    public async Task<AdminUser?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        const string sql = @"
SELECT id, username, is_admin, created_at, updated_at, password_hash
FROM admin_users
WHERE id = @id
LIMIT 1;
";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapUser(reader);
        }

        return null;
    }

    public async Task<IReadOnlyList<AdminUser>> GetUsersAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT id, username, is_admin, created_at, updated_at
FROM admin_users
ORDER BY username;
";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);

        var results = new List<AdminUser>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapUserWithoutHash(reader));
        }

        return results;
    }

    public async Task<AdminUser> CreateUserAsync(string username, string password, bool isAdmin, CancellationToken ct)
    {
    ArgumentException.ThrowIfNullOrEmpty(username);
    ArgumentException.ThrowIfNullOrEmpty(password);
    username = username.Trim();

        const string sql = @"
INSERT INTO admin_users (id, username, password_hash, is_admin)
VALUES (@id, @username, @password_hash, @is_admin)
RETURNING id, username, is_admin, created_at, updated_at;
";

        var passwordHash = PasswordHasher.Hash(password);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        var id = Guid.NewGuid();
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("username", username);
        cmd.Parameters.AddWithValue("password_hash", passwordHash);
        cmd.Parameters.AddWithValue("is_admin", isAdmin);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            throw new InvalidOperationException("Failed to create admin user.");
        }

        return MapUserWithoutHash(reader);
    }

    public async Task<bool> TrySetPasswordAsync(Guid id, string password, CancellationToken ct)
    {
        const string sql = @"
UPDATE admin_users
SET password_hash = @password_hash, updated_at = now()
WHERE id = @id;
";

        var hash = PasswordHasher.Hash(password);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("password_hash", hash);

        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }

    public async Task<bool> TrySetAdminAsync(Guid id, bool isAdmin, CancellationToken ct)
    {
        const string sql = @"
UPDATE admin_users
SET is_admin = @is_admin, updated_at = now()
WHERE id = @id;
";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("is_admin", isAdmin);

        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }

    public async Task<int> CountAdminsAsync(CancellationToken ct)
    {
        const string sql = "SELECT COUNT(*) FROM admin_users WHERE is_admin = TRUE";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<AdminUser?> ValidateCredentialsAsync(string username, string password, CancellationToken ct)
    {
        var user = await GetByUsernameAsync(username, ct);
        if (user is null || user.PasswordHash is null)
        {
            return null;
        }

            if (!PasswordHasher.Verify(password, user.PasswordHash))
            {
                return null;
            }

            return new AdminUser(user.Id, user.Username, user.IsAdmin, user.CreatedAt, user.UpdatedAt, null);
    }

    private static AdminUser MapUser(NpgsqlDataReader reader)
    {
        return new AdminUser(
            reader.GetGuid(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("username")),
            reader.GetBoolean(reader.GetOrdinal("is_admin")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at")),
            reader.GetString(reader.GetOrdinal("password_hash")));
    }

    private static AdminUser MapUserWithoutHash(NpgsqlDataReader reader)
    {
        return new AdminUser(
            reader.GetGuid(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("username")),
            reader.GetBoolean(reader.GetOrdinal("is_admin")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at")),
            null);
    }
}

public sealed record AdminUser(
    Guid Id,
    string Username,
    bool IsAdmin,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? PasswordHash);
