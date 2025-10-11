using Indexer.Options;
using Indexer.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector;

namespace Indexer.Services;

/// <summary>
/// Repository for inserting and managing vector embeddings in PostgreSQL with pgvector.
/// Updated to support multiple document providers.
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
    /// Registers or updates a provider in the database.
    /// </summary>
    public async Task RegisterProviderAsync(ProviderMetadata metadata, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        await using var conn = new NpgsqlConnection(_options.ConnectionString);
        await conn.OpenAsync(ct);

        const string sql = @"
            INSERT INTO providers (provider_type, provider_name, is_enabled, registered_at, metadata)
            VALUES (@provider_type, @provider_name, @is_enabled, @registered_at, @metadata::jsonb)
            ON CONFLICT (provider_type, provider_name)
            DO UPDATE SET
                is_enabled = EXCLUDED.is_enabled,
                metadata = EXCLUDED.metadata";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("provider_type", metadata.ProviderType);
        cmd.Parameters.AddWithValue("provider_name", metadata.ProviderName);
        cmd.Parameters.AddWithValue("is_enabled", metadata.IsEnabled);
        cmd.Parameters.AddWithValue("registered_at", metadata.RegisteredAt);
        cmd.Parameters.AddWithValue("metadata", 
            System.Text.Json.JsonSerializer.Serialize(metadata.AdditionalInfo ?? new Dictionary<string, string>()));

        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Registered provider: {Type}/{Name}", metadata.ProviderType, metadata.ProviderName);
    }

    /// <summary>
    /// Updates last sync timestamp for a provider.
    /// </summary>
    public async Task UpdateProviderSyncTimeAsync(string providerType, string providerName, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_options.ConnectionString);
        await conn.OpenAsync(ct);

        const string sql = @"
            UPDATE providers
            SET last_sync_at = now()
            WHERE provider_type = @provider_type AND provider_name = @provider_name";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("provider_type", providerType);
        cmd.Parameters.AddWithValue("provider_name", providerName);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Inserts or updates chunks with embeddings for a document.
    /// Uses upsert on (doc_id, chunk_num) to ensure idempotency.
    /// </summary>
    public async Task InsertOrUpsertChunksAsync(
        IEnumerable<ChunkRecord> records,
        string providerType,
        string providerName,
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
            INSERT INTO docs_chunks (doc_id, filename, provider_type, provider_name, chunk_num, text, metadata, embedding)
            VALUES (@doc_id, @filename, @provider_type, @provider_name, @chunk_num, @text, @metadata::jsonb, @embedding)
            ON CONFLICT (doc_id, chunk_num)
            DO UPDATE SET
                filename = EXCLUDED.filename,
                provider_type = EXCLUDED.provider_type,
                provider_name = EXCLUDED.provider_name,
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
            cmd.Parameters.AddWithValue("provider_type", providerType);
            cmd.Parameters.AddWithValue("provider_name", providerName);
            cmd.Parameters.AddWithValue("chunk_num", record.Chunk.ChunkNum);
            cmd.Parameters.AddWithValue("text", record.Chunk.Text);
            cmd.Parameters.AddWithValue("metadata", record.Metadata.RootElement.GetRawText());
            cmd.Parameters.AddWithValue("embedding", new Vector(record.Embedding));

            await cmd.ExecuteNonQueryAsync(ct);
            insertedCount++;
        }

        _logger.LogInformation("Upserted {Count} chunks to database for provider {Type}/{Name}", 
            insertedCount, providerType, providerName);
    }

    /// <summary>
    /// Checks if a document has already been indexed by comparing ETag.
    /// </summary>
    public async Task<bool> IsDocumentIndexedAsync(
        string docId, 
        string etag, 
        string providerType, 
        string providerName, 
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(docId);
        ArgumentNullException.ThrowIfNull(etag);

        await using var conn = new NpgsqlConnection(_options.ConnectionString);
        await conn.OpenAsync(ct);

        const string sql = @"
            SELECT EXISTS(
                SELECT 1 FROM docs_files
                WHERE doc_id = @doc_id 
                  AND etag = @etag
                  AND provider_type = @provider_type
                  AND provider_name = @provider_name
            )";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("doc_id", docId);
        cmd.Parameters.AddWithValue("etag", etag);
        cmd.Parameters.AddWithValue("provider_type", providerType);
        cmd.Parameters.AddWithValue("provider_name", providerName);

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
        string providerType,
        string providerName,
        string? relativePath = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(docId);
        ArgumentNullException.ThrowIfNull(filename);
        ArgumentNullException.ThrowIfNull(etag);

        await using var conn = new NpgsqlConnection(_options.ConnectionString);
        await conn.OpenAsync(ct);

        const string sql = @"
            INSERT INTO docs_files (doc_id, provider_type, provider_name, filename, etag, last_modified, relative_path)
            VALUES (@doc_id, @provider_type, @provider_name, @filename, @etag, @last_modified, @relative_path)
            ON CONFLICT (doc_id, provider_type, provider_name)
            DO UPDATE SET
                filename = EXCLUDED.filename,
                etag = EXCLUDED.etag,
                last_modified = EXCLUDED.last_modified,
                relative_path = EXCLUDED.relative_path";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("doc_id", docId);
        cmd.Parameters.AddWithValue("provider_type", providerType);
        cmd.Parameters.AddWithValue("provider_name", providerName);
        cmd.Parameters.AddWithValue("filename", filename);
        cmd.Parameters.AddWithValue("etag", etag);
        cmd.Parameters.AddWithValue("last_modified", lastModified);
        cmd.Parameters.AddWithValue("relative_path", (object?)relativePath ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogDebug("Updated file tracking for {DocId} from {Type}/{Name}", 
            docId, providerType, providerName);
    }

    /// <summary>
    /// Removes orphaned documents that no longer exist in the provider.
    /// Compares current provider documents with database records and deletes orphans.
    /// </summary>
    public async Task CleanupOrphanedDocumentsAsync(
        string providerType,
        string providerName,
        IReadOnlyCollection<string> currentDocumentIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(providerType);
        ArgumentNullException.ThrowIfNull(providerName);
        ArgumentNullException.ThrowIfNull(currentDocumentIds);

        await using var conn = new NpgsqlConnection(_options.ConnectionString);
        await conn.OpenAsync(ct);

        // Find orphaned docs: in database but not in current provider list
        const string findOrphansSql = @"
            SELECT doc_id, filename
            FROM docs_files
            WHERE provider_type = @provider_type
              AND provider_name = @provider_name
              AND doc_id != ALL(@current_doc_ids)";

        var orphanedDocs = new List<(string docId, string filename)>();

        await using (var findCmd = new NpgsqlCommand(findOrphansSql, conn))
        {
            findCmd.Parameters.AddWithValue("provider_type", providerType);
            findCmd.Parameters.AddWithValue("provider_name", providerName);
            findCmd.Parameters.AddWithValue("current_doc_ids", currentDocumentIds.ToArray());

            await using var reader = await findCmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                orphanedDocs.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        if (orphanedDocs.Count == 0)
        {
            _logger.LogInformation("No orphaned documents found for {Type}/{Name}",
                providerType, providerName);
            return;
        }

        _logger.LogInformation("Found {Count} orphaned document(s) for {Type}/{Name}. Cleaning up...",
            orphanedDocs.Count, providerType, providerName);

        // Delete chunks for orphaned documents
        const string deleteChunksSql = @"
            DELETE FROM docs_chunks
            WHERE provider_type = @provider_type
              AND provider_name = @provider_name
              AND doc_id = @doc_id";

        // Delete file tracking for orphaned documents
        const string deleteFilesSql = @"
            DELETE FROM docs_files
            WHERE provider_type = @provider_type
              AND provider_name = @provider_name
              AND doc_id = @doc_id";

        var totalChunksDeleted = 0;

        foreach (var (docId, filename) in orphanedDocs)
        {
            ct.ThrowIfCancellationRequested();

            // Delete chunks
            await using (var deleteChunksCmd = new NpgsqlCommand(deleteChunksSql, conn))
            {
                deleteChunksCmd.Parameters.AddWithValue("provider_type", providerType);
                deleteChunksCmd.Parameters.AddWithValue("provider_name", providerName);
                deleteChunksCmd.Parameters.AddWithValue("doc_id", docId);
                var chunksDeleted = await deleteChunksCmd.ExecuteNonQueryAsync(ct);
                totalChunksDeleted += chunksDeleted;
            }

            // Delete file tracking
            await using (var deleteFilesCmd = new NpgsqlCommand(deleteFilesSql, conn))
            {
                deleteFilesCmd.Parameters.AddWithValue("provider_type", providerType);
                deleteFilesCmd.Parameters.AddWithValue("provider_name", providerName);
                deleteFilesCmd.Parameters.AddWithValue("doc_id", docId);
                await deleteFilesCmd.ExecuteNonQueryAsync(ct);
            }

            _logger.LogInformation("Deleted orphaned document: {Filename} (DocId: {DocId})",
                filename, docId);
        }

        _logger.LogInformation(
            "Cleanup complete for {Type}/{Name}: {DocCount} documents removed, {ChunkCount} chunks deleted",
            providerType, providerName, orphanedDocs.Count, totalChunksDeleted);
    }

    /// <summary>
    /// Deletes all documents and chunks for a specific provider.
    /// Useful when a provider is disabled or re-indexed from scratch.
    /// </summary>
    public async Task DeleteAllProviderDocumentsAsync(
        string providerType,
        string providerName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(providerType);
        ArgumentNullException.ThrowIfNull(providerName);

        await using var conn = new NpgsqlConnection(_options.ConnectionString);
        await conn.OpenAsync(ct);

        // Count before deletion for logging
        const string countSql = @"
            SELECT COUNT(DISTINCT doc_id) as doc_count, COUNT(*) as chunk_count
            FROM docs_chunks
            WHERE provider_type = @provider_type AND provider_name = @provider_name";

        int docCount = 0, chunkCount = 0;

        await using (var countCmd = new NpgsqlCommand(countSql, conn))
        {
            countCmd.Parameters.AddWithValue("provider_type", providerType);
            countCmd.Parameters.AddWithValue("provider_name", providerName);

            await using var reader = await countCmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                docCount = reader.GetInt32(0);
                chunkCount = reader.GetInt32(1);
            }
        }

        if (docCount == 0)
        {
            _logger.LogInformation("No documents to delete for {Type}/{Name}",
                providerType, providerName);
            return;
        }

        // Delete chunks
        const string deleteChunksSql = @"
            DELETE FROM docs_chunks
            WHERE provider_type = @provider_type AND provider_name = @provider_name";

        await using (var deleteChunksCmd = new NpgsqlCommand(deleteChunksSql, conn))
        {
            deleteChunksCmd.Parameters.AddWithValue("provider_type", providerType);
            deleteChunksCmd.Parameters.AddWithValue("provider_name", providerName);
            await deleteChunksCmd.ExecuteNonQueryAsync(ct);
        }

        // Delete file tracking
        const string deleteFilesSql = @"
            DELETE FROM docs_files
            WHERE provider_type = @provider_type AND provider_name = @provider_name";

        await using (var deleteFilesCmd = new NpgsqlCommand(deleteFilesSql, conn))
        {
            deleteFilesCmd.Parameters.AddWithValue("provider_type", providerType);
            deleteFilesCmd.Parameters.AddWithValue("provider_name", providerName);
            await deleteFilesCmd.ExecuteNonQueryAsync(ct);
        }

        _logger.LogInformation(
            "Deleted all documents for {Type}/{Name}: {DocCount} documents, {ChunkCount} chunks",
            providerType, providerName, docCount, chunkCount);
    }
}
