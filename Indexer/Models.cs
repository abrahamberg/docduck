using System.Text.Json;

namespace Indexer;

/// <summary>
/// Represents a text chunk with position metadata.
/// </summary>
public record Chunk(
    int ChunkNum,
    int CharStart,
    int CharEnd,
    string Text
);

/// <summary>
/// Represents a chunk record ready for database insertion with embedding.
/// </summary>
public record ChunkRecord(
    string DocId,
    string Filename,
    Chunk Chunk,
    float[] Embedding,
    JsonDocument Metadata
);

/// <summary>
/// Metadata for a OneDrive document.
/// </summary>
public record DocItem(
    string Id,
    string Name,
    string? ETag,
    DateTimeOffset? LastModified
);
