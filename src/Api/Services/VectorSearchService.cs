using System.Globalization;
using System.Text;
using System.Text.Json;
using Api.Models;
using Api.Options;
using Api.Services.Interfaces;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Api.Services;

/// <summary>
/// Repository for vector similarity search against PostgreSQL + pgvector.
/// </summary>
public sealed class VectorSearchService(
    IOptions<DbOptions> dbOptions,
    IOptions<SearchOptions> searchOptions,
    ILogger<VectorSearchService> _logger) : IVectorSearchService
{
    private const string AndSeparator = " AND ";

    // Explicit field declarations needed for null validation
    private readonly DbOptions _dbOptions = dbOptions?.Value ?? throw new ArgumentNullException(nameof(dbOptions));
    private readonly SearchOptions _searchOptions = searchOptions?.Value ?? throw new ArgumentNullException(nameof(searchOptions));
    private readonly ILogger<VectorSearchService> _logger = _logger ?? throw new ArgumentNullException(nameof(_logger));
    private bool _lexicalSearchUnavailable;

    public async Task<List<Source>> SearchAsync(
        float[] queryEmbedding,
        string queryText,
        int? topK = null,
        string? providerType = null,
        string? providerName = null,
        int searchDepth = 1,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(queryEmbedding);

        var searchParams = PrepareSearchParameters(queryText, topK, searchDepth);

        LogSearchStart(searchParams, providerType, providerName);

        await using var conn = new NpgsqlConnection(_dbOptions.ConnectionString);
        await conn.OpenAsync(ct);

        var allowedDocIds = await GetDocumentFilterIfEnabledAsync(
            conn,
            queryEmbedding,
            providerType,
            providerName,
            searchParams.LexicalOnly,
            ct);

        var vectorResults = await ExecuteVectorSearchIfNeededAsync(
            conn,
            queryEmbedding,
            searchParams,
            providerType,
            providerName,
            allowedDocIds,
            ct);

        var lexicalResults = await ExecuteLexicalSearchIfEnabledAsync(
            conn,
            searchParams,
            providerType,
            providerName,
            ct);

        var combined = CombineCandidates(
            vectorResults,
            lexicalResults,
            searchParams.K,
            searchParams.Depth,
            searchParams.LexicalEnabled,
            !searchParams.LexicalOnly && vectorResults.Count > 0);

        LogSearchResults(combined.Count, vectorResults.Count, lexicalResults.Count, allowedDocIds != null);

        return combined;
    }

    private SearchParameters PrepareSearchParameters(
        string? queryText,
        int? topK,
        int searchDepth)
    {
        var depth = Math.Clamp(searchDepth, 1, _searchOptions.MaxSearchDepth);
        var k = Math.Min(topK ?? _searchOptions.DefaultTopK, _searchOptions.MaxTopK);
        var normalizedQuery = (queryText ?? string.Empty).Trim();
        var lexicalEnabled = _searchOptions.EnableLexicalSearch && !string.IsNullOrWhiteSpace(normalizedQuery);
        var lexicalOnly = lexicalEnabled && depth == 1;
        var lexicalLimit = Math.Max(1, Math.Min(_searchOptions.MaxLexicalResults, Math.Max(k * 3, k)));

        return new SearchParameters(
            Depth: depth,
            K: k,
            NormalizedQuery: normalizedQuery,
            LexicalEnabled: lexicalEnabled,
            LexicalOnly: lexicalOnly,
            LexicalLimit: lexicalLimit
        );
    }

    private void LogSearchStart(SearchParameters searchParams, string? providerType, string? providerName)
    {
        _logger.LogDebug(
            "Searching depth {Depth} for top {K} chunks (Provider: {Type}/{Name}, Lexical:{LexicalEnabled}, Vector:{VectorEnabled})",
            searchParams.Depth,
            searchParams.K,
            providerType ?? "all",
            providerName ?? "all",
            searchParams.LexicalEnabled,
            !searchParams.LexicalOnly);
    }

    private void LogSearchResults(int combinedCount, int vectorCount, int lexicalCount, bool docFilterEnabled)
    {
        _logger.LogInformation(
            "Search produced {CombinedCount} chunks (vector: {VectorCount}, lexical: {LexicalCount}, doc-filter: {DocFilter})",
            combinedCount,
            vectorCount,
            lexicalCount,
            docFilterEnabled ? "enabled" : "disabled");
    }

    private async Task<HashSet<string>?> GetDocumentFilterIfEnabledAsync(
        NpgsqlConnection conn,
        float[] queryEmbedding,
        string? providerType,
        string? providerName,
        bool lexicalOnly,
        CancellationToken ct)
    {
        if (!_searchOptions.EnableDocumentLevelFiltering || lexicalOnly)
        {
            return null;
        }

        var docIds = await ExecuteDocumentLevelFilterAsync(
            conn,
            queryEmbedding,
            _searchOptions.DocumentLevelTopK,
            providerType,
            providerName,
            ct);

        if (docIds.Count == 0)
        {
            _logger.LogWarning("Document-level filtering returned no documents. Falling back to standard search.");
            return null;
        }

        return docIds;
    }

    private static async Task<List<Source>> ExecuteVectorSearchIfNeededAsync(
        NpgsqlConnection conn,
        float[] queryEmbedding,
        SearchParameters searchParams,
        string? providerType,
        string? providerName,
        HashSet<string>? allowedDocIds,
        CancellationToken ct)
    {
        if (searchParams.LexicalOnly)
        {
            return [];
        }

        return allowedDocIds != null
            ? await ExecuteVectorSearchWithDocFilterAsync(conn, queryEmbedding, searchParams.K, providerType, providerName, allowedDocIds, ct)
            : await ExecuteVectorSearchAsync(conn, queryEmbedding, searchParams.K, providerType, providerName, ct);
    }

    private async Task<List<LexicalMatch>> ExecuteLexicalSearchIfEnabledAsync(
        NpgsqlConnection conn,
        SearchParameters searchParams,
        string? providerType,
        string? providerName,
        CancellationToken ct)
    {
        if (!searchParams.LexicalEnabled)
        {
            return [];
        }

        return await ExecuteLexicalSearchAsync(
            conn,
            searchParams.NormalizedQuery,
            providerType,
            providerName,
            searchParams.LexicalLimit,
            ct);
    }

    private static async Task<List<Source>> ExecuteVectorSearchAsync(
        NpgsqlConnection conn,
        float[] queryEmbedding,
        int limit,
        string? providerType,
        string? providerName,
        CancellationToken ct)
    {
        var sql = BuildVectorSearchSql(providerType, providerName, docFilterEnabled: false);
        await using var cmd = CreateVectorSearchCommand(conn, sql, queryEmbedding, limit, providerType, providerName, allowedDocIds: null);

        return await ReadVectorSearchResultsAsync(cmd, limit, ct);
    }

    private static string BuildVectorSearchSql(string? providerType, string? providerName, bool docFilterEnabled)
    {
        var sql = @"
            SELECT
                doc_id,
                filename,
                provider_type,
                provider_name,
                chunk_num,
                text,
                metadata,
                embedding <=> @embedding::vector AS distance
            FROM docs_chunks";

        var whereConditions = new List<string>();

        if (docFilterEnabled)
        {
            whereConditions.Add("doc_id = ANY(@allowed_doc_ids)");
        }

        if (!string.IsNullOrWhiteSpace(providerType))
        {
            whereConditions.Add("provider_type = @provider_type");
        }

        if (!string.IsNullOrWhiteSpace(providerName))
        {
            whereConditions.Add("provider_name = @provider_name");
        }

        if (whereConditions.Count > 0)
        {
            sql += $" WHERE {string.Join(AndSeparator, whereConditions)}";
        }

        sql += @"
            ORDER BY embedding <=> (@embedding)::vector
            LIMIT @limit";

        return sql;
    }

    private static NpgsqlCommand CreateVectorSearchCommand(
        NpgsqlConnection conn,
        string sql,
        float[] queryEmbedding,
        int limit,
        string? providerType,
        string? providerName,
        HashSet<string>? allowedDocIds)
    {
        var cmd = new NpgsqlCommand(sql, conn);

        var embeddingText = FormatEmbeddingArray(queryEmbedding);
        cmd.Parameters.AddWithValue("embedding", embeddingText);
        cmd.Parameters.AddWithValue("limit", limit);

        if (allowedDocIds != null)
        {
            cmd.Parameters.AddWithValue("allowed_doc_ids", allowedDocIds.ToArray());
        }

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

    private static async Task<List<Source>> ReadVectorSearchResultsAsync(
        NpgsqlCommand cmd,
        int capacity,
        CancellationToken ct)
    {
        var results = new List<Source>(capacity);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var source = ParseVectorSearchRow(reader);
            results.Add(source);
        }

        return results;
    }

    private static Source ParseVectorSearchRow(NpgsqlDataReader reader)
    {
        var docId = reader.GetString(0);
        var filename = reader.GetString(1);
        var providerType = reader.GetString(2);
        var providerName = reader.GetString(3);
        var chunkNum = reader.GetInt32(4);
        var text = reader.GetString(5);
        var distance = reader.GetDouble(7);
        var citation = BuildCitation(providerType, providerName, filename, chunkNum);

        return new Source(
            DocId: docId,
            Filename: filename,
            ChunkNum: chunkNum,
            Text: text,
            Distance: distance,
            Citation: citation,
            ProviderType: providerType,
            ProviderName: providerName
        );
    }

    private static string FormatEmbeddingArray(float[] queryEmbedding)
    {
        return "[" + string.Join(",", queryEmbedding.Select(f => f.ToString(CultureInfo.InvariantCulture))) + "]";
    }

    private static async Task<List<Source>> ExecuteVectorSearchWithDocFilterAsync(
        NpgsqlConnection conn,
        float[] queryEmbedding,
        int limit,
        string? providerType,
        string? providerName,
        HashSet<string> allowedDocIds,
        CancellationToken ct)
    {
        var sql = BuildVectorSearchSql(providerType, providerName, docFilterEnabled: true);
        await using var cmd = CreateVectorSearchCommand(conn, sql, queryEmbedding, limit, providerType, providerName, allowedDocIds);

        return await ReadVectorSearchResultsAsync(cmd, limit, ct);
    }

    private async Task<HashSet<string>> ExecuteDocumentLevelFilterAsync(
        NpgsqlConnection conn,
        float[] queryEmbedding,
        int topK,
        string? providerType,
        string? providerName,
        CancellationToken ct)
    {
        var sql = BuildDocumentFilterSql(providerType, providerName);
        await using var cmd = CreateDocumentFilterCommand(conn, sql, queryEmbedding, topK, providerType, providerName);

        var docIds = await ReadDocumentIdsAsync(cmd, ct);

        _logger.LogInformation(
            "Document-level filtering selected {Count} documents from top-{TopK}",
            docIds.Count,
            topK);

        return docIds;
    }

    private static string BuildDocumentFilterSql(string? providerType, string? providerName)
    {
        var sql = @"
            SELECT doc_id
            FROM docs_files
            WHERE avg_embedding IS NOT NULL";

        var whereConditions = new List<string>();

        if (!string.IsNullOrWhiteSpace(providerType))
        {
            whereConditions.Add("provider_type = @provider_type");
        }

        if (!string.IsNullOrWhiteSpace(providerName))
        {
            whereConditions.Add("provider_name = @provider_name");
        }

        if (whereConditions.Count > 0)
        {
            sql += AndSeparator + string.Join(AndSeparator, whereConditions);
        }

        sql += @"
            ORDER BY avg_embedding <=> @embedding::vector
            LIMIT @limit";

        return sql;
    }

    private static NpgsqlCommand CreateDocumentFilterCommand(
        NpgsqlConnection conn,
        string sql,
        float[] queryEmbedding,
        int topK,
        string? providerType,
        string? providerName)
    {
        var cmd = new NpgsqlCommand(sql, conn);

        var embeddingText = FormatEmbeddingArray(queryEmbedding);
        cmd.Parameters.AddWithValue("embedding", embeddingText);
        cmd.Parameters.AddWithValue("limit", topK);

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

    private static async Task<HashSet<string>> ReadDocumentIdsAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var docIds = new HashSet<string>();

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            docIds.Add(reader.GetString(0));
        }

        return docIds;
    }

    private async Task<List<LexicalMatch>> ExecuteLexicalSearchAsync(
        NpgsqlConnection conn,
        string queryText,
        string? providerType,
        string? providerName,
        int limit,
        CancellationToken ct)
    {
        if (_lexicalSearchUnavailable)
        {
            return [];
        }

        var sql = BuildLexicalSearchSql(providerType, providerName);

        try
        {
            await using var cmd = CreateLexicalSearchCommand(conn, sql, queryText, limit, providerType, providerName);
            return await ReadLexicalSearchResultsAsync(cmd, limit, ct);
        }
        catch (PostgresException ex) when (ex.SqlState is "42P01" or "42703" or "42883")
        {
            HandleLexicalSearchDisabled(ex);
            return [];
        }
        catch (PostgresException ex) when (ex.SqlState == "XX000")
        {
            LogLexicalSearchFailure(ex, queryText);
            return [];
        }
    }

    private static string BuildLexicalSearchSql(string? providerType, string? providerName)
    {
        const string baseSql = @"
            WITH search_query AS (
                SELECT websearch_to_tsquery(CAST(@config AS regconfig), @query) AS q
            )
            SELECT
                c.doc_id,
                c.filename,
                c.provider_type,
                c.provider_name,
                c.chunk_num,
                c.text,
                c.metadata,
                ts_rank_cd(c.search_lexeme, sq.q) AS rank
            FROM docs_chunks c, search_query sq";

        var whereConditions = new List<string> { "c.search_lexeme @@ sq.q" };

        if (!string.IsNullOrWhiteSpace(providerType))
        {
            whereConditions.Add("c.provider_type = @provider_type");
        }

        if (!string.IsNullOrWhiteSpace(providerName))
        {
            whereConditions.Add("c.provider_name = @provider_name");
        }

        var sqlBuilder = new StringBuilder(baseSql);
        sqlBuilder.AppendLine();
        sqlBuilder.Append("WHERE ");
        sqlBuilder.Append(string.Join(AndSeparator, whereConditions));
        sqlBuilder.AppendLine();
        sqlBuilder.AppendLine("ORDER BY rank DESC");
        sqlBuilder.AppendLine("LIMIT @limit");

        return sqlBuilder.ToString();
    }

    private NpgsqlCommand CreateLexicalSearchCommand(
        NpgsqlConnection conn,
        string sql,
        string queryText,
        int limit,
        string? providerType,
        string? providerName)
    {
        var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("config", _searchOptions.LexicalConfiguration);
        cmd.Parameters.AddWithValue("query", queryText);
        cmd.Parameters.AddWithValue("limit", limit);

        if (!string.IsNullOrWhiteSpace(providerType))
        {
            cmd.Parameters.AddWithValue("provider_type", providerType!);
        }

        if (!string.IsNullOrWhiteSpace(providerName))
        {
            cmd.Parameters.AddWithValue("provider_name", providerName!);
        }

        return cmd;
    }

    private static async Task<List<LexicalMatch>> ReadLexicalSearchResultsAsync(
        NpgsqlCommand cmd,
        int capacity,
        CancellationToken ct)
    {
        var matches = new List<LexicalMatch>(capacity);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var match = ParseLexicalSearchRow(reader);
            matches.Add(match);
        }

        return matches;
    }

    private static LexicalMatch ParseLexicalSearchRow(NpgsqlDataReader reader)
    {
        var docId = reader.GetString(0);
        var filename = reader.GetString(1);
        var providerType = reader.GetString(2);
        var providerName = reader.GetString(3);
        var chunkNum = reader.GetInt32(4);
        var text = reader.GetString(5);
        var rank = reader.GetDouble(7);
        var citation = BuildCitation(providerType, providerName, filename, chunkNum);

        var source = new Source(
            DocId: docId,
            Filename: filename,
            ChunkNum: chunkNum,
            Text: text,
            Distance: 1d,
            Citation: citation,
            ProviderType: providerType,
            ProviderName: providerName
        );

        return new LexicalMatch(source, rank);
    }

    private void HandleLexicalSearchDisabled(PostgresException ex)
    {
        _lexicalSearchUnavailable = true;
        _logger.LogWarning(ex, "Lexical search disabled because the database is missing the required schema or extension.");
    }

    private void LogLexicalSearchFailure(PostgresException ex, string queryText)
    {
        _logger.LogDebug(ex, "Lexical search failed for query '{Query}'. Falling back to vector-only results.", queryText);
    }

    private List<Source> CombineCandidates(
        List<Source> vectorResults,
        IReadOnlyCollection<LexicalMatch> lexicalResults,
        int limit,
        int depth,
        bool lexicalEnabled,
        bool vectorEnabled)
    {
        if (vectorResults.Count == 0 && lexicalResults.Count == 0)
        {
            return [];
        }

        var candidates = BuildCandidateDictionary(vectorResults, lexicalResults);
        var lexicalWeight = DetermineLexicalWeight(depth, lexicalEnabled, vectorEnabled);
        var vectorWeight = 1d - lexicalWeight;

        return RankAndSelectCandidates(candidates, vectorWeight, lexicalWeight, limit);
    }

    private static Dictionary<(string DocId, int Chunk), CandidateScore> BuildCandidateDictionary(
        List<Source> vectorResults,
        IReadOnlyCollection<LexicalMatch> lexicalResults)
    {
        var candidates = new Dictionary<(string DocId, int Chunk), CandidateScore>();

        foreach (var source in vectorResults)
        {
            var key = (source.DocId, source.ChunkNum);
            if (!candidates.TryGetValue(key, out var candidate))
            {
                candidate = new CandidateScore(source);
                candidates[key] = candidate;
            }

            candidate.VectorDistance = source.Distance;
            candidate.VectorScore = Math.Max(candidate.VectorScore, CalculateVectorScore(source.Distance));
            candidate.Source = source;
        }

        if (lexicalResults.Count > 0)
        {
            AddLexicalScoresToCandidates(candidates, lexicalResults);
        }

        return candidates;
    }

    private static void AddLexicalScoresToCandidates(
        Dictionary<(string DocId, int Chunk), CandidateScore> candidates,
        IReadOnlyCollection<LexicalMatch> lexicalResults)
    {
        var maxRank = lexicalResults.Max(match => match.Rank);
        var normalization = maxRank <= 0d ? 0d : maxRank;

        foreach (var match in lexicalResults)
        {
            var key = (match.Source.DocId, match.Source.ChunkNum);
            if (!candidates.TryGetValue(key, out var candidate))
            {
                candidate = new CandidateScore(match.Source);
                candidates[key] = candidate;
            }

            var lexicalScore = normalization <= double.Epsilon ? 0d : match.Rank / normalization;
            candidate.LexicalScore = Math.Max(candidate.LexicalScore, lexicalScore);

            if (string.IsNullOrWhiteSpace(candidate.Source.Text))
            {
                candidate.Source = match.Source;
            }
        }
    }

    private static List<Source> RankAndSelectCandidates(
        Dictionary<(string DocId, int Chunk), CandidateScore> candidates,
        double vectorWeight,
        double lexicalWeight,
        int limit)
    {
        return candidates.Values
            .Select(candidate =>
            {
                var combinedScore = (vectorWeight * candidate.VectorScore) + (lexicalWeight * candidate.LexicalScore);
                var adjustedDistance = 1d - combinedScore;
                var updatedSource = candidate.Source with { Distance = adjustedDistance };
                return new
                {
                    Source = updatedSource,
                    Combined = combinedScore,
                    candidate.VectorDistance
                };
            })
            .OrderByDescending(x => x.Combined)
            .ThenBy(x => x.VectorDistance ?? double.MaxValue)
            .ThenBy(x => x.Source.Distance)
            .Take(limit)
            .Select(x => x.Source)
            .ToList();
    }

    private static double CalculateVectorScore(double distance)
    {
        var clamped = Math.Clamp(distance, 0d, 2d);
        return 1d - (clamped / 2d);
    }

    private double DetermineLexicalWeight(int depth, bool lexicalEnabled, bool vectorEnabled)
    {
        if (!lexicalEnabled)
        {
            return 0d;
        }

        if (!vectorEnabled)
        {
            return 1d;
        }

        return depth switch
        {
            1 => 1d,
            2 => Math.Clamp(_searchOptions.LexicalScoreWeight + 0.15d, 0d, 1d),
            _ => Math.Clamp(_searchOptions.LexicalScoreWeight, 0d, 1d)
        };
    }

    private static string BuildCitation(string providerType, string providerName, string filename, int chunkNum)
    {
        return $"[{providerType}/{providerName}:{filename}#chunk{chunkNum}]";
    }

    /// <summary>
    /// Get total count of indexed chunks.
    /// </summary>
    public async Task<long> GetChunkCountAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_dbOptions.ConnectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM docs_chunks", conn);
        var count = (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);

        return count;
    }

    /// <summary>
    /// Get count of indexed documents.
    /// </summary>
    public async Task<long> GetDocumentCountAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_dbOptions.ConnectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(DISTINCT doc_id) FROM docs_chunks",
            conn);
        var count = (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);

        return count;
    }

    /// <summary>
    /// Get list of all registered providers.
    /// </summary>
    public async Task<List<Api.Models.ProviderInfo>> GetProvidersAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_dbOptions.ConnectionString);
        await conn.OpenAsync(ct);

        const string sql = @"
            SELECT
                provider_type,
                provider_name,
                is_enabled,
                registered_at,
                last_sync_at,
                metadata
            FROM providers
            ORDER BY provider_type, provider_name";

        await using var cmd = new NpgsqlCommand(sql, conn);
        var providers = new List<Api.Models.ProviderInfo>();

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var providerType = reader.GetString(0);
            var providerName = reader.GetString(1);
            var isEnabled = reader.GetBoolean(2);
            var registeredAt = reader.GetDateTime(3);
            var lastSyncAt = await reader.IsDBNullAsync(4, ct) ? null : (DateTimeOffset?)reader.GetDateTime(4);
            var metadataJson = await reader.IsDBNullAsync(5, ct) ? null : reader.GetString(5);

            Dictionary<string, string>? metadata = null;
            if (!string.IsNullOrEmpty(metadataJson))
            {
                metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson);
            }

            providers.Add(new Api.Models.ProviderInfo(
                ProviderType: providerType,
                ProviderName: providerName,
                IsEnabled: isEnabled,
                RegisteredAt: registeredAt,
                LastSyncAt: lastSyncAt,
                Metadata: metadata
            ));
        }

        return providers;
    }

    /// <summary>
    /// Fetch surrounding chunks for given doc/chunk list plus optional document top snippet.
    /// </summary>
    public async Task<Dictionary<string, List<Source>>> FetchContextWindowAsync(
        List<(string DocId, int ChunkNum)> targets,
        int window = 1,
        CancellationToken ct = default)
    {
        if (targets.Count == 0) return [];
        await using var conn = new NpgsqlConnection(_dbOptions.ConnectionString);
        await conn.OpenAsync(ct);

        var result = new Dictionary<string, List<Source>>();

        foreach (var group in targets.GroupBy(t => t.DocId))
        {
            var docId = group.Key;
            var chunkNums = group.Select(g => g.ChunkNum).ToList();
            var minChunk = chunkNums.Min() - window;
            var maxChunk = chunkNums.Max() + window;
            var sql = @"SELECT doc_id, filename, provider_type, provider_name, chunk_num, text, embedding <=> embedding AS distance FROM docs_chunks WHERE doc_id = @doc AND chunk_num BETWEEN @min AND @max ORDER BY chunk_num";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("doc", docId);
            cmd.Parameters.AddWithValue("min", minChunk);
            cmd.Parameters.AddWithValue("max", maxChunk);
            var list = new List<Source>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var dId = reader.GetString(0);
                var filename = reader.GetString(1);
                var pType = reader.GetString(2);
                var pName = reader.GetString(3);
                var cNum = reader.GetInt32(4);
                var text = reader.GetString(5);
                var dist = 0.0; // distance not meaningful here
                var citation = $"[{pType}/{pName}:{filename}#chunk{cNum}]";
                list.Add(new Source(dId, filename, cNum, text, dist, citation, pType, pName));
            }
            await reader.CloseAsync();
            result[docId] = list;
        }
        return result;
    }
}
