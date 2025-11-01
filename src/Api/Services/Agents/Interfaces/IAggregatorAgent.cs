using Api.Models;

namespace Api.Services.Agents.Interfaces;

/// <summary>
/// Aggregator agent: merges, deduplicates, and ranks final results.
/// </summary>
public interface IAggregatorAgent
{
    Task<List<SearchFinding>> AggregateAsync(
        List<SearchStep> steps,
        CancellationToken ct = default);
}
