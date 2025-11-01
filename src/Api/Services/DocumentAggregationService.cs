using Api.Models;
using Api.Options;
using Api.Services.Interfaces;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Api.Services;

/// <summary>
/// Service for aggregating search results at the document level.
/// Combines chunks, adds context, deduplicates, and calculates strength scores.
/// </summary>
public sealed class DocumentAggregationService(
    IOptions<DbOptions> dbOptions,
    ILogger<DocumentAggregationService> logger) : IDocumentAggregationService
{
    private readonly DbOptions _dbOptions = dbOptions.Value;
    private readonly ILogger<DocumentAggregationService> _logger = logger;

    public async Task<List<SearchFinding>> AggregateByDocumentAsync(
        List<RawSearchResult> rawResults,
        int contextChunkCount = 2,
        CancellationToken ct = default)
    {
        if (rawResults.Count == 0)
        {
            return [];
        }

        var documentGroups = GroupResultsByDocument(rawResults);
        var findings = await ProcessDocumentGroupsAsync(documentGroups, contextChunkCount, ct);
        var sortedFindings = SortFindingsByStrength(findings);

        LogAggregationResults(rawResults.Count, sortedFindings.Count);

        return sortedFindings;
    }

    private static IEnumerable<IGrouping<DocumentKey, RawSearchResult>> GroupResultsByDocument(List<RawSearchResult> rawResults)
    {
        return rawResults.GroupBy(r => new DocumentKey(r.DocId, r.Filename, r.ProviderType, r.ProviderName));
    }

    private async Task<List<SearchFinding>> ProcessDocumentGroupsAsync(
        IEnumerable<IGrouping<DocumentKey, RawSearchResult>> documentGroups,
        int contextChunkCount,
        CancellationToken ct)
    {
        var findings = new List<SearchFinding>();

        foreach (var group in documentGroups)
        {
            var finding = await ProcessDocumentGroupAsync(group, contextChunkCount, ct);
            findings.Add(finding);
        }

        return findings;
    }

    private async Task<SearchFinding> ProcessDocumentGroupAsync(
        IGrouping<DocumentKey, RawSearchResult> group,
        int contextChunkCount,
        CancellationToken ct)
    {
        var key = group.Key;
        var uniqueChunks = DeduplicateChunks(group);
        var chunks = ConvertToChunkInfo(uniqueChunks, key.DocId);
        var contextChunks = await FetchContextChunksAsync(key.DocId, contextChunkCount, ct);
        var metrics = CalculateDocumentMetrics(uniqueChunks, key.Filename);

        return CreateSearchFinding(key, chunks, contextChunks, metrics);
    }

    private static List<RawSearchResult> DeduplicateChunks(IEnumerable<RawSearchResult> results)
    {
        return results
            .GroupBy(r => r.ChunkNum)
            .Select(g => g.OrderBy(r => r.Distance).First())
            .ToList();
    }

    private static List<ChunkInfo> ConvertToChunkInfo(List<RawSearchResult> uniqueChunks, string docId)
    {
        return uniqueChunks
            .Select(r => new ChunkInfo(
                ChunkId: $"{docId}_{r.ChunkNum}",
                ChunkNum: r.ChunkNum,
                Distance: r.Distance,
                Text: r.Text,
                MatchedKeywords: r.MatchedKeywords
            ))
            .OrderBy(c => c.Distance)
            .ToList();
    }

    private static DocumentMetrics CalculateDocumentMetrics(List<RawSearchResult> uniqueChunks, string filename)
    {
        var bestDistance = uniqueChunks.Min(r => r.Distance);
        var averageDistance = uniqueChunks.Average(r => r.Distance);
        var allKeywords = uniqueChunks
            .SelectMany(r => r.MatchedKeywords ?? [])
            .Distinct()
            .ToList();

        var strength = CalculateStrength(bestDistance, averageDistance, uniqueChunks.Count, allKeywords.Count, filename, uniqueChunks);
        var comment = GenerateComment(uniqueChunks.Count, allKeywords.Count, bestDistance, filename);

        return new DocumentMetrics(bestDistance, strength, comment, allKeywords);
    }

    private static SearchFinding CreateSearchFinding(
        DocumentKey key,
        List<ChunkInfo> chunks,
        List<ContextChunk> contextChunks,
        DocumentMetrics metrics)
    {
        return new SearchFinding(
            DocId: key.DocId,
            Filename: key.Filename,
            ProviderType: key.ProviderType,
            ProviderName: key.ProviderName,
            Strength: metrics.Strength,
            Comment: metrics.Comment,
            Distance: metrics.BestDistance,
            Keywords: metrics.AllKeywords,
            ChunkCount: chunks.Count,
            Chunks: chunks,
            ContextChunks: contextChunks
        );
    }

    private static List<SearchFinding> SortFindingsByStrength(List<SearchFinding> findings)
    {
        return findings.OrderByDescending(f => f.Strength).ToList();
    }

    private void LogAggregationResults(int rawCount, int docCount)
    {
        _logger.LogInformation(
            "Aggregated {RawCount} raw results into {DocCount} document findings",
            rawCount,
            docCount);
    }

    public async Task<List<ContextChunk>> FetchContextChunksAsync(
        string docId,
        int count = 2,
        CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_dbOptions.ConnectionString);
        await conn.OpenAsync(ct);

        const string sql = @"
            SELECT chunk_num, text
            FROM docs_chunks
            WHERE doc_id = @doc_id
            ORDER BY chunk_num ASC
            LIMIT @count";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("doc_id", docId);
        cmd.Parameters.AddWithValue("count", count);

        var contextChunks = new List<ContextChunk>();

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var chunkNum = reader.GetInt32(0);
                var text = reader.GetString(1);

                contextChunks.Add(new ContextChunk(
                    ChunkId: $"{docId}_{chunkNum}",
                    ChunkNum: chunkNum,
                    Text: text
                ));
            }

            return contextChunks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch context chunks for document {DocId}", docId);
            return [];
        }
    }

    private static int CalculateStrength(
        double bestDistance,
        double averageDistance,
        int chunkCount,
        int keywordCount,
        string filename,
        List<RawSearchResult> results)
    {
        // Vector score: convert distance (0-2) to score (0-1), higher is better
        var vectorScore = 1.0 - Math.Clamp(bestDistance / 2.0, 0.0, 1.0);

        // Lexical score: average of lexical search scores
        var lexicalScore = results
            .Where(r => r.SearchStrategy == "keyword" || r.SearchStrategy == "lexical")
            .Select(r => r.Score)
            .DefaultIfEmpty(0.0)
            .Average();

        // Keyword bonus: 0-1 based on matched keyword count
        var keywordBonus = Math.Clamp(keywordCount / 5.0, 0.0, 1.0);

        // Chunk count bonus: 0-1 based on number of matching chunks (more is better)
        var chunkCountScore = Math.Clamp(chunkCount / 10.0, 0.0, 1.0);

        // Filename match bonus: check if keywords appear in filename
        var filenameBonus = CalculateFilenameMatchBonus(filename, results);

        // Context bonus: file type and name relevance (simple heuristic)
        var contextBonus = CalculateContextBonus(filename);

        // Weighted combination (total = 100)
        var strength = (int)Math.Round(
            vectorScore * 30 +       // semantic relevance (30%)
            lexicalScore * 20 +      // keyword presence (20%)
            keywordBonus * 15 +      // exact keyword matches (15%)
            filenameBonus * 20 +     // filename matches keywords (20%)
            chunkCountScore * 10 +   // number of matching chunks (10%)
            contextBonus * 5         // file type/name relevance (5%)
        );

        return Math.Clamp(strength, 0, 100);
    }

    private static double CalculateFilenameMatchBonus(string filename, List<RawSearchResult> results)
    {
        // Extract all matched keywords from results
        var allKeywords = results
            .Where(r => r.MatchedKeywords != null)
            .SelectMany(r => r.MatchedKeywords!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (allKeywords.Count == 0)
            return 0.0;

        // Count how many keywords appear in the filename (case-insensitive)
        var filenameLower = filename.ToLowerInvariant();
        var matchedInFilename = allKeywords
            .Count(kw => filenameLower.Contains(kw.ToLowerInvariant()));

        // Return 0-1 score based on percentage of keywords in filename
        return Math.Clamp((double)matchedInFilename / allKeywords.Count, 0.0, 1.0);
    }

    private static double CalculateContextBonus(string filename)
    {
        var extension = Path.GetExtension(filename).ToLowerInvariant();
        var name = Path.GetFileNameWithoutExtension(filename).ToLowerInvariant();

        // Bonus for common documentation file types
        var score = extension switch
        {
            ".md" or ".markdown" => 0.8,
            ".txt" or ".text" => 0.7,
            ".pdf" => 0.6,
            ".doc" or ".docx" => 0.6,
            ".html" or ".htm" => 0.5,
            _ => 0.3
        };

        // Additional bonus for readme, documentation, or guide files
        if (name.Contains("readme") || name.Contains("doc") || name.Contains("guide"))
        {
            score = Math.Min(score + 0.2, 1.0);
        }

        return score;
    }

    private static string GenerateComment(
        int chunkCount,
        int keywordCount,
        double bestDistance,
        string filename)
    {
        var parts = new List<string>();

        // Chunk count
        if (chunkCount == 1)
        {
            parts.Add("1 matching chunk");
        }
        else
        {
            parts.Add($"{chunkCount} matching chunks");
        }

        // Keyword count
        if (keywordCount > 0)
        {
            parts.Add($"{keywordCount} keyword{(keywordCount > 1 ? "s" : "")} matched");
        }

        // Distance quality
        var quality = bestDistance switch
        {
            < 0.3 => "excellent match",
            < 0.5 => "strong match",
            < 0.7 => "good match",
            < 0.9 => "moderate match",
            _ => "weak match"
        };
        parts.Add(quality);

        // File type
        var extension = Path.GetExtension(filename).ToLowerInvariant();
        if (!string.IsNullOrEmpty(extension))
        {
            parts.Add($"in {extension} file");
        }

        var comment = string.Join(", ", parts);

        // Truncate to 300 chars
        return comment.Length <= 300 ? comment : string.Concat(comment.AsSpan(0, 297), "...");
    }
}
