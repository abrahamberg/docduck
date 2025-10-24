using Api.Models;
using Api.Options;
using System.Linq;
using Microsoft.Extensions.Options;

namespace Api.Services;

/// <summary>
/// Orchestrates multi-step chat interaction with LLM-driven refinement:
/// 1. Digest user input -> refine phrase for embedding (small model)
/// 2. Vector search for candidate chunks
/// 3. Evaluate using function calling - model explicitly chooses action:
///    - answer_ready: Context is sufficient
///    - needs_more_context: Search was on-topic but incomplete
///    - refine_query: Search was off-topic, needs better phrase
///    - cannot_answer: Question is fundamentally unanswerable
/// 4. Based on decision, either answer or refine and retry (attempts scale with search depth)
/// 5. Produce final answer or ask user to rephrase.
/// 
/// Uses OpenAI function calling for structured, reliable decision-making.
/// </summary>
public class ChatService
{
    private readonly VectorSearchService _searchService;
    private readonly OpenAiSdkService _openAiClient;
    private readonly ILogger<ChatService> _logger;
    private readonly SearchOptions _searchOptions;

    public ChatService(
        VectorSearchService searchService,
        OpenAiSdkService openAiClient,
        IOptions<SearchOptions> searchOptions,
        ILogger<ChatService> logger)
    {
        _searchService = searchService;
        _openAiClient = openAiClient;
        _searchOptions = searchOptions.Value;
        _logger = logger;
    }

