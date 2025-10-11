using System.Text.Json;
using Indexer;
using Indexer.Options;
using Indexer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Indexer.Tests;

/// <summary>
/// Integration tests for VectorRepository.
/// Note: These tests require a running PostgreSQL instance with pgvector extension.
/// Set environment variable TEST_DB_CONNECTION_STRING to run these tests.
/// Example: TEST_DB_CONNECTION_STRING="Host=localhost;Database=vectors_test;Username=postgres;Password=password"
/// </summary>
public class VectorRepositoryIntegrationTests : IDisposable
{
    private readonly string? _connectionString;
    private readonly bool _skipTests;

    public VectorRepositoryIntegrationTests()
    {
        _connectionString = Environment.GetEnvironmentVariable("TEST_DB_CONNECTION_STRING");
        _skipTests = string.IsNullOrEmpty(_connectionString);

        if (!_skipTests)
        {
            InitializeDatabase();
        }
    }

    private void InitializeDatabase()
    {
        if (string.IsNullOrEmpty(_connectionString)) return;

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        // Clean up test tables
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DROP TABLE IF EXISTS docs_chunks CASCADE;
            DROP TABLE IF EXISTS docs_files CASCADE;
            
            CREATE TABLE docs_chunks (
                id BIGSERIAL PRIMARY KEY,
                doc_id TEXT NOT NULL,
                filename TEXT NOT NULL,
                chunk_num INT NOT NULL,
                text TEXT NOT NULL,
                metadata JSONB,
                embedding vector(1536),
                created_at TIMESTAMPTZ DEFAULT now(),
                CONSTRAINT unique_doc_chunk UNIQUE (doc_id, chunk_num)
            );
            
            CREATE TABLE docs_files (
                doc_id TEXT PRIMARY KEY,
                filename TEXT NOT NULL,
                etag TEXT NOT NULL,
                last_modified TIMESTAMPTZ NOT NULL
            );
        ";
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task InsertOrUpsertChunksAsync_WithNewChunks_InsertsSuccessfully()
    {
        if (_skipTests)
        {
            // Skip test if no database connection available
            return;
        }

        // Arrange
        var options = Options.Create(new DbOptions { ConnectionString = _connectionString! });
        var repository = new VectorRepository(options, NullLogger<VectorRepository>.Instance);

        var embedding = new float[1536];
        Array.Fill(embedding, 0.1f);

        var metadata = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            doc_id = "test-doc-1",
            filename = "test.docx",
            chunk_num = 0,
            char_start = 0,
            char_end = 100
        }));

        var chunk = new Chunk(0, 0, 100, "This is a test chunk.");
        var record = new ChunkRecord("test-doc-1", "test.docx", chunk, embedding, metadata);

        // Act
        await repository.InsertOrUpsertChunksAsync(new[] { record });

        // Assert
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM docs_chunks WHERE doc_id = 'test-doc-1'", conn);
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task InsertOrUpsertChunksAsync_WithDuplicateChunk_UpdatesExisting()
    {
        if (_skipTests)
        {
            return;
        }

        // Arrange
        var options = Options.Create(new DbOptions { ConnectionString = _connectionString! });
        var repository = new VectorRepository(options, NullLogger<VectorRepository>.Instance);

        var embedding = new float[1536];
        Array.Fill(embedding, 0.1f);

        var metadata = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            doc_id = "test-doc-2",
            filename = "test.docx",
            chunk_num = 0
        }));

        var chunk1 = new Chunk(0, 0, 100, "Original text");
        var record1 = new ChunkRecord("test-doc-2", "test.docx", chunk1, embedding, metadata);

        // Act - Insert first time
        await repository.InsertOrUpsertChunksAsync(new[] { record1 });

        // Update metadata for second insert
        var metadata2 = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            doc_id = "test-doc-2",
            filename = "test-updated.docx",
            chunk_num = 0
        }));

        var chunk2 = new Chunk(0, 0, 100, "Updated text");
        var record2 = new ChunkRecord("test-doc-2", "test-updated.docx", chunk2, embedding, metadata2);

        // Act - Upsert
        await repository.InsertOrUpsertChunksAsync(new[] { record2 });

        // Assert - Should still be only 1 record
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*), MAX(text), MAX(filename) FROM docs_chunks WHERE doc_id = 'test-doc-2'", 
            conn);
        
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        
        var count = reader.GetInt64(0);
        var text = reader.GetString(1);
        var filename = reader.GetString(2);

        Assert.Equal(1, count);
        Assert.Equal("Updated text", text);
        Assert.Equal("test-updated.docx", filename);
    }

    [Fact]
    public async Task InsertOrUpsertChunksAsync_WithMultipleChunks_InsertsAll()
    {
        if (_skipTests)
        {
            return;
        }

        // Arrange
        var options = Options.Create(new DbOptions { ConnectionString = _connectionString! });
        var repository = new VectorRepository(options, NullLogger<VectorRepository>.Instance);

        var records = Enumerable.Range(0, 5).Select(i =>
        {
            var embedding = new float[1536];
            Array.Fill(embedding, 0.1f * i);

            var metadata = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                doc_id = "test-doc-3",
                filename = "test.docx",
                chunk_num = i
            }));

            var chunk = new Chunk(i, i * 100, (i + 1) * 100, $"Chunk {i} text");
            return new ChunkRecord("test-doc-3", "test.docx", chunk, embedding, metadata);
        }).ToList();

        // Act
        await repository.InsertOrUpsertChunksAsync(records);

        // Assert
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM docs_chunks WHERE doc_id = 'test-doc-3'", 
            conn);
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        
        Assert.Equal(5, count);
    }

    [Fact]
    public async Task IsDocumentIndexedAsync_WithExistingDocument_ReturnsTrue()
    {
        if (_skipTests)
        {
            return;
        }

        // Arrange
        var options = Options.Create(new DbOptions { ConnectionString = _connectionString! });
        var repository = new VectorRepository(options, NullLogger<VectorRepository>.Instance);

        var docId = "test-doc-4";
        var etag = "etag-123";
        
        await repository.UpdateFileTrackingAsync(
            docId, 
            "test.docx", 
            etag, 
            DateTimeOffset.UtcNow);

        // Act
        var exists = await repository.IsDocumentIndexedAsync(docId, etag);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task IsDocumentIndexedAsync_WithNonExistingDocument_ReturnsFalse()
    {
        if (_skipTests)
        {
            return;
        }

        // Arrange
        var options = Options.Create(new DbOptions { ConnectionString = _connectionString! });
        var repository = new VectorRepository(options, NullLogger<VectorRepository>.Instance);

        // Act
        var exists = await repository.IsDocumentIndexedAsync("non-existing-doc", "any-etag");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task IsDocumentIndexedAsync_WithDifferentETag_ReturnsFalse()
    {
        if (_skipTests)
        {
            return;
        }

        // Arrange
        var options = Options.Create(new DbOptions { ConnectionString = _connectionString! });
        var repository = new VectorRepository(options, NullLogger<VectorRepository>.Instance);

        var docId = "test-doc-5";
        var originalETag = "etag-original";
        
        await repository.UpdateFileTrackingAsync(
            docId, 
            "test.docx", 
            originalETag, 
            DateTimeOffset.UtcNow);

        // Act
        var exists = await repository.IsDocumentIndexedAsync(docId, "etag-different");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task UpdateFileTrackingAsync_WithNewFile_InsertsSuccessfully()
    {
        if (_skipTests)
        {
            return;
        }

        // Arrange
        var options = Options.Create(new DbOptions { ConnectionString = _connectionString! });
        var repository = new VectorRepository(options, NullLogger<VectorRepository>.Instance);

        var docId = "test-doc-6";
        var filename = "test.docx";
        var etag = "etag-123";
        var lastModified = DateTimeOffset.UtcNow;

        // Act
        await repository.UpdateFileTrackingAsync(docId, filename, etag, lastModified);

        // Assert
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM docs_files WHERE doc_id = @doc_id AND etag = @etag", 
            conn);
        cmd.Parameters.AddWithValue("doc_id", docId);
        cmd.Parameters.AddWithValue("etag", etag);
        
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task UpdateFileTrackingAsync_WithExistingFile_UpdatesSuccessfully()
    {
        if (_skipTests)
        {
            return;
        }

        // Arrange
        var options = Options.Create(new DbOptions { ConnectionString = _connectionString! });
        var repository = new VectorRepository(options, NullLogger<VectorRepository>.Instance);

        var docId = "test-doc-7";
        
        // Act - Insert first time
        await repository.UpdateFileTrackingAsync(
            docId, 
            "original.docx", 
            "etag-v1", 
            DateTimeOffset.UtcNow);

        // Act - Update
        var newLastModified = DateTimeOffset.UtcNow.AddHours(1);
        await repository.UpdateFileTrackingAsync(
            docId, 
            "updated.docx", 
            "etag-v2", 
            newLastModified);

        // Assert - Should still be only 1 record
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*), MAX(filename), MAX(etag) FROM docs_files WHERE doc_id = @doc_id", 
            conn);
        cmd.Parameters.AddWithValue("doc_id", docId);
        
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        
        var count = reader.GetInt64(0);
        var filename = reader.GetString(1);
        var etag = reader.GetString(2);

        Assert.Equal(1, count);
        Assert.Equal("updated.docx", filename);
        Assert.Equal("etag-v2", etag);
    }

    [Fact]
    public async Task InsertOrUpsertChunksAsync_WithEmptyList_DoesNothing()
    {
        if (_skipTests)
        {
            return;
        }

        // Arrange
        var options = Options.Create(new DbOptions { ConnectionString = _connectionString! });
        var repository = new VectorRepository(options, NullLogger<VectorRepository>.Instance);

        // Act - Should not throw
        await repository.InsertOrUpsertChunksAsync(Array.Empty<ChunkRecord>());

        // Assert - No exception thrown is the assertion
        Assert.True(true);
    }

    [Fact]
    public async Task InsertOrUpsertChunksAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        if (_skipTests)
        {
            return;
        }

        // Arrange
        var options = Options.Create(new DbOptions { ConnectionString = _connectionString! });
        var repository = new VectorRepository(options, NullLogger<VectorRepository>.Instance);

        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var embedding = new float[1536];
        var metadata = JsonDocument.Parse("{}");
        var chunk = new Chunk(0, 0, 100, "Test");
        var record = new ChunkRecord("test", "test.docx", chunk, embedding, metadata);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => repository.InsertOrUpsertChunksAsync(new[] { record }, cts.Token));
    }

    public void Dispose()
    {
        if (!_skipTests && !string.IsNullOrEmpty(_connectionString))
        {
            // Clean up test database
            try
            {
                using var conn = new NpgsqlConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    DROP TABLE IF EXISTS docs_chunks CASCADE;
                    DROP TABLE IF EXISTS docs_files CASCADE;
                ";
                cmd.ExecuteNonQuery();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
