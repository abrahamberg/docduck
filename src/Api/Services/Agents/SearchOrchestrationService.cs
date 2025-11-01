using Api.Models;
using Api.Options;
using Api.Services.Agents.Interfaces;
using Api.Services.Agents.Models;
using DocDuck.Providers.Ai;
using Microsoft.Extensions.Options;

namespace Api.Services.Agents;

public sealed class SearchOrchestrationService(
    IQueryPlannerAgent queryPlanner,
    ISearcherAgent searcher,
    IEvaluatorAgent evaluator,
    IAggregatorAgent aggregator,
    IRefinementAgent refinement,
    IOptions<SearchOptions> searchOptions,
    ILogger<SearchOrchestrationService> logger) : ISearchOrchestrationService
{
    public Task<MultiStepSearchResponse> ExecuteSearchAsync(
        MultiStepSearchRequest request,
        CancellationToken ct = default)
    {
        return ExecuteSearchInternalAsync(request, null, ct);
    }

    public Task<MultiStepSearchResponse> ExecuteSearchAsync(
        MultiStepSearchRequest request,
        Func<string, Task> onThinkingStep,
        CancellationToken ct = default)
    {
        return ExecuteSearchInternalAsync(request, onThinkingStep, ct);
    }

    private async Task<MultiStepSearchResponse> ExecuteSearchInternalAsync(
        MultiStepSearchRequest request,
        Func<string, Task>? onThinkingStep,
        CancellationToken ct)
    {
        var searchContext = InitializeSearchContext(request);
        var thinkingSteps = new List<string>();

        LogSearchStart(searchContext);

        var steps = await ExecuteSearchStepsAsync(request, searchContext, thinkingSteps, onThinkingStep, ct);

        return await BuildFinalResponseAsync(searchContext, steps, thinkingSteps, onThinkingStep, ct);
    }

    private SearchContext InitializeSearchContext(MultiStepSearchRequest request)
    {
        return new SearchContext(
            SearchId: Guid.NewGuid().ToString(),
            OriginalQuery: request.Query,
            StartTime: DateTime.UtcNow,
            MaxDepth: request.MaxSteps ?? searchOptions.Value.DefaultSearchDepth,
            TopK: request.TopK ?? searchOptions.Value.DefaultTopK,
            ProviderType: request.ProviderType,
            ProviderName: request.ProviderName
        );
    }

    private void LogSearchStart(SearchContext context)
    {
        logger.LogInformation(
            "Starting multi-step search: {Query} (ID: {SearchId}, maxDepth: {MaxDepth})",
            context.OriginalQuery,
            context.SearchId,
            context.MaxDepth);
    }

    private async Task<List<SearchStep>> ExecuteSearchStepsAsync(
        MultiStepSearchRequest request,
        SearchContext context,
        List<string> thinkingSteps,
        Func<string, Task>? onThinkingStep,
        CancellationToken ct)
    {
        var steps = new List<SearchStep>();
        var currentQuery = request.Query;
        var currentDepth = 0;

        while (ShouldContinueSearching(currentDepth, context.MaxDepth))
        {
            currentDepth++;

            var stepResult = await ExecuteSingleSearchStepAsync(
                currentQuery,
                currentDepth,
                context,
                steps,
                thinkingSteps,
                onThinkingStep,
                ct);

            if (stepResult.ShouldStop)
            {
                break;
            }

            steps.Add(stepResult.Step!);

            if (!stepResult.ShouldRefine)
            {
                break;
            }

            currentQuery = stepResult.RefinedQuery ?? currentQuery;
        }

        return steps;
    }

    private static bool ShouldContinueSearching(int currentDepth, int maxDepth)
    {
        return currentDepth < maxDepth;
    }

    private async Task<SearchStepResult> ExecuteSingleSearchStepAsync(
        string query,
        int depth,
        SearchContext context,
        List<SearchStep> previousSteps,
        List<string> thinkingSteps,
        Func<string, Task>? onThinkingStep,
        CancellationToken ct)
    {
        await AddThinkingStepAsync($"🤔 Analyzing query: \"{query}\"", thinkingSteps, onThinkingStep, depth == 1);

        var plan = await queryPlanner.PlanSearchAsync(query, ct);

        await RecordQueryPlanAsync(plan, depth, thinkingSteps, onThinkingStep);

        var rawResults = await ExecuteParallelSearchesAsync(plan, context, depth, thinkingSteps, onThinkingStep, ct);

        if (IsEmptyResults(rawResults, depth))
        {
            await RecordNoResultsAsync(depth, thinkingSteps, onThinkingStep);
            return SearchStepResult.Stop();
        }

        var evaluatedFindings = await EvaluateAndRecordFindingsAsync(plan, rawResults, depth, thinkingSteps, onThinkingStep, ct);

        var step = CreateSearchStep(plan, evaluatedFindings, depth);

        var refinementDecision = await CheckRefinementNeededAsync(
            context.OriginalQuery,
            previousSteps,
            step,
            depth,
            context.MaxDepth,
            thinkingSteps,
            onThinkingStep,
            ct);

        return SearchStepResult.Continue(step, refinementDecision.ShouldContinue, refinementDecision.RefinedQuery);
    }

    private async Task AddThinkingStepAsync(
        string message,
        List<string> thinkingSteps,
        Func<string, Task>? onThinkingStep,
        bool condition = true)
    {
        if (!condition) return;

        thinkingSteps.Add(message);

        if (onThinkingStep != null)
        {
            await onThinkingStep(message);
        }
    }

    private async Task RecordQueryPlanAsync(
        SearchPlan plan,
        int depth,
        List<string> thinkingSteps,
        Func<string, Task>? onThinkingStep)
    {
        if (depth != 1) return;

        await AddThinkingStepAsync($"📋 Query plan created:", thinkingSteps, onThinkingStep);
        await AddThinkingStepAsync($"   • Search phrase: \"{plan.Phrase}\"", thinkingSteps, onThinkingStep);
        await AddThinkingStepAsync($"   • Keywords: [{string.Join(", ", plan.Keywords)}]", thinkingSteps, onThinkingStep);
        await AddThinkingStepAsync($"   • Looking for: {plan.LookingFor}", thinkingSteps, onThinkingStep);
        await AddThinkingStepAsync($"   • Document type: {plan.DocType}", thinkingSteps, onThinkingStep, plan.DocType != null);
        await AddThinkingStepAsync($"   • Language: {plan.Language}", thinkingSteps, onThinkingStep, plan.Language != null);

        logger.LogInformation(
            "Step {Depth}: keywords=[{Keywords}], phrase=\"{Phrase}\"",
            depth,
            string.Join(", ", plan.Keywords),
            plan.Phrase);
    }

    private async Task<List<RawSearchResult>> ExecuteParallelSearchesAsync(
        SearchPlan plan,
        SearchContext context,
        int depth,
        List<string> thinkingSteps,
        Func<string, Task>? onThinkingStep,
        CancellationToken ct)
    {
        var stepPrefix = GetStepPrefix(depth);
        await AddThinkingStepAsync($"{stepPrefix}: Executing parallel searches (vector + keyword)...", thinkingSteps, onThinkingStep);

        var rawResults = await searcher.SearchAsync(
            plan,
            context.TopK,
            context.ProviderType,
            context.ProviderName,
            ct);

        await RecordSearchResultsAsync(rawResults, plan, depth, thinkingSteps, onThinkingStep);

        return rawResults;
    }

    private static string GetStepPrefix(int depth)
    {
        return depth == 1 ? "🔍 Step 1" : $"🔄 Step {depth} (Refinement)";
    }

    private async Task RecordSearchResultsAsync(
        List<RawSearchResult> rawResults,
        SearchPlan plan,
        int depth,
        List<string> thinkingSteps,
        Func<string, Task>? onThinkingStep)
    {
        var vectorResults = FilterResultsByStrategy(rawResults, "vector");
        var keywordResults = FilterResultsByKeywordStrategies(rawResults);

        await RecordVectorSearchResultsAsync(vectorResults, plan.Phrase, thinkingSteps, onThinkingStep);
        await RecordKeywordSearchResultsAsync(keywordResults, plan.Keywords, thinkingSteps, onThinkingStep);

        logger.LogInformation(
            "Step {Depth}: found {Count} raw results (vector: {Vector}, keyword: {Keyword})",
            depth,
            rawResults.Count,
            vectorResults.Count,
            keywordResults.Count);
    }

    private static List<RawSearchResult> FilterResultsByStrategy(List<RawSearchResult> results, string strategy)
    {
        return results.Where(r => r.SearchStrategy == strategy).ToList();
    }

    private static List<RawSearchResult> FilterResultsByKeywordStrategies(List<RawSearchResult> results)
    {
        return results.Where(r => r.SearchStrategy is "keyword" or "pattern").ToList();
    }

    private async Task RecordVectorSearchResultsAsync(
        List<RawSearchResult> results,
        string phrase,
        List<string> thinkingSteps,
        Func<string, Task>? onThinkingStep)
    {
        await AddThinkingStepAsync($"   • Vector search: \"{phrase}\"", thinkingSteps, onThinkingStep);

        if (results.Count == 0)
        {
            await AddThinkingStepAsync("     (no results)", thinkingSteps, onThinkingStep);
            return;
        }

        var selectedChunks = SelectRelevantChunks(results, "vector");
        await RecordSelectedChunksAsync(selectedChunks, thinkingSteps, onThinkingStep, includeAdaptiveNote: true);
    }

    private async Task RecordKeywordSearchResultsAsync(
        List<RawSearchResult> results,
        List<string> keywords,
        List<string> thinkingSteps,
        Func<string, Task>? onThinkingStep)
    {
        await AddThinkingStepAsync(
            $"   • Keyword search: {keywords.Count} keywords ({string.Join(", ", keywords)})",
            thinkingSteps,
            onThinkingStep);

        if (results.Count == 0)
        {
            await AddThinkingStepAsync("     (no results)", thinkingSteps, onThinkingStep);
            return;
        }

        var selectedChunks = SelectRelevantChunks(results, "keyword");
        await RecordSelectedChunksAsync(selectedChunks, thinkingSteps, onThinkingStep, includeAdaptiveNote: false);
    }

    private async Task RecordSelectedChunksAsync(
        List<RawSearchResult> chunks,
        List<string> thinkingSteps,
        Func<string, Task>? onThinkingStep,
        bool includeAdaptiveNote)
    {
        var fileGroups = chunks.GroupBy(r => r.Filename).OrderBy(g => g.Min(r => r.Distance));
        var fileCount = fileGroups.Count();

        var summaryNote = includeAdaptiveNote
            ? $"     Selected {chunks.Count} chunks from {fileCount} files (adaptive based on distance clustering)"
            : $"     Selected {chunks.Count} chunks from {fileCount} files";

        await AddThinkingStepAsync(summaryNote, thinkingSteps, onThinkingStep);

        foreach (var fileGroup in fileGroups)
        {
            await RecordFileGroupDetailsAsync(fileGroup, thinkingSteps, onThinkingStep);
        }
    }

    private async Task RecordFileGroupDetailsAsync(
        IGrouping<string, RawSearchResult> fileGroup,
        List<string> thinkingSteps,
        Func<string, Task>? onThinkingStep)
    {
        var chunks = fileGroup.OrderBy(r => r.Distance).ToList();
        var firstChunk = chunks[0];

        var fileInfo = BuildFileGroupSummary(fileGroup.Key, chunks, firstChunk);
        await AddThinkingStepAsync(fileInfo, thinkingSteps, onThinkingStep);

        await RecordTopChunksAsync(chunks, thinkingSteps, onThinkingStep);
    }

    private static string BuildFileGroupSummary(string filename, List<RawSearchResult> chunks, RawSearchResult firstChunk)
    {
        var chunkCount = chunks.Count;
        var bestDistance = firstChunk.Distance;

        if (firstChunk.SearchStrategy is "keyword" or "pattern")
        {
            var keywords = FormatMatchedKeywords(firstChunk.MatchedKeywords);
            var strategy = firstChunk.SearchStrategy == "pattern" ? "(pattern)" : "(fts)";
            return $"     - {filename}: {chunkCount} chunks {strategy} - keywords: {keywords}";
        }

        return $"     - {filename}: {chunkCount} chunks, best distance: {bestDistance:F3}";
    }

    private static string FormatMatchedKeywords(List<string>? keywords)
    {
        return keywords?.Count > 0 ? string.Join(", ", keywords) : "all";
    }

    private async Task RecordTopChunksAsync(
        List<RawSearchResult> chunks,
        List<string> thinkingSteps,
        Func<string, Task>? onThinkingStep)
    {
        const int maxChunksToShow = 3;
        var chunksToShow = chunks.Take(maxChunksToShow);

        foreach (var chunk in chunksToShow)
        {
            var chunkInfo = FormatChunkInfo(chunk);
            await AddThinkingStepAsync(chunkInfo, thinkingSteps, onThinkingStep);
        }

        var remainingCount = chunks.Count - maxChunksToShow;
        if (remainingCount > 0)
        {
            await AddThinkingStepAsync($"         ... and {remainingCount} more chunks", thinkingSteps, onThinkingStep);
        }
    }

    private static string FormatChunkInfo(RawSearchResult chunk)
    {
        var keywordInfo = chunk.MatchedKeywords?.Count > 0
            ? $" - keywords: {string.Join(", ", chunk.MatchedKeywords)}"
            : "";

        return $"         chunk {chunk.ChunkNum}{keywordInfo} - distance: {chunk.Distance:F3}";
    }

    private static bool IsEmptyResults(List<RawSearchResult> results, int depth)
    {
        return results.Count == 0;
    }

    private async Task RecordNoResultsAsync(
        int depth,
        List<string> thinkingSteps,
        Func<string, Task>? onThinkingStep)
    {
        await AddThinkingStepAsync($"   ⚠️  No new results found in step {depth}", thinkingSteps, onThinkingStep);

        if (depth == 1)
        {
            await AddThinkingStepAsync("❌ No matching chunks found in the database", thinkingSteps, onThinkingStep);
            await AddThinkingStepAsync("💡 Try rephrasing your query or check if documents are indexed", thinkingSteps, onThinkingStep);
        }
    }

    private async Task<List<SearchFinding>> EvaluateAndRecordFindingsAsync(
        SearchPlan plan,
        List<RawSearchResult> rawResults,
        int depth,
        List<string> thinkingSteps,
        Func<string, Task>? onThinkingStep,
        CancellationToken ct)
    {
        await AddThinkingStepAsync($"   ⚖️  Aggregating {rawResults.Count} chunks into documents...", thinkingSteps, onThinkingStep);

        var findings = await evaluator.EvaluateAsync(plan, rawResults, ct);

        await RecordEvaluatedFindingsAsync(findings, depth, thinkingSteps, onThinkingStep);

        return findings;
    }

    private async Task RecordEvaluatedFindingsAsync(
        List<SearchFinding> findings,
        int depth,
        List<string> thinkingSteps,
        Func<string, Task>? onThinkingStep)
    {
        var candidateDocs = findings.Select(f => f.DocId).Distinct().Count();
        await AddThinkingStepAsync($"   • Aggregated into {candidateDocs} documents:", thinkingSteps, onThinkingStep);

        foreach (var finding in findings.OrderByDescending(f => f.Strength).Take(10))
        {
            await RecordFindingDetailsAsync(finding, thinkingSteps, onThinkingStep);
        }

        logger.LogInformation(
            "Step {Depth}: produced {Count} findings from {Docs} candidate documents",
            depth,
            findings.Count,
            candidateDocs);
    }

    private async Task RecordFindingDetailsAsync(
        SearchFinding finding,
        List<string> thinkingSteps,
        Func<string, Task>? onThinkingStep)
    {
        await AddThinkingStepAsync($"     - {finding.Filename} (strength: {finding.Strength})", thinkingSteps, onThinkingStep);

        foreach (var chunk in finding.Chunks.OrderBy(c => c.Distance).Take(5))
        {
            var chunkInfo = FormatFindingChunkInfo(chunk);
            await AddThinkingStepAsync(chunkInfo, thinkingSteps, onThinkingStep);
        }
    }

    private static string FormatFindingChunkInfo(ChunkInfo chunk)
    {
        var keywordInfo = chunk.MatchedKeywords?.Count > 0
            ? $" - keywords: {string.Join(", ", chunk.MatchedKeywords)}"
            : "";

        return $"       • chunk {chunk.ChunkNum}{keywordInfo} - distance: {chunk.Distance:F3}";
    }

    private static SearchStep CreateSearchStep(SearchPlan plan, List<SearchFinding> findings, int depth)
    {
        var stepName = depth == 1 ? "initial_search" : $"refinement_{depth}";
        var stepPrompt = depth == 1 ? $"Search for: {plan.Phrase}" : $"Refined search: {plan.Phrase}";

        return new SearchStep(
            StepName: stepName,
            Findings: findings,
            Language: plan.Language,
            LookingFor: plan.LookingFor,
            Keywords: plan.Keywords,
            Phrase: plan.Phrase,
            DocType: plan.DocType,
            StepPrompt: stepPrompt
        );
    }

    private async Task<AgentRefinementDecision> CheckRefinementNeededAsync(
        string originalQuery,
        List<SearchStep> previousSteps,
        SearchStep currentStep,
        int currentDepth,
        int maxDepth,
        List<string> thinkingSteps,
        Func<string, Task>? onThinkingStep,
        CancellationToken ct)
    {
        var allSteps = previousSteps.Concat(new[] { currentStep }).ToList();

        var decision = await refinement.ShouldRefineAsync(
            originalQuery,
            allSteps,
            currentDepth,
            maxDepth,
            ct);

        await AddThinkingStepAsync($"   🤔 Refinement check: {decision.Reason}", thinkingSteps, onThinkingStep);

        logger.LogInformation(
            "Step {Depth}: Refinement decision - Continue: {Continue}, Reason: {Reason}",
            currentDepth,
            decision.ShouldContinue,
            decision.Reason);

        return decision;
    }

    private async Task<MultiStepSearchResponse> BuildFinalResponseAsync(
        SearchContext context,
        List<SearchStep> steps,
        List<string> thinkingSteps,
        Func<string, Task>? onThinkingStep,
        CancellationToken ct)
    {
        if (steps.Count == 0)
        {
            return CreateEmptyResponse(context.SearchId, context.OriginalQuery, context.StartTime, thinkingSteps);
        }

        await AddThinkingStepAsync($"🔄 Aggregating results (deduplication + ranking)...", thinkingSteps, onThinkingStep);

        var finalFindings = await aggregator.AggregateAsync(steps, ct);

        await RecordFinalFindingsAsync(finalFindings, thinkingSteps, onThinkingStep);

        return CreateSuccessResponse(context, steps, finalFindings, thinkingSteps);
    }

    private async Task RecordFinalFindingsAsync(
        List<SearchFinding> findings,
        List<string> thinkingSteps,
        Func<string, Task>? onThinkingStep)
    {
        await AddThinkingStepAsync($"   • Final findings: {findings.Count} document(s)", thinkingSteps, onThinkingStep);
        await AddThinkingStepAsync($"   • Total unique chunks: {findings.Sum(f => f.ChunkCount)}", thinkingSteps, onThinkingStep);

        logger.LogInformation("Aggregator produced {Count} final findings", findings.Count);
    }

    private MultiStepSearchResponse CreateSuccessResponse(
        SearchContext context,
        List<SearchStep> steps,
        List<SearchFinding> finalFindings,
        List<string> thinkingSteps)
    {
        var completedAt = DateTime.UtcNow;
        var duration = completedAt - context.StartTime;
        var totalDocs = finalFindings.Select(f => f.DocId).Distinct().Count();
        var totalChunks = finalFindings.Sum(f => f.ChunkCount);

        thinkingSteps.Add($"✅ Search completed in {duration.TotalMilliseconds:F0}ms");

        logger.LogInformation(
            "Multi-step search completed in {Duration}ms: {Docs} docs, {Chunks} chunks",
            duration.TotalMilliseconds,
            totalDocs,
            totalChunks);

        var state = new SearchState(
            OriginalPrompt: context.OriginalQuery,
            Steps: steps,
            CreatedAt: context.StartTime,
            CompletedAt: completedAt,
            Status: "completed"
        );

        return new MultiStepSearchResponse(
            SearchId: context.SearchId,
            State: state,
            FinalFindings: finalFindings,
            TotalDocuments: totalDocs,
            TotalChunks: totalChunks,
            Duration: duration,
            ThinkingSteps: thinkingSteps
        );
    }

    private static MultiStepSearchResponse CreateEmptyResponse(
        string searchId,
        string query,
        DateTime startTime,
        List<string> thinkingSteps)
    {
        var state = new SearchState(
            OriginalPrompt: query,
            Steps: [],
            CreatedAt: startTime,
            CompletedAt: DateTime.UtcNow,
            Status: "completed"
        );

        return new MultiStepSearchResponse(
            SearchId: searchId,
            State: state,
            FinalFindings: [],
            TotalDocuments: 0,
            TotalChunks: 0,
            Duration: DateTime.UtcNow - startTime,
            ThinkingSteps: thinkingSteps
        );
    }

    /// <summary>
    /// Adaptively select relevant chunks based on distance clustering and file diversity.
    /// Strategy:
    /// - If many chunks from same file: take more chunks (up to 20-30)
    /// - If big distance gap exists: only take chunks before the gap
    /// - Min 5 chunks, max 30 chunks
    /// </summary>
    private List<RawSearchResult> SelectRelevantChunks(List<RawSearchResult> results, string searchType)
    {
        if (results.Count == 0) return results;

        var sorted = results.OrderBy(r => r.Distance).ToList();

        // Calculate distance gaps to find natural cutoff points
        var gaps = new List<(int index, double gap)>();
        for (int i = 1; i < sorted.Count && i < 30; i++)
        {
            var gap = sorted[i].Distance - sorted[i - 1].Distance;
            gaps.Add((i, gap));
        }

        // Find significant gap (>0.15 difference) after at least 5 results
        var significantGap = gaps
            .Where(g => g.index >= 5 && g.gap > 0.15)
            .OrderByDescending(g => g.gap)
            .FirstOrDefault();

        int cutoffIndex;
        if (significantGap != default)
        {
            // Cut at significant gap
            cutoffIndex = significantGap.index;
        }
        else
        {
            // No significant gap: check file diversity
            var topChunks = sorted.Take(20).ToList();
            var filesInTop = topChunks.GroupBy(r => r.Filename).Count();

            // If many chunks from few files (e.g., 10+ chunks from 2-3 files), take more
            if (filesInTop <= 3 && topChunks.Count >= 10)
            {
                cutoffIndex = Math.Min(30, sorted.Count);
            }
            // If diverse files, take fewer chunks
            else if (filesInTop >= topChunks.Count / 2)
            {
                cutoffIndex = Math.Min(15, sorted.Count);
            }
            else
            {
                cutoffIndex = Math.Min(20, sorted.Count);
            }
        }

        return sorted.Take(cutoffIndex).ToList();
    }
}
