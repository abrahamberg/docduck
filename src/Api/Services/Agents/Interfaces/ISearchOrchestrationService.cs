using Api.Models;

namespace Api.Services.Agents.Interfaces;

/// <summary>
/// Orchestrates multi-agent search workflow: query planner → searcher → evaluator → aggregator.
/// </summary>
public interface ISearchOrchestrationService
{
    /// <summary>
    /// Execute a multi-step search with optional refinement.
    /// </summary>
    Task<MultiStepSearchResponse> ExecuteSearchAsync(
        MultiStepSearchRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Execute a multi-step search with real-time thinking step callbacks.
    /// </summary>
    Task<MultiStepSearchResponse> ExecuteSearchAsync(
        MultiStepSearchRequest request,
        Func<string, Task> onThinkingStep,
        CancellationToken ct = default);
}
