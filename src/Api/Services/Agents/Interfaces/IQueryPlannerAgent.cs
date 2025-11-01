using Api.Services.Agents.Models;

namespace Api.Services.Agents.Interfaces;

/// <summary>
/// Query planner agent: analyzes user query and creates a search plan.
/// </summary>
public interface IQueryPlannerAgent
{
    Task<SearchPlan> PlanSearchAsync(string query, CancellationToken ct = default);
}
