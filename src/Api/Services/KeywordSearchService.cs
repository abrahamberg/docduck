using Api.Models;
using Api.Options;
using Api.Services.Interfaces;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Globalization;
using System.Text;

namespace Api.Services;

/// <summary>
/// Service for keyword-based search using PostgreSQL full-text search.
/// Provides exact phrase matching and keyword extraction capabilities.
/// </summary>
public sealed class KeywordSearchService(
    IOptions<DbOptions> dbOptions,
    IOptions<SearchOptions> searchOptions,
    ILogger<KeywordSearchService> logger) : IKeywordSearchService
{
    public async Task<List<RawSearchResult>> SearchByKeywordsAsync(
        List<string> keywords,
        float[]? queryEmbedding = null,
        string? providerType = null,
        string? providerName = null,
        int limit = 50,
        CancellationToken ct = default)
    {
        if (keywords.Count == 0)
        {
            return [];
        }

        await using var conn = new NpgsqlConnection(dbOptions.Value.ConnectionString);
        await conn.OpenAsync(ct);

        // Try full-text search first
        var ftsResults = await FullTextSearchAsync(conn, keywords, providerType, providerName, limit, ct);

        // If full-text search returns no results, fall back to simple pattern matching (ILIKE)
        // This handles proper nouns, company names, and non-English text better
        if (ftsResults.Count == 0)
        {
            logger.LogDebug("Full-text search returned 0 results, trying pattern matching for keywords: {Keywords}",
                string.Join(", ", keywords));
            var patternResults = await PatternMatchSearchAsync(conn, keywords, queryEmbedding, providerType, providerName, limit, ct);
            return patternResults;
        }

        return ftsResults;
    }

    private async Task<List<RawSearchResult>> FullTextSearchAsync(
        NpgsqlConnection conn,
        List<string> keywords,
        string? providerType,
        string? providerName,
        int limit,
        CancellationToken ct)
    {
        var queryText = BuildFullTextQuery(keywords);
        var sql = BuildFullTextSearchSql(providerType, providerName);

        await using var cmd = CreateFullTextSearchCommand(conn, sql, queryText, limit, providerType, providerName);

        try
        {
            var results = await ReadFullTextSearchResultsAsync(cmd, keywords, ct);

            LogFullTextSearchResults(keywords, results.Count);

            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Keyword search failed for keywords: {Keywords}", string.Join(", ", keywords));
            throw;
        }
    }

    private static string BuildFullTextQuery(List<string> keywords)
    {
        return string.Join(" | ", keywords); // OR search for any keyword
    }

    private static string BuildFullTextSearchSql(string? providerType, string? providerName)
    {
        const string baseSql = @"
            WITH search_query AS (
                SELECT websearch_to_tsquery(CAST(@config AS regconfig), @query) AS q
            ),
            highlighted AS (
                SELECT
                    c.doc_id,
                    c.filename,
                    c.provider_type,
                    c.provider_name,
                    c.chunk_num,
                    c.text,
                    ts_rank_cd(c.search_lexeme, sq.q) AS rank,
                    ts_headline('simple', c.text, sq.q, 'MaxWords=50, MinWords=20') AS headline
                FROM docs_chunks c, search_query sq
                WHERE c.search_lexeme @@ sq.q";

        var sqlBuilder = new StringBuilder(baseSql);

        if (!string.IsNullOrWhiteSpace(providerType))
        {
            sqlBuilder.AppendLine("AND c.provider_type = @provider_type");
        }

        if (!string.IsNullOrWhiteSpace(providerName))
        {
            sqlBuilder.AppendLine("AND c.provider_name = @provider_name");
        }

        sqlBuilder.AppendLine(")");
        sqlBuilder.AppendLine("SELECT doc_id, filename, provider_type, provider_name, chunk_num, text, rank, headline FROM highlighted");
        sqlBuilder.AppendLine("ORDER BY rank DESC");
        sqlBuilder.AppendLine("LIMIT @limit");

        return sqlBuilder.ToString();
    }

    private NpgsqlCommand CreateFullTextSearchCommand(
        NpgsqlConnection conn,
        string sql,
        string queryText,
        int limit,
        string? providerType,
        string? providerName)
    {
        var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("config", searchOptions.Value.LexicalConfiguration);
        cmd.Parameters.AddWithValue("query", queryText);
        cmd.Parameters.AddWithValue("limit", limit);

        if (!string.IsNullOrWhiteSpace(providerType))
        {
            cmd.Parameters.AddWithValue("provider_type", providerType);
        }

        if (!string.IsNullOrWhiteSpace(providerName))
        {
            cmd.Parameters.AddWithValue("provider_name", providerName);
        }

        return cmd;
    }

    private static async Task<List<RawSearchResult>> ReadFullTextSearchResultsAsync(
        NpgsqlCommand cmd,
        List<string> keywords,
        CancellationToken ct)
    {
        var results = new List<RawSearchResult>();

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var result = ParseFullTextSearchRow(reader, keywords);
            results.Add(result);
        }

        return results;
    }

    private static RawSearchResult ParseFullTextSearchRow(NpgsqlDataReader reader, List<string> keywords)
    {
        var docId = reader.GetString(0);
        var filename = reader.GetString(1);
        var providerType = reader.GetString(2);
        var providerName = reader.GetString(3);
        var chunkNum = reader.GetInt32(4);
        var text = reader.GetString(5);
        var rank = reader.GetDouble(6);

        var matchedKeywords = ExtractMatchedKeywords(text, keywords);
        var score = NormalizeRank(rank);

        return new RawSearchResult(
            DocId: docId,
            Filename: filename,
            ProviderType: providerType,
            ProviderName: providerName,
            ChunkNum: chunkNum,
            Text: text,
            Distance: 1.0 - score, // Convert score to distance for consistency
            Score: score,
            MatchedKeywords: matchedKeywords,
            SearchStrategy: "keyword"
        );
    }

    private void LogFullTextSearchResults(List<string> keywords, int resultCount)
    {
        logger.LogInformation(
            "Keyword search for [{Keywords}] returned {Count} results",
            string.Join(", ", keywords),
            resultCount);
    }

    /// <summary>
    /// Pattern matching search using ILIKE for exact substring matches.
    /// Better for proper nouns, company names, and non-English text.
    /// </summary>
    private async Task<List<RawSearchResult>> PatternMatchSearchAsync(
        NpgsqlConnection conn,
        List<string> keywords,
        float[]? queryEmbedding,
        string? providerType,
        string? providerName,
        int limit,
        CancellationToken ct)
    {
        var sql = BuildPatternMatchSql(keywords, queryEmbedding != null, providerType, providerName);
        await using var cmd = CreatePatternMatchCommand(conn, sql, keywords, queryEmbedding, limit, providerType, providerName);

        try
        {
            var results = await ReadPatternMatchResultsAsync(cmd, keywords, ct);

            LogPatternMatchResults(keywords, results.Count, queryEmbedding != null);

            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Pattern match search failed for keywords: {Keywords}", string.Join(", ", keywords));
            throw;
        }
    }

    private static string BuildPatternMatchSql(List<string> keywords, bool hasEmbedding, string? providerType, string? providerName)
    {
        var sqlBuilder = new StringBuilder();

        if (hasEmbedding)
        {
            sqlBuilder.Append(@"
            SELECT
                c.doc_id,
                c.filename,
                c.provider_type,
                c.provider_name,
                c.chunk_num,
                c.text,
                0.5 AS rank,
                (c.embedding <=> (@query_embedding)::vector) AS distance
            FROM docs_chunks c
            WHERE (");
        }
        else
        {
            sqlBuilder.Append(@"
            SELECT
                c.doc_id,
                c.filename,
                c.provider_type,
                c.provider_name,
                c.chunk_num,
                c.text,
                0.5 AS rank,
                0.5 AS distance
            FROM docs_chunks c
            WHERE (");
        }

        var keywordConditions = keywords.Select((_, i) => $"c.text ILIKE @keyword{i}");
        sqlBuilder.Append(string.Join(" OR ", keywordConditions));
        sqlBuilder.Append(")");

        if (!string.IsNullOrWhiteSpace(providerType))
        {
            sqlBuilder.Append(" AND c.provider_type = @provider_type");
        }

        if (!string.IsNullOrWhiteSpace(providerName))
        {
            sqlBuilder.Append(" AND c.provider_name = @provider_name");
        }

        sqlBuilder.AppendLine();
        sqlBuilder.AppendLine("ORDER BY filename, chunk_num");
        sqlBuilder.AppendLine("LIMIT @limit");

        return sqlBuilder.ToString();
    }

    private static NpgsqlCommand CreatePatternMatchCommand(
        NpgsqlConnection conn,
        string sql,
        List<string> keywords,
        float[]? queryEmbedding,
        int limit,
        string? providerType,
        string? providerName)
    {
        var cmd = new NpgsqlCommand(sql, conn);

        if (queryEmbedding != null)
        {
            var embeddingText = "[" + string.Join(",", queryEmbedding.Select(f => f.ToString(CultureInfo.InvariantCulture))) + "]";
            cmd.Parameters.AddWithValue("query_embedding", embeddingText);
        }

        for (int i = 0; i < keywords.Count; i++)
        {
            cmd.Parameters.AddWithValue($"keyword{i}", $"%{keywords[i]}%");
        }

        cmd.Parameters.AddWithValue("limit", limit);

        if (!string.IsNullOrWhiteSpace(providerType))
        {
            cmd.Parameters.AddWithValue("provider_type", providerType);
        }

        if (!string.IsNullOrWhiteSpace(providerName))
        {
            cmd.Parameters.AddWithValue("provider_name", providerName);
        }

        return cmd;
    }

    private static async Task<List<RawSearchResult>> ReadPatternMatchResultsAsync(
        NpgsqlCommand cmd,
        List<string> keywords,
        CancellationToken ct)
    {
        var results = new List<RawSearchResult>();

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var result = ParsePatternMatchRow(reader, keywords);
            results.Add(result);
        }

        return results;
    }

    private static RawSearchResult ParsePatternMatchRow(NpgsqlDataReader reader, List<string> keywords)
    {
        var docId = reader.GetString(0);
        var filename = reader.GetString(1);
        var providerType = reader.GetString(2);
        var providerName = reader.GetString(3);
        var chunkNum = reader.GetInt32(4);
        var text = reader.GetString(5);
        var rank = reader.GetDouble(6);
        var distance = reader.GetDouble(7);

        var matchedKeywords = ExtractMatchedKeywords(text, keywords);

        return new RawSearchResult(
            DocId: docId,
            Filename: filename,
            ProviderType: providerType,
            ProviderName: providerName,
            ChunkNum: chunkNum,
            Text: text,
            Distance: distance,
            Score: rank,
            MatchedKeywords: matchedKeywords,
            SearchStrategy: "pattern"
        );
    }

    private void LogPatternMatchResults(List<string> keywords, int resultCount, bool hasEmbedding)
    {
        logger.LogInformation(
            "Pattern match search for [{Keywords}] returned {Count} results (with {DistanceType} distances)",
            string.Join(", ", keywords),
            resultCount,
            hasEmbedding ? "calculated" : "fixed");
    }
    public List<string> ExtractKeywords(string query, int maxKeywords = 3)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        // Common words to filter (reduced list - keep more words)
        var commonWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for",
            "of", "with", "by", "from", "as", "is", "was", "are", "were", "be",
            "have", "has", "had", "do", "does", "did", "will", "would", "should",
            "could", "may", "might", "can", "what", "when", "where", "who", "why",
            "how", "which", "this", "that", "these", "those", "i", "you", "he",
            "she", "it", "we", "they", "my", "your", "his", "her", "its", "our",
            "their", "me", "him", "them", "us", "about", "than", "into", "through",
            "during", "before", "after", "above", "below", "between", "under",
            // Swedish common words
            "och", "att", "det", "som", "för", "på", "med", "till", "av", "är",
            "den", "om", "ett", "var", "sig", "så", "här", "har", "från", "ska",
            "kan", "inte", "men", "eller", "vi", "de", "en"
        };

        // Split on various delimiters but preserve quoted phrases
        var words = query
            .Split(new[] { ' ', '\t', '\n', '\r', ',', '.', '!', '?', ';', ':', '(', ')', '[', ']' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length > 2 && !commonWords.Contains(w)) // Filter out very short words (length <= 2)
            .Select(w => w.Trim('"', '\'')) // Remove quotes but keep the word
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxKeywords)
            .ToList();

        logger.LogDebug("Extracted {Count} keywords from query: {Keywords}", words.Count, string.Join(", ", words));

        return words;
    }

    private static List<string> ExtractMatchedKeywords(string text, List<string> keywords)
    {
        var matched = new List<string>();
        var lowerText = text.ToLowerInvariant();

        foreach (var keyword in keywords)
        {
            if (lowerText.Contains(keyword.ToLowerInvariant()))
            {
                matched.Add(keyword);
            }
        }

        return matched;
    }

    private static double NormalizeRank(double rank)
    {
        // ts_rank_cd typically ranges from 0 to 1+, but can go higher
        // Normalize to 0-1 range with some headroom
        return Math.Clamp(rank / 2.0, 0.0, 1.0);
    }
}
