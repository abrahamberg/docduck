namespace Api.Models;

/// <summary>
/// Internal context for tracking search execution state.
/// </summary>
internal sealed record SearchContext(
    string SearchId,
    string OriginalQuery,
    DateTime StartTime,
    int MaxDepth,
    int TopK,
    string? ProviderType,
    string? ProviderName
);

/// <summary>
/// Result of executing a single search step.
/// </summary>
internal sealed record SearchStepResult
{
    public SearchStep? Step { get; init; }
    public bool ShouldStop { get; init; }
    public bool ShouldRefine { get; init; }
    public string? RefinedQuery { get; init; }

    public static SearchStepResult Stop() => new() { ShouldStop = true };

    public static SearchStepResult Continue(SearchStep step, bool shouldRefine, string? refinedQuery) => new()
    {
        Step = step,
        ShouldStop = false,
        ShouldRefine = shouldRefine,
        RefinedQuery = refinedQuery
    };
}