    public async Task<ChatResponse> ProcessAsync(
        ChatRequest request,
        Func<ChatStreamUpdate, Task>? progress = null,
        CancellationToken ct = default)
    {
        var history = request.History ?? new List<ChatMessage>();
        var depth = Math.Clamp(request.SearchDepth ?? _searchOptions.DefaultSearchDepth, 1, _searchOptions.MaxSearchDepth);
        
        // Depth-based attempt logic:
        // depth=1: 1 attempt (simple, no retry)
        // depth=2-3: 2 attempts (smart with one refinement)
        // depth=4: 3 attempts (advanced)
        // depth=5: 4 attempts (deep search with multiple refinements)
        var maxAttempts = depth switch
        {
            1 => 1,
            2 or 3 => 2,
            4 => 3,
            _ => 4  // depth 5
        };
        
        var steps = new List<string>();

        _logger.LogInformation("Query search depth {Depth} configured for {Attempts} attempt(s)", depth, maxAttempts);

        async Task RecordStepAsync(string message)
        {
            steps.Add(message);
            if (progress != null)
            {
                await progress(new ChatStreamUpdate(
                    Type: "step",
                    Message: message,
                    Files: null,
                    Final: null));
            }
        }

        var latestSources = new List<Source>();
        string currentPhrase = request.Message.Trim();
        currentPhrase = await _openAiClient.RefineQueryPhraseAsync(currentPhrase, history, ct);
        await RecordStepAsync($"Rephrased the question for retrieval: \"{currentPhrase}\".");
        await RecordStepAsync($"Search depth level {depth} → {maxAttempts} retrieval attempt(s).");

        string? finalAnswer = null;
        int totalTokens = 0;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            _logger.LogInformation("Chat attempt {Attempt} with phrase: {Phrase}", attempt, currentPhrase);
            await RecordStepAsync($"Attempt {attempt}: searching the index with \"{currentPhrase}\".");

            var embedding = await _openAiClient.EmbedAsync(currentPhrase, ct);
            latestSources = await _searchService.SearchAsync(
                embedding,
                currentPhrase,
                request.TopK,
                request.ProviderType,
                request.ProviderName,
                depth,
                ct);

            if (latestSources.Count == 0)
            {
                _logger.LogInformation("No sources found on attempt {Attempt}", attempt);
                await RecordStepAsync("No matching passages came back.");

                if (attempt == maxAttempts)
                {
                    await RecordStepAsync($"Still nothing after {maxAttempts} attempt(s). Handing control back to the user.");
                    var failure = BuildResponse(
                        answer: "I couldn't find anything relevant. Could you rephrase your question?",
                        userMessage: request.Message,
                        history,
                        steps,
                        sources: new List<Source>(),
                        tokens: totalTokens,
                        includeStepsInHistory: progress != null,
                        includeStepsInResponse: progress != null);

                    if (progress != null)
                    {
                        await progress(new ChatStreamUpdate(
                            Type: "final",
                            Message: null,
                            Files: failure.Files,
                            Final: failure));
                    }

                    return failure;
                }

                currentPhrase = await _openAiClient.RephraseForRetryAsync(currentPhrase, history, latestSources, ct);
                await RecordStepAsync($"Trying a new search phrase: \"{currentPhrase}\".");
                continue;
            }

            var docCount = latestSources.Select(s => s.DocId).Distinct().Count();
            await RecordStepAsync($"Found {latestSources.Count} chunks across {docCount} documents.");

            // Use tool-based evaluation for structured decision making
            var (decision, evalTokens) = await _openAiClient.EvaluateWithToolsAsync(
                currentPhrase, 
                latestSources.Select(s => s.Text).ToList(), 
                ct);
            totalTokens += evalTokens;

            _logger.LogInformation("Model decision: {Action} - {Reasoning}", decision.Action, decision.Reasoning);

            switch (decision.Action)
            {
                case RefinementAction.AnswerReady:
                    await RecordStepAsync($"Context evaluation: {decision.Reasoning}");
                    await RecordStepAsync("Context looks solid — drafting the answer.");
                    break;

                case RefinementAction.NeedsMoreContext when attempt < maxAttempts:
                    await RecordStepAsync($"Model analysis: {decision.Reasoning}");
                    await RecordStepAsync("Broadening the search for additional context.");
                    // Keep same query but increase topK or search depth on next iteration
                    // For now, rephrase to try a different angle
                    currentPhrase = await _openAiClient.RephraseForRetryAsync(currentPhrase, history, latestSources, ct);
                    await RecordStepAsync($"Trying alternative phrasing: \"{currentPhrase}\".");
                    continue;

                case RefinementAction.RefineQuery when attempt < maxAttempts:
                    await RecordStepAsync($"Model analysis: {decision.Reasoning}");
                    currentPhrase = decision.SuggestedQuery ?? await _openAiClient.RephraseForRetryAsync(currentPhrase, history, latestSources, ct);
                    await RecordStepAsync($"Switching to refined query: \"{currentPhrase}\".");
                    continue;

                case RefinementAction.CannotAnswer:
                    _logger.LogInformation("Model determined question cannot be answered: {Reason}", decision.CannotAnswerReason);
                    await RecordStepAsync($"Analysis: {decision.Reasoning}");
                    await RecordStepAsync("This question appears to be outside the scope of available documentation.");
                    
                    var cannotAnswerResponse = BuildResponse(
                        answer: $"I cannot answer this question with the available documentation. {decision.Reasoning}",
                        userMessage: request.Message,
                        history,
                        steps,
                        sources: latestSources,
                        tokens: totalTokens,
                        includeStepsInHistory: progress != null,
                        includeStepsInResponse: progress != null);

                    if (progress != null)
                    {
                        await progress(new ChatStreamUpdate(
                            Type: "final",
                            Message: null,
                            Files: cannotAnswerResponse.Files,
                            Final: cannotAnswerResponse));
                    }

                    return cannotAnswerResponse;

                default:
                    // Reached max attempts with needs_more_context or refine_query
                    await RecordStepAsync($"Analysis: {decision.Reasoning}");
                    await RecordStepAsync($"Reached attempt limit ({maxAttempts}). Answering with available context.");
                    break;
            }

            await RecordStepAsync("Generating answer from available context.");
            var (answer, answerTokens) = await _openAiClient.GenerateAnswerAsync(
                currentPhrase,
                latestSources.Select(s => s.Text).ToList(),
                history.Select(h => (h.Role, h.Content)).ToList(),
                ct,
                useLargeModel: true);
            totalTokens += answerTokens;

            finalAnswer = answer;
            break;
        }

