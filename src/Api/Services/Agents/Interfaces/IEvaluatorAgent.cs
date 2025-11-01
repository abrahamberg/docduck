using Api.Models;
using Api.Services.Agents.Models;

namespace Api.Services.Agents.Interfaces;

/// <summary>
/// Evaluator agent: scores and comments on search findings.
/// </summary>
public interface IEvaluatorAgent
{
    Task<List<SearchFinding>> EvaluateAsync(
        SearchPlan plan,
        List<RawSearchResult> rawResults,
        CancellationToken ct = default);
}
