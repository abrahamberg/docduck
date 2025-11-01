namespace Api.Services.Agents.Models;

/// <summary>
/// Decision about whether to continue refining the agent-driven search.
/// </summary>
public sealed record AgentRefinementDecision(
    bool ShouldContinue,
    string? Reason,
    string? RefinedQuery = null,
    List<string>? FocusKeywords = null
);
