using Api.Models;
using Api.Services.Agents.Models;

namespace Api.Services.Agents.Interfaces;

/// <summary>
/// Refinement agent: decides if search should continue with modifications.
/// </summary>
public interface IRefinementAgent
{
    Task<AgentRefinementDecision> ShouldRefineAsync(
        string originalQuery,
        List<SearchStep> steps,
        int currentDepth,
        int maxDepth,
        CancellationToken ct = default);
}
