using Api.Models;
using Api.Services.Agents.Models;

namespace Api.Services.Agents.Interfaces;

/// <summary>
/// Searcher agent: executes parallel searches using multiple strategies.
/// </summary>
public interface ISearcherAgent
{
    Task<List<RawSearchResult>> SearchAsync(
        SearchPlan plan,
        int topK,
        string? providerType,
        string? providerName,
        CancellationToken ct = default);
}
