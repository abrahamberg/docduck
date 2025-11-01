using Api.Models;
using Api.Services.Agents.Interfaces;
using Api.Services.Agents.Models;
using DocDuck.Providers.Ai;

namespace Api.Services.Agents;

public sealed class RefinementAgent : IRefinementAgent
{
    private readonly IModelAgnosticAiService _aiService;
    private readonly ILogger<RefinementAgent> _logger;

    public RefinementAgent(
        IModelAgnosticAiService aiService,
        ILogger<RefinementAgent> logger)
    {
        _aiService = aiService;
        _logger = logger;
    }

    public async Task<AgentRefinementDecision> ShouldRefineAsync(
        string originalQuery,
        List<SearchStep> steps,
        int currentDepth,
        int maxDepth,
        CancellationToken ct = default)
    {
        // Don't refine if we've reached max depth
        if (currentDepth >= maxDepth)
        {
            _logger.LogInformation("Max depth {MaxDepth} reached, stopping refinement", maxDepth);
            return new AgentRefinementDecision(
                ShouldContinue: false,
                Reason: $"Maximum search depth ({maxDepth}) reached"
            );
        }

        // Don't refine if we have no results to work with
        if (steps.Count == 0 || steps.All(s => s.Findings.Count == 0))
        {
            _logger.LogInformation("No findings to refine, stopping");
            return new AgentRefinementDecision(
                ShouldContinue: false,
                Reason: "No results found to refine"
            );
        }

        // Get current findings
        var allFindings = steps.SelectMany(s => s.Findings).ToList();
        var topStrength = allFindings.Max(f => f.Strength);
        var avgStrength = allFindings.Average(f => f.Strength);
        var uniqueDocs = allFindings.Select(f => f.DocId).Distinct().Count();

        // If we have strong results, we might not need refinement
        if (topStrength >= 80 && uniqueDocs >= 3)
        {
            _logger.LogInformation(
                "Strong results found (strength: {Top}, docs: {Docs}), no refinement needed",
                topStrength, uniqueDocs);
            return new AgentRefinementDecision(
                ShouldContinue: false,
                Reason: $"Strong results already found (strength: {topStrength}, {uniqueDocs} documents)"
            );
        }

        // If results are weak or limited, try refinement
        if (topStrength < 60 || uniqueDocs < 2)
        {
            _logger.LogInformation(
                "Weak results (strength: {Avg}, docs: {Docs}), attempting refinement",
                avgStrength, uniqueDocs);

            var refinedQuery = await GenerateRefinedQueryAsync(
                originalQuery,
                steps,
                ct);

            return new AgentRefinementDecision(
                ShouldContinue: true,
                Reason: $"Results are limited (strength: {topStrength:F0}, {uniqueDocs} docs) - refining search",
                RefinedQuery: refinedQuery
            );
        }

        // Moderate results - try one more refinement if we have depth available
        if (currentDepth < maxDepth - 1)
        {
            var refinedQuery = await GenerateRefinedQueryAsync(
                originalQuery,
                steps,
                ct);

            return new AgentRefinementDecision(
                ShouldContinue: true,
                Reason: $"Attempting to find additional relevant documents (current: {uniqueDocs} docs)",
                RefinedQuery: refinedQuery
            );
        }

        return new AgentRefinementDecision(
            ShouldContinue: false,
            Reason: $"Sufficient results found ({uniqueDocs} documents)"
        );
    }

    private async Task<string> GenerateRefinedQueryAsync(
        string originalQuery,
        List<SearchStep> steps,
        CancellationToken ct)
    {
        var lastStep = steps.LastOrDefault();
        if (lastStep == null)
        {
            return originalQuery;
        }

        // Build context from previous findings
        var topFindings = steps
            .SelectMany(s => s.Findings)
            .OrderByDescending(f => f.Strength)
            .Take(3)
            .ToList();

        var findingsContext = string.Join("\n", topFindings.Select((f, i) =>
            $"{i + 1}. {f.Filename} (strength: {f.Strength}) - Keywords: {string.Join(", ", f.Keywords ?? new List<string>())}"));

        var systemPrompt = @"You are a search refinement expert. Generate a NATURAL LANGUAGE search query for semantic vector search.

CRITICAL RULES:
1. OUTPUT MUST BE NATURAL LANGUAGE - NOT boolean queries, NOT SQL syntax
2. NO boolean operators like AND, OR, parentheses - use natural connecting words instead
3. PRESERVE the original language (Swedish stays Swedish, English stays English)
4. Expand on aspects not well covered in previous results
5. Use different terminology or perspectives while maintaining core intent
6. Keep it concise and readable - a human would say these words

GOOD Examples:
- ""contract in progress CV maker offering website maintenance""
- ""hemsida underhåll kontrakt pågående arbete CV-verktyg""
- ""deployment best practices continuous integration automation""

BAD Examples (DO NOT use):
- ""(contract OR agreement) AND (CV maker OR resume builder)"" ❌ NO BOOLEAN
- ""contract | status & CV-maker"" ❌ NO OPERATORS
- ""SELECT * FROM contracts"" ❌ NO SQL

Respond with ONLY the natural language refined query, nothing else.";

        var userPrompt = $@"Original query: ""{originalQuery}""

Previous search found these documents:
{findingsContext}

Generate a refined query that explores different angles or uses alternative terminology to find more relevant information.";

        try
        {
            var messages = new List<ChatMessagePayload>
            {
                new("system", systemPrompt),
                new("user", userPrompt)
            };

            var result = await _aiService.CompleteChatAsync(
                messages,
                TaskComplexity.Simple,
                strategy: null,
                options: null,
                ct: ct);

            var refinedQuery = result.Content?.Trim().Trim('"') ?? originalQuery;
            _logger.LogInformation("Generated refined query: {Query}", refinedQuery);
            return refinedQuery;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate refined query, using original");
            return originalQuery;
        }
    }
}
