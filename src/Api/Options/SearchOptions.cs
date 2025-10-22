namespace Api.Options;

/// <summary>
/// Configuration for vector search.
/// </summary>
public class SearchOptions
{
    public const string SectionName = "Search";

    /// <summary>
    /// Default number of chunks to retrieve for RAG context.
    /// </summary>
    public int DefaultTopK { get; set; } = 8;

    /// <summary>
    /// Maximum allowed TopK value.
    /// </summary>
    public int MaxTopK { get; set; } = 20;

    /// <summary>
    /// Default search depth level (1-5) controlling retrieval orchestration effort.
    /// </summary>
    public int DefaultSearchDepth { get; set; } = 3;

    /// <summary>
    /// Maximum allowed search depth level.
    /// </summary>
    public int MaxSearchDepth { get; set; } = 5;

    /// <summary>
    /// Enables the lexical component of hybrid search.
    /// </summary>
    public bool EnableLexicalSearch { get; set; } = true;

    /// <summary>
    /// Weight given to lexical scores when blending with vector similarity (0-1).
    /// </summary>
    public double LexicalScoreWeight { get; set; } = 0.35;

    /// <summary>
    /// Maximum lexical matches to retrieve before blending.
    /// </summary>
    public int MaxLexicalResults { get; set; } = 40;

    /// <summary>
    /// PostgreSQL text search configuration used for lexical queries.
    /// </summary>
    public string LexicalConfiguration { get; set; } = "simple";
}
