namespace Api.Services.Interfaces;

/// <summary>
/// Service for keyword-based search using PostgreSQL full-text search.
/// Provides exact phrase matching and keyword extraction capabilities.
/// </summary>
public interface IKeywordSearchService
{
    /// <summary>
    /// Search for chunks containing exact keywords or phrases.
    /// </summary>
    Task<List<Api.Models.RawSearchResult>> SearchByKeywordsAsync(
        List<string> keywords,
        float[]? queryEmbedding = null,
        string? providerType = null,
        string? providerName = null,
        int limit = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Extract important keywords from a query string.
    /// </summary>
    List<string> ExtractKeywords(string query, int maxKeywords = 3);
}