        if (finalAnswer == null)
        {
            await RecordStepAsync("I couldn't gather enough context to answer confidently.");
            var fallback = BuildResponse(
                answer: "I couldn't confidently answer. Please rephrase your question.",
                userMessage: request.Message,
                history,
                steps,
                sources: latestSources,
                tokens: totalTokens,
                includeStepsInHistory: progress != null,
                includeStepsInResponse: progress != null);

            if (progress != null)
            {
                await progress(new ChatStreamUpdate(
                    Type: "final",
                    Message: null,
                    Files: fallback.Files,
                    Final: fallback));
            }

            return fallback;
        }

        var success = BuildResponse(
            answer: finalAnswer,
            userMessage: request.Message,
            history,
            steps,
            sources: latestSources,
            tokens: totalTokens,
            includeStepsInHistory: progress != null,
            includeStepsInResponse: progress != null);

        if (progress != null)
        {
            await progress(new ChatStreamUpdate(
                Type: "final",
                Message: null,
                Files: success.Files,
                Final: success));
        }

        return success;
    }

    private ChatResponse BuildResponse(
        string answer,
        string userMessage,
        List<ChatMessage> history,
        List<string> steps,
        List<Source> sources,
        int tokens,
        bool includeStepsInHistory,
        bool includeStepsInResponse)
    {
        var files = BuildDocumentResults(sources);
        var responseSteps = includeStepsInResponse ? new List<string>(steps) : new List<string>();

        if (includeStepsInResponse && files.Count > 0)
        {
            var previewNames = files
                .Select(f => f.Filename)
                .Distinct()
                .Take(3)
                .ToList();
            if (previewNames.Count > 0)
            {
                var suffix = files.Count > previewNames.Count ? "…" : string.Empty;
                responseSteps.Add($"Noted promising documents: {string.Join(", ", previewNames)}{suffix}.");
            }
        }

        var updatedHistory = new List<ChatMessage>(history)
        {
            new ChatMessage("user", userMessage)
        };

        if (includeStepsInHistory)
        {
            foreach (var step in responseSteps)
            {
                updatedHistory.Add(new ChatMessage("assistant", step));
            }
        }

        // Include top source files in history for better context in follow-up questions
        if (files.Count > 0)
        {
            var fileList = string.Join(", ", files.Take(3).Select(f => f.Filename));
            var sourceSummary = files.Count <= 3 
                ? $"[Found in: {fileList}]" 
                : $"[Found in: {fileList}, and {files.Count - 3} more]";
            updatedHistory.Add(new ChatMessage("assistant", sourceSummary));
        }

        updatedHistory.Add(new ChatMessage("assistant", $"Answer:\n{answer}"));

        return new ChatResponse(
            Answer: answer,
            Steps: responseSteps,
            Files: files,
            Sources: sources,
            TokensUsed: tokens,
            History: updatedHistory
        );
    }

    private static List<DocumentResult> BuildDocumentResults(List<Source> sources)
    {
        if (sources.Count == 0)
        {
            return new List<DocumentResult>();
        }

        return sources
            .GroupBy(s => s.DocId)
            .Select(group => new
            {
                DocId = group.Key,
                First = group.OrderBy(s => s.Distance).First()
            })
            .OrderBy(x => x.First.Distance)
            .Take(5)
            .Select(x =>
            {
                var providerType = x.First.ProviderType ?? string.Empty;
                var providerName = x.First.ProviderName ?? string.Empty;
                var providerPrefix = string.IsNullOrWhiteSpace(providerType) && string.IsNullOrWhiteSpace(providerName)
                    ? string.Empty
                    : $"{providerType}/{providerName}".Trim('/');
                var address = string.IsNullOrWhiteSpace(providerPrefix)
                    ? x.First.Filename
                    : $"{providerPrefix}:{x.First.Filename}";

                return new DocumentResult(
                    DocId: x.DocId,
                    Filename: x.First.Filename,
                    Address: address,
                    Text: x.First.Text,
                    Distance: x.First.Distance,
                    ProviderType: x.First.ProviderType,
                    ProviderName: x.First.ProviderName
                );
            })
            .ToList();
    }
}
