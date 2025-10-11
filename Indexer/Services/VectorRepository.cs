using Indexer.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector;

namespace Indexer.Services;

/// <summary>
/// Repository for inserting and managing vector embeddings in PostgreSQL with pgvector.
/// </summary>
public class VectorRepository
{
    private readonly DbOptions _options;
    private readonly ILogger<VectorRepository> _logger;

    public VectorRepository(IOptions<DbOptions> options, ILogger<VectorRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Inserts or updates chunks with embeddings for a document.
    /// Uses upsert on (doc_id, chunk_num) to ensure idempotency.
    /// </summary>
    public async Task InsertOrUpsertChunksAsync(
        IEnumerable<ChunkRecord> records,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        var recordList = records.ToList();
        if (recordList.Count == 0)
        {
            _logger.LogDebug("No records to insert");
            return;
        }

        await using var conn = new NpgsqlConnection(_options.ConnectionString);
        await conn.OpenAsync(ct);

        const string sql = @"
            INSERT INTO docs_chunks (doc_id, filename, chunk_num, text, metadata, embedding)
            VALUES (@doc_id, @filename, @chunk_num, @text, @metadata::jsonb, @embedding)
            ON CONFLICT (doc_id, chunk_num)
            DO UPDATE SET
                filename = EXCLUDED.filename,
                text = EXCLUDED.text,
                metadata = EXCLUDED.metadata,
                embedding = EXCLUDED.embedding,
                created_at = now()";

        var insertedCount = 0;

        foreach (var record in recordList)
        {
            ct.ThrowIfCancellationRequested();

            await using var cmd = new NpgsqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("doc_id", record.DocId);
            cmd.Parameters.AddWithValue("filename", record.Filename);
            cmd.Parameters.AddWithValue("chunk_num", record.Chunk.ChunkNum);
            cmd.Parameters.AddWithValue("text", record.Chunk.Text);
            cmd.Parameters.AddWithValue("metadata", record.Metadata.RootElement.GetRawText());
            cmd.Parameters.AddWithValue("embedding", new Vector(record.Embedding));

            await cmd.ExecuteNonQueryAsync(ct);
            insertedCount++;
        }

        _logger.LogInformation("Upserted {Count} chunks to database", insertedCount);
    }

    /// <summary>
    /// Checks if a document has already been indexed by comparing ETag.
    /// </summary>
    public async Task<bool> IsDocumentIndexedAsync(string docId, string etag, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(docId);
        ArgumentNullException.ThrowIfNull(etag);

        await using var conn = new NpgsqlConnection(_options.ConnectionString);
        await conn.OpenAsync(ct);

        const string sql = @"
            SELECT EXISTS(
                SELECT 1 FROM docs_files
                WHERE doc_id = @doc_id AND etag = @etag
            )";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("doc_id", docId);
        cmd.Parameters.AddWithValue("etag", etag);

        var exists = await cmd.ExecuteScalarAsync(ct);
        return exists is bool b && b;
    }

    /// <summary>
    /// Updates the file tracking table with latest ETag and modified timestamp.
    /// </summary>
    public async Task UpdateFileTrackingAsync(
        string docId,
        string filename,
        string etag,
        DateTimeOffset lastModified,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(docId);
        ArgumentNullException.ThrowIfNull(filename);
        ArgumentNullException.ThrowIfNull(etag);

        await using var conn = new NpgsqlConnection(_options.ConnectionString);
        await conn.OpenAsync(ct);

        const string sql = @"
            INSERT INTO docs_files (doc_id, filename, etag, last_modified)
            VALUES (@doc_id, @filename, @etag, @last_modified)
            ON CONFLICT (doc_id)
            DO UPDATE SET
                filename = EXCLUDED.filename,
                etag = EXCLUDED.etag,
                last_modified = EXCLUDED.last_modified";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("doc_id", docId);
        cmd.Parameters.AddWithValue("filename", filename);
        cmd.Parameters.AddWithValue("etag", etag);
        cmd.Parameters.AddWithValue("last_modified", lastModified);

        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogDebug("Updated file tracking for {DocId}", docId);
    }
}
