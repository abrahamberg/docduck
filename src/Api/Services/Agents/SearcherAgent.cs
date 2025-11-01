using Api.Models;
using Api.Services.Agents.Interfaces;
using Api.Services.Agents.Models;
using Api.Services.Interfaces;
using DocDuck.Providers.Ai;

namespace Api.Services.Agents;

/// <summary>
/// Searcher agent: executes parallel searches using vector, lexical, and keyword strategies.
/// </summary>
public sealed class SearcherAgent : ISearcherAgent
{
    private readonly IVectorSearchService _vectorSearch;
    private readonly IKeywordSearchService _keywordSearch;
    private readonly IModelAgnosticAiService _aiService;
    private readonly ILogger<SearcherAgent> _logger;

    public SearcherAgent(
        IVectorSearchService vectorSearch,
        IKeywordSearchService keywordSearch,
        IModelAgnosticAiService aiService,
        ILogger<SearcherAgent> logger)
    {
        _vectorSearch = vectorSearch;
        _keywordSearch = keywordSearch;
        _aiService = aiService;
        _logger = logger;
    }

    public async Task<List<RawSearchResult>> SearchAsync(
        SearchPlan plan,
        int topK,
        string? providerType,
        string? providerName,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Executing parallel searches: vector + keyword (topK={TopK}, provider={Type}/{Name})",
            topK,
            providerType ?? "all",
            providerName ?? "all");

        // Generate embedding once for both vector and keyword searches
        _logger.LogDebug("Generating embedding for phrase: {Phrase}", plan.Phrase);
        var embedding = await _aiService.EmbedAsync(plan.Phrase, ct);

        // Execute searches in parallel with shared embedding
        var vectorTask = ExecuteVectorSearchAsync(plan.Phrase, embedding, topK, providerType, providerName, ct);
        var keywordTask = ExecuteKeywordSearchAsync(plan.Keywords, embedding, topK, providerType, providerName, ct);

        await Task.WhenAll(vectorTask, keywordTask);

        var vectorResults = await vectorTask;
        var keywordResults = await keywordTask;

        // Combine results
        var allResults = new List<RawSearchResult>();
        allResults.AddRange(vectorResults);
        allResults.AddRange(keywordResults);

        _logger.LogInformation(
            "Search completed: {VectorCount} vector results, {KeywordCount} keyword results, {TotalCount} total",
            vectorResults.Count,
            keywordResults.Count,
            allResults.Count);

        return allResults;
    }

    private async Task<List<RawSearchResult>> ExecuteVectorSearchAsync(
        string phrase,
        float[] embedding,
        int topK,
        string? providerType,
        string? providerName,
        CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("Executing vector search with pre-generated embedding (topK={TopK})", topK);

            // Execute vector search (using existing VectorSearchService)
            var sources = await _vectorSearch.SearchAsync(
                embedding,
                phrase,
                topK,
                providerType,
                providerName,
                searchDepth: 1, // Simple search, no refinement
                ct);

            _logger.LogInformation(
                "Vector search for phrase \"{Phrase}\" returned {Count} results, best distance: {BestDist:F3}",
                phrase,
                sources.Count,
                sources.Count > 0 ? sources.Min(s => s.Distance) : double.MaxValue);

            // Convert Source to RawSearchResult
            return sources.Select(s => new RawSearchResult(
                DocId: s.DocId,
                Filename: s.Filename,
                ProviderType: s.ProviderType ?? string.Empty,
                ProviderName: s.ProviderName ?? string.Empty,
                ChunkNum: s.ChunkNum,
                Text: s.Text,
                Distance: s.Distance,
                Score: 1.0 - Math.Clamp(s.Distance / 2.0, 0.0, 1.0), // Convert distance to score
                MatchedKeywords: null,
                SearchStrategy: "vector"
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Vector search failed for phrase: {Phrase}", phrase);
            return [];
        }
    }

    private async Task<List<RawSearchResult>> ExecuteKeywordSearchAsync(
        List<string> keywords,
        float[] embedding,
        int topK,
        string? providerType,
        string? providerName,
        CancellationToken ct)
    {
        if (keywords.Count == 0)
        {
            return [];
        }

        try
        {
            return await _keywordSearch.SearchByKeywordsAsync(
                keywords,
                embedding,
                providerType,
                providerName,
                topK,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Keyword search failed for keywords: {Keywords}", string.Join(", ", keywords));
            return [];
        }
    }
}
