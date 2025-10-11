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
    /// </summary>
    public async Task<List<Source>> SearchAsync(
        float[] queryEmbedding,
        int? topK = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(queryEmbedding);

        var k = Math.Min(
            topK ?? _searchOptions.DefaultTopK,
            _searchOptions.MaxTopK);

        _logger.LogDebug("Searching for top {K} similar chunks", k);

        await using var conn = new NpgsqlConnection(_dbOptions.ConnectionString);
        await conn.OpenAsync(ct);

        // Query using cosine distance operator (<=>)
        const string sql = @"
            SELECT 
                doc_id,
                filename,
                chunk_num,
                text,
                metadata,
                embedding <=> @embedding AS distance
            FROM docs_chunks
            ORDER BY embedding <=> @embedding
            LIMIT @limit";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("embedding", new Vector(queryEmbedding));
        cmd.Parameters.AddWithValue("limit", k);

        var results = new List<Source>();

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var docId = reader.GetString(0);
            var filename = reader.GetString(1);
            var chunkNum = reader.GetInt32(2);
            var text = reader.GetString(3);
            var metadataJson = reader.IsDBNull(4) ? null : reader.GetString(4);
            var distance = reader.GetDouble(5);

            // Generate citation
            var citation = $"[{filename}#chunk{chunkNum}]";

            results.Add(new Source(
                DocId: docId,
                Filename: filename,
                ChunkNum: chunkNum,
                Text: text,
                Distance: distance,
                Citation: citation
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
}
