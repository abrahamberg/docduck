namespace Api.Models;

/// <summary>
/// Represents a chunk of text within a search finding.
/// </summary>
public sealed record ChunkInfo(
    string ChunkId,
    int ChunkNum,
    double Distance,
    string Text,
    List<string>? MatchedKeywords = null
);

/// <summary>
/// Represents a context chunk (first N chunks of document for context).
/// </summary>
public sealed record ContextChunk(
    string ChunkId,
    int ChunkNum,
    string Text
);

/// <summary>
/// Represents a document finding with all its matched chunks and context.
/// </summary>
public sealed record SearchFinding(
    string DocId,
    string Filename,
    string ProviderType,
    string ProviderName,
    int Strength,
    string Comment,
    double Distance,
    List<string>? Keywords,
    int ChunkCount,
    List<ChunkInfo> Chunks,
    List<ContextChunk>? ContextChunks = null
)
{
    /// <summary>
    /// Validates the finding data.
    /// </summary>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(DocId)
            && !string.IsNullOrWhiteSpace(Filename)
            && Strength is >= 0 and <= 100
            && Comment.Length <= 300
            && ChunkCount >= 0
            && Chunks.Count > 0;
    }
}

/// <summary>
/// Represents a single step in the multi-agent search process.
/// </summary>
public sealed record SearchStep(
    string StepName,
    List<SearchFinding> Findings,
    string? Language,
    string LookingFor,
    List<string> Keywords,
    string Phrase,
    string? DocType,
    string StepPrompt
)
{
    /// <summary>
    /// Gets the total number of documents found in this step.
    /// </summary>
    public int DocumentCount => Findings.Select(f => f.DocId).Distinct().Count();

    /// <summary>
    /// Gets the total number of chunks across all findings.
    /// </summary>
    public int TotalChunkCount => Findings.Sum(f => f.ChunkCount);
}

/// <summary>
/// Complete state of a multi-step search operation.
/// </summary>
public sealed record SearchState(
    string OriginalPrompt,
    List<SearchStep> Steps,
    DateTime CreatedAt,
    DateTime? CompletedAt = null,
    string Status = "in_progress"
)
{
    /// <summary>
    /// Gets all unique documents across all steps.
    /// </summary>
    public HashSet<string> AllDocumentIds =>
        Steps.SelectMany(s => s.Findings.Select(f => f.DocId)).ToHashSet();

    /// <summary>
    /// Gets all findings from all steps, flattened.
    /// </summary>
    public List<SearchFinding> GetAllFindings() =>
        Steps.SelectMany(s => s.Findings).ToList();

    /// <summary>
    /// Gets the highest strength finding.
    /// </summary>
    public SearchFinding? TopFinding =>
        GetAllFindings().OrderByDescending(f => f.Strength).FirstOrDefault();
}

/// <summary>
/// Request model for multi-step search endpoint.
/// </summary>
public sealed record MultiStepSearchRequest(
    string Query,
    int? MaxSteps = null,
    int? TopK = null,
    string? ProviderType = null,
    string? ProviderName = null
);

/// <summary>
/// Response model for multi-step search endpoint.
/// </summary>
public sealed record MultiStepSearchResponse(
    string SearchId,
    SearchState State,
    List<SearchFinding> FinalFindings,
    int TotalDocuments,
    int TotalChunks,
    TimeSpan Duration,
    List<string> ThinkingSteps
);

/// <summary>
/// Raw search result from a single search strategy (vector, lexical, or keyword).
/// </summary>
public sealed record RawSearchResult(
    string DocId,
    string Filename,
    string ProviderType,
    string ProviderName,
    int ChunkNum,
    string Text,
    double Distance,
    double Score,
    List<string>? MatchedKeywords = null,
    string SearchStrategy = "unknown"
);

/// <summary>
/// Aggregated document finding before final scoring.
/// </summary>
public sealed record AggregatedDocument(
    string DocId,
    string Filename,
    string ProviderType,
    string ProviderName,
    List<RawSearchResult> MatchedChunks,
    List<ContextChunk> ContextChunks,
    double BestDistance,
    double AverageDistance,
    int ChunkCount,
    List<string> AllMatchedKeywords
);
