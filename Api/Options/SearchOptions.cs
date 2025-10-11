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
}
