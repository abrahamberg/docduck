namespace Api.Services.Interfaces;

/// <summary>
/// Service for aggregating search results at the document level.
/// Combines chunks, adds context, deduplicates, and calculates strength scores.
/// </summary>
public interface IDocumentAggregationService
{
    /// <summary>
    /// Aggregate raw search results by document, adding context chunks and calculating strength.
    /// </summary>
    Task<List<Api.Models.SearchFinding>> AggregateByDocumentAsync(
        List<Api.Models.RawSearchResult> rawResults,
        int contextChunkCount = 2,
        CancellationToken ct = default);

    /// <summary>
    /// Fetch the first N chunks of a document for context.
    /// </summary>
    Task<List<Api.Models.ContextChunk>> FetchContextChunksAsync(
        string docId,
        int count = 2,
        CancellationToken ct = default);
}
