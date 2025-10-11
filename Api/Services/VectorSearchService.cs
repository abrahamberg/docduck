using System.Text.Json;
using Api.Models;
using Api.Options;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector;

namespace Api.Services;

/// <summary>
/// Repository for vector similarity search against PostgreSQL + pgvector.
/// </summary>
public class VectorSearchService
{
    private readonly DbOptions _dbOptions;
    private readonly SearchOptions _searchOptions;
    private readonly ILogger<VectorSearchService> _logger;

    public VectorSearchService(
        IOptions<DbOptions> dbOptions,
        IOptions<SearchOptions> searchOptions,
        ILogger<VectorSearchService> logger)
    {
        ArgumentNullException.ThrowIfNull(dbOptions);
        ArgumentNullException.ThrowIfNull(searchOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _dbOptions = dbOptions.Value;
        _searchOptions = searchOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Search for similar chunks using vector similarity (cosine distance).
    /// Optionally filter by provider.
    /// </summary>
    public async Task<List<Source>> SearchAsync(
        float[] queryEmbedding,
        int? topK = null,
        string? providerType = null,
        string? providerName = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(queryEmbedding);

        var k = Math.Min(
            topK ?? _searchOptions.DefaultTopK,
            _searchOptions.MaxTopK);

        _logger.LogDebug("Searching for top {K} similar chunks (Provider: {Type}/{Name})", 
            k, providerType ?? "all", providerName ?? "all");

        await using var conn = new NpgsqlConnection(_dbOptions.ConnectionString);
        await conn.OpenAsync(ct);

        // Build SQL with optional provider filter
        var sql = @"
            SELECT 
                doc_id,
                filename,
                provider_type,
                provider_name,
                chunk_num,
                text,
                metadata,
                embedding <=> @embedding AS distance
            FROM docs_chunks";

        var whereConditions = new List<string>();
        if (!string.IsNullOrEmpty(providerType))
        {
            whereConditions.Add("provider_type = @provider_type");
        }
        if (!string.IsNullOrEmpty(providerName))
        {
            whereConditions.Add("provider_name = @provider_name");
        }

        if (whereConditions.Count > 0)
        {
            sql += $" WHERE {string.Join(" AND ", whereConditions)}";
        }

        sql += @"
            ORDER BY embedding <=> @embedding
            LIMIT @limit";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("embedding", new Vector(queryEmbedding));
        cmd.Parameters.AddWithValue("limit", k);
        
        if (!string.IsNullOrEmpty(providerType))
        {
            cmd.Parameters.AddWithValue("provider_type", providerType);
        }
        if (!string.IsNullOrEmpty(providerName))
        {
            cmd.Parameters.AddWithValue("provider_name", providerName);
        }

        var results = new List<Source>();

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var docId = reader.GetString(0);
            var filename = reader.GetString(1);
            var pType = reader.GetString(2);
            var pName = reader.GetString(3);
            var chunkNum = reader.GetInt32(4);
            var text = reader.GetString(5);
            var distance = reader.GetDouble(7);

            // Generate citation including provider info
            var citation = $"[{pType}/{pName}:{filename}#chunk{chunkNum}]";

            results.Add(new Source(
                DocId: docId,
                Filename: filename,
                ChunkNum: chunkNum,
                Text: text,
                Distance: distance,
                Citation: citation,
                ProviderType: pType,
                ProviderName: pName
            ));
        }

        _logger.LogInformation("Found {Count} similar chunks", results.Count);

        return results;
    }

    /// <summary>
    /// Get total count of indexed chunks.
    /// </summary>
    public async Task<long> GetChunkCountAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_dbOptions.ConnectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM docs_chunks", conn);
        var count = (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);

        return count;
    }

    /// <summary>
    /// Get count of indexed documents.
    /// </summary>
    public async Task<long> GetDocumentCountAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_dbOptions.ConnectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(DISTINCT doc_id) FROM docs_chunks", 
            conn);
        var count = (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);

        return count;
    }

    /// <summary>
    /// Get list of all registered providers.
    /// </summary>
    public async Task<List<Api.Models.ProviderInfo>> GetProvidersAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_dbOptions.ConnectionString);
        await conn.OpenAsync(ct);

        const string sql = @"
            SELECT 
                provider_type,
                provider_name,
                is_enabled,
                registered_at,
                last_sync_at,
                metadata
            FROM providers
            ORDER BY provider_type, provider_name";

        await using var cmd = new NpgsqlCommand(sql, conn);
        var providers = new List<Api.Models.ProviderInfo>();

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var providerType = reader.GetString(0);
            var providerName = reader.GetString(1);
            var isEnabled = reader.GetBoolean(2);
            var registeredAt = reader.GetDateTime(3);
            var lastSyncAt = await reader.IsDBNullAsync(4, ct) ? null : (DateTimeOffset?)reader.GetDateTime(4);
            var metadataJson = await reader.IsDBNullAsync(5, ct) ? null : reader.GetString(5);

            Dictionary<string, string>? metadata = null;
            if (!string.IsNullOrEmpty(metadataJson))
            {
                metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson);
            }

            providers.Add(new Api.Models.ProviderInfo(
                ProviderType: providerType,
                ProviderName: providerName,
                IsEnabled: isEnabled,
                RegisteredAt: registeredAt,
                LastSyncAt: lastSyncAt,
                Metadata: metadata
            ));
        }

        return providers;
    }
}
