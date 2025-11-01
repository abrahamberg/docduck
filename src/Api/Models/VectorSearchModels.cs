namespace Api.Models;

/// <summary>
/// Internal search parameters for vector search execution
/// </summary>
internal record SearchParameters(
    int Depth,
    int K,
    string NormalizedQuery,
    bool LexicalEnabled,
    bool LexicalOnly,
    int LexicalLimit
);

/// <summary>
/// Internal model for lexical search match with source and rank
/// </summary>
internal sealed record LexicalMatch(Source Source, double Rank);

/// <summary>
/// Internal class for tracking combined candidate scores
/// </summary>
internal sealed class CandidateScore
{
    public Source Source { get; set; }
    public double? VectorDistance { get; set; }
    public double VectorScore { get; set; }
    public double LexicalScore { get; set; }

    public CandidateScore(Source source)
    {
        Source = source;
    }
}
