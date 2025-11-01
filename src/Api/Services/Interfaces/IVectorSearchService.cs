using Api.Models;

namespace Api.Services.Interfaces;

/// <summary>
/// Interface for vector similarity search against PostgreSQL + pgvector.
/// </summary>
public interface IVectorSearchService
{
    /// <summary>
    /// Search for similar chunks using vector similarity (cosine distance).
    /// Optionally filter by provider.
    /// </summary>
    Task<List<Source>> SearchAsync(
        float[] queryEmbedding,
        string queryText,
        int? topK = null,
        string? providerType = null,
        string? providerName = null,
        int searchDepth = 1,
        CancellationToken ct = default);

    /// <summary>
    /// Get total count of indexed chunks.
    /// </summary>
    Task<long> GetChunkCountAsync(CancellationToken ct = default);

    /// <summary>
    /// Get count of indexed documents.
    /// </summary>
    Task<long> GetDocumentCountAsync(CancellationToken ct = default);

    /// <summary>
    /// Get list of all registered providers.
    /// </summary>
    Task<List<ProviderInfo>> GetProvidersAsync(CancellationToken ct = default);

    /// <summary>
    /// Fetch surrounding chunks for given doc/chunk list plus optional document top snippet.
    /// </summary>
    Task<Dictionary<string, List<Source>>> FetchContextWindowAsync(
        List<(string DocId, int ChunkNum)> targets,
        int window = 1,
        CancellationToken ct = default);
}
