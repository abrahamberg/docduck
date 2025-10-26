using Api.Models;
using Api.Options;
using DocDuck.Providers.Ai;
using Microsoft.Extensions.Options;

namespace Api.Services;

/// <summary>
/// Parameters for search attempt execution
/// </summary>
internal sealed record SearchAttemptParams(
    string InitialPhrase,
    ChatRequest Request,
    List<ChatMessage> History,
    int MaxAttempts,
    int Depth,
    Func<string, Task> RecordStepAsync,
    List<ModelUsageInfo> ModelUsage,
    Func<ChatStreamUpdate, Task>? Progress,
    List<string> Steps);

/// <summary>
/// Parameters for no-results handling
/// </summary>
internal sealed record NoResultsParams(
    int Attempt,
    int MaxAttempts,
    string CurrentPhrase,
    string UserMessage,
    List<ChatMessage> History,
    List<Source> LatestSources,
    List<string> Steps,
    int TotalTokens,
    Func<ChatStreamUpdate, Task>? Progress,
    Func<string, Task> RecordStepAsync);

/// <summary>
/// Parameters for decision action processing
/// </summary>
internal sealed record DecisionActionParams(
    RefinementDecision Decision,
    int Attempt,
    int MaxAttempts,
    string CurrentPhrase,
    string UserMessage,
    List<ChatMessage> History,
    List<Source> LatestSources,
    List<string> Steps,
    int TotalTokens,
    Func<ChatStreamUpdate, Task>? Progress,
    Func<string, Task> RecordStepAsync);

/// <summary>
/// Parameters for building chat response
/// </summary>
internal sealed record BuildResponseParams(
    string Answer,
    string UserMessage,
    List<ChatMessage> History,
    List<string> Steps,
    List<Source> Sources,
    int Tokens,
    bool IncludeStepsInHistory,
    bool IncludeStepsInResponse,
    List<ModelUsageInfo>? ModelUsage = null);

/// <summary>
/// Orchestrates multi-step chat interaction with model-agnostic LLM-driven refinement:
/// 1. Digest user input -> refine phrase for embedding (micro/mini model)
/// 2. Vector search for candidate chunks
/// 3. Evaluate using function calling - model explicitly chooses action:
///    - answer_ready: Context is sufficient
///    - needs_more_context: Search was on-topic but incomplete
///    - refine_query: Search was off-topic, needs better phrase
///    - cannot_answer: Question is fundamentally unanswerable
/// 4. Based on decision, either answer or refine and retry (attempts scale with search depth)
/// 5. Produce final answer or ask user to rephrase.
///
/// Uses function calling for structured, reliable decision-making across any OpenAI-compatible model.
/// </summary>
public class ChatService : IChatService
{
    private const string SystemRole = "system";
    private const string StreamTypeFinal = "final";

    private readonly IVectorSearchService _searchService;
    private readonly IModelAgnosticAiService _aiService;
    private readonly ILogger<ChatService> _logger;
    private readonly SearchOptions _searchOptions;

    public ChatService(
        IVectorSearchService searchService,
        IModelAgnosticAiService aiService,
        IOptions<SearchOptions> searchOptions,
        ILogger<ChatService> logger)
    {
        _searchService = searchService;
        _aiService = aiService;
        _searchOptions = searchOptions.Value;
        _logger = logger;
    }

    public async Task<ChatResponse> ProcessAsync(
        ChatRequest request,
        Func<ChatStreamUpdate, Task>? progress = null,
        CancellationToken ct = default)
    {
        var history = request.History ?? [];
        var depth = Math.Clamp(request.SearchDepth ?? _searchOptions.DefaultSearchDepth, 1, _searchOptions.MaxSearchDepth);
        var maxAttempts = CalculateMaxAttempts(depth);

        var steps = new List<string>();
        var modelUsage = new List<ModelUsageInfo>();

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

        var currentPhrase = await InitializeSearchPhrase(request.Message, history, RecordStepAsync, ct);
        await RecordStepAsync($"Search depth level {depth} → {maxAttempts} retrieval attempt(s).");

        var searchParams = new SearchAttemptParams(
            currentPhrase,
            request,
            history,
            maxAttempts,
            depth,
            RecordStepAsync,
            modelUsage,
            progress,
            steps);

        var (finalAnswer, latestSources, totalTokens, earlyResponse) = await ExecuteSearchAttempts(searchParams, ct);

        // If an early response was generated (e.g., cannot answer), return it
        if (earlyResponse != null)
        {
            return earlyResponse;
        }

        if (finalAnswer == null)
        {
            return await CreateFallbackResponse(request.Message, history, steps, latestSources, totalTokens, progress);
        }

        var success = BuildResponse(new BuildResponseParams(
            Answer: finalAnswer,
            UserMessage: request.Message,
            History: history,
            Steps: steps,
            Sources: latestSources,
            Tokens: totalTokens,
            IncludeStepsInHistory: progress != null,
            IncludeStepsInResponse: progress != null,
            ModelUsage: modelUsage));

        if (progress != null)
        {
            await progress(new ChatStreamUpdate(
                Type: StreamTypeFinal,
                Message: null,
                Files: success.Files,
                Final: success));
        }

        return success;
    }

    private static ChatResponse BuildResponse(BuildResponseParams buildParams)
    {
        var files = BuildDocumentResults(buildParams.Sources);
        var responseSteps = buildParams.IncludeStepsInResponse ? new List<string>(buildParams.Steps) : [];

        if (buildParams.IncludeStepsInResponse && files.Count > 0)
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

        var updatedHistory = new List<ChatMessage>(buildParams.History)
        {
            new("user", buildParams.UserMessage)
        };

        if (buildParams.IncludeStepsInHistory)
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

        updatedHistory.Add(new ChatMessage("assistant", $"Answer:\n{buildParams.Answer}"));

        return new ChatResponse(
            Answer: buildParams.Answer,
            Steps: responseSteps,
            Files: files,
            Sources: buildParams.Sources,
            TokensUsed: buildParams.Tokens,
            History: updatedHistory,
            ModelUsage: buildParams.ModelUsage
        );
    }

    private static List<DocumentResult> BuildDocumentResults(List<Source> sources)
    {
        if (sources.Count == 0)
        {
            return [];
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

    // ========== Helper methods that wrap ModelAgnosticAiService ==========

    private static int CalculateMaxAttempts(int depth)
    {
        // Depth-based attempt logic:
        // depth=1: 1 attempt (simple, no retry)
        // depth=2-3: 2 attempts (smart with one refinement)
        // depth=4: 3 attempts (advanced)
        // depth=5: 4 attempts (deep search with multiple refinements)
        return depth switch
        {
            1 => 1,
            2 or 3 => 2,
            4 => 3,
            _ => 4  // depth 5
        };
    }

    private async Task<string> InitializeSearchPhrase(
        string originalMessage,
        List<ChatMessage> history,
        Func<string, Task> recordStepAsync,
        CancellationToken ct)
    {
        var currentPhrase = originalMessage.Trim();
        currentPhrase = await RefineQueryPhraseAsync(currentPhrase, history, ct);
        await recordStepAsync($"Rephrased the question for retrieval: \"{currentPhrase}\".");
        return currentPhrase;
    }

    private async Task<(string? FinalAnswer, List<Source> LatestSources, int TotalTokens, ChatResponse? EarlyResponse)> ExecuteSearchAttempts(
        SearchAttemptParams searchParams,
        CancellationToken ct)
    {
        var latestSources = new List<Source>();
        string currentPhrase = searchParams.InitialPhrase;
        string? finalAnswer = null;
        int totalTokens = 0;

        for (int attempt = 1; attempt <= searchParams.MaxAttempts; attempt++)
        {
            _logger.LogInformation("Chat attempt {Attempt} with phrase: {Phrase}", attempt, currentPhrase);
            await searchParams.RecordStepAsync($"Attempt {attempt}: searching the index with \"{currentPhrase}\".");

            var embedding = await _aiService.EmbedAsync(currentPhrase, ct);
            latestSources = await _searchService.SearchAsync(
                embedding,
                currentPhrase,
                searchParams.Request.TopK,
                searchParams.Request.ProviderType,
                searchParams.Request.ProviderName,
                searchParams.Depth,
                ct);

            if (latestSources.Count == 0)
            {
                var noResultsParams = new NoResultsParams(
                    attempt,
                    searchParams.MaxAttempts,
                    currentPhrase,
                    searchParams.Request.Message,
                    searchParams.History,
                    latestSources,
                    searchParams.Steps,
                    totalTokens,
                    searchParams.Progress,
                    searchParams.RecordStepAsync);

                var noResultsResponse = await HandleNoResults(noResultsParams, ct);

                if (noResultsResponse != null)
                {
                    return (null, [], totalTokens, noResultsResponse);
                }

                currentPhrase = await RephraseForRetryAsync(currentPhrase, searchParams.History, latestSources, ct);
                await searchParams.RecordStepAsync($"Trying a new search phrase: \"{currentPhrase}\".");
                continue;
            }

            var docCount = latestSources.Select(s => s.DocId).Distinct().Count();
            await searchParams.RecordStepAsync($"Found {latestSources.Count} chunks across {docCount} documents.");

            var (decision, evalTokens) = await EvaluateWithToolsAsync(
                currentPhrase,
                latestSources.Select(s => s.Text).ToList(),
                ct);
            totalTokens += evalTokens;
            searchParams.ModelUsage.Add(new ModelUsageInfo("chat-model", "context_evaluation", evalTokens));

            _logger.LogInformation("Model decision: {Action} - {Reasoning}", decision.Action, decision.Reasoning);

            var decisionParams = new DecisionActionParams(
                decision,
                attempt,
                searchParams.MaxAttempts,
                currentPhrase,
                searchParams.Request.Message,
                searchParams.History,
                latestSources,
                searchParams.Steps,
                totalTokens,
                searchParams.Progress,
                searchParams.RecordStepAsync);

            var actionResult = await ProcessDecisionAction(decisionParams, ct);

            if (actionResult.ShouldReturn)
            {
                return (null, latestSources, totalTokens, actionResult.EarlyResponse);
            }

            if (actionResult.ShouldContinue)
            {
                currentPhrase = actionResult.NewPhrase!;
                continue;
            }

            // Generate answer
            await searchParams.RecordStepAsync("Generating answer from available context.");
            var (answer, answerTokens) = await GenerateAnswerAsync(
                currentPhrase,
                latestSources.Select(s => s.Text).ToList(),
                searchParams.History.Select(h => (h.Role, h.Content)).ToList(),
                useLargeModel: true,
                ct);
            totalTokens += answerTokens;
            searchParams.ModelUsage.Add(new ModelUsageInfo("chat-model-large", "answer_generation", answerTokens));

            finalAnswer = answer;
            break;
        }

        return (finalAnswer, latestSources, totalTokens, null);
    }

    private async Task<ChatResponse?> HandleNoResults(
        NoResultsParams noResultsParams,
        CancellationToken ct)
    {
        _logger.LogInformation("No sources found on attempt {Attempt}", noResultsParams.Attempt);
        await noResultsParams.RecordStepAsync("No matching passages came back.");

        if (noResultsParams.Attempt == noResultsParams.MaxAttempts)
        {
            await noResultsParams.RecordStepAsync($"Still nothing after {noResultsParams.MaxAttempts} attempt(s). Handing control back to the user.");
            var failure = BuildResponse(new BuildResponseParams(
                Answer: "I couldn't find anything relevant. Could you rephrase your question?",
                UserMessage: noResultsParams.UserMessage,
                History: noResultsParams.History,
                Steps: noResultsParams.Steps,
                Sources: [],
                Tokens: noResultsParams.TotalTokens,
                IncludeStepsInHistory: noResultsParams.Progress != null,
                IncludeStepsInResponse: noResultsParams.Progress != null));

            if (noResultsParams.Progress != null)
            {
                await noResultsParams.Progress(new ChatStreamUpdate(
                    Type: StreamTypeFinal,
                    Message: null,
                    Files: failure.Files,
                    Final: failure));
            }

            return failure;
        }

        return null;
    }

    private async Task<(bool ShouldReturn, bool ShouldContinue, string? NewPhrase, ChatResponse? EarlyResponse)> ProcessDecisionAction(
        DecisionActionParams decisionParams,
        CancellationToken ct)
    {
        switch (decisionParams.Decision.Action)
        {
            case RefinementAction.AnswerReady:
                await decisionParams.RecordStepAsync($"Context evaluation: {decisionParams.Decision.Reasoning}");
                await decisionParams.RecordStepAsync("Context looks solid — drafting the answer.");
                return (false, false, null, null);

            case RefinementAction.NeedsMoreContext when decisionParams.Attempt < decisionParams.MaxAttempts:
                await decisionParams.RecordStepAsync($"Model analysis: {decisionParams.Decision.Reasoning}");
                await decisionParams.RecordStepAsync("Broadening the search for additional context.");
                var newPhrase = await RephraseForRetryAsync(decisionParams.CurrentPhrase, decisionParams.History, decisionParams.LatestSources, ct);
                await decisionParams.RecordStepAsync($"Trying alternative phrasing: \"{newPhrase}\".");
                return (false, true, newPhrase, null);

            case RefinementAction.RefineQuery when decisionParams.Attempt < decisionParams.MaxAttempts:
                await decisionParams.RecordStepAsync($"Model analysis: {decisionParams.Decision.Reasoning}");
                var refinedPhrase = decisionParams.Decision.SuggestedQuery ?? await RephraseForRetryAsync(decisionParams.CurrentPhrase, decisionParams.History, decisionParams.LatestSources, ct);
                await decisionParams.RecordStepAsync($"Switching to refined query: \"{refinedPhrase}\".");
                return (false, true, refinedPhrase, null);

            case RefinementAction.CannotAnswer:
                _logger.LogInformation("Model determined question cannot be answered: {Reason}", decisionParams.Decision.CannotAnswerReason);
                await decisionParams.RecordStepAsync($"Analysis: {decisionParams.Decision.Reasoning}");
                await decisionParams.RecordStepAsync("This question appears to be outside the scope of available documentation.");

                var cannotAnswerResponse = BuildResponse(new BuildResponseParams(
                    Answer: $"I cannot answer this question with the available documentation. {decisionParams.Decision.Reasoning}",
                    UserMessage: decisionParams.UserMessage,
                    History: decisionParams.History,
                    Steps: decisionParams.Steps,
                    Sources: decisionParams.LatestSources,
                    Tokens: decisionParams.TotalTokens,
                    IncludeStepsInHistory: decisionParams.Progress != null,
                    IncludeStepsInResponse: decisionParams.Progress != null));

                if (decisionParams.Progress != null)
                {
                    await decisionParams.Progress(new ChatStreamUpdate(
                        Type: StreamTypeFinal,
                        Message: null,
                        Files: cannotAnswerResponse.Files,
                        Final: cannotAnswerResponse));
                }

                return (true, false, null, cannotAnswerResponse);

            default:
                await decisionParams.RecordStepAsync($"Analysis: {decisionParams.Decision.Reasoning}");
                await decisionParams.RecordStepAsync($"Reached attempt limit ({decisionParams.MaxAttempts}). Answering with available context.");
                return (false, false, null, null);
        }
    }

    private static async Task<ChatResponse> CreateFallbackResponse(
        string userMessage,
        List<ChatMessage> history,
        List<string> steps,
        List<Source> latestSources,
        int totalTokens,
        Func<ChatStreamUpdate, Task>? progress)
    {
        steps.Add("I couldn't gather enough context to answer confidently.");
        var fallback = BuildResponse(new BuildResponseParams(
            Answer: "I couldn't confidently answer. Please rephrase your question.",
            UserMessage: userMessage,
            History: history,
            Steps: steps,
            Sources: latestSources,
            Tokens: totalTokens,
            IncludeStepsInHistory: progress != null,
            IncludeStepsInResponse: progress != null));

        if (progress != null)
        {
            await progress(new ChatStreamUpdate(
                Type: StreamTypeFinal,
                Message: null,
                Files: fallback.Files,
                Final: fallback));
        }

        return fallback;
    }

    // ========== Helper methods that wrap ModelAgnosticAiService ==========

    private async Task<string> RefineQueryPhraseAsync(string original, List<ChatMessage> history, CancellationToken ct)
    {
        var systemPrompt = DocDuck.Providers.Ai.SystemPrompts.Refine;

        var messages = new List<ChatMessagePayload>
        {
            new(SystemRole, systemPrompt)
        };

        if (history.Count > 0)
        {
            var contextBuilder = new System.Text.StringBuilder();
            contextBuilder.AppendLine("Conversation context:");
            foreach (var msg in history.TakeLast(4))
            {
                contextBuilder.AppendLine($"{msg.Role}: {msg.Content}");
            }
            contextBuilder.AppendLine();
            contextBuilder.AppendLine($"Current question: {original}");
            messages.Add(new ChatMessagePayload("user", contextBuilder.ToString()));
        }
        else
        {
            messages.Add(new ChatMessagePayload("user", original));
        }

        var result = await _aiService.CompleteChatAsync(
            messages,
            TaskComplexity.Simple,
            strategy: null,
            options: null,
            ct: ct);

        var refined = result.Content?.Trim();

        if (string.IsNullOrWhiteSpace(refined))
        {
            _logger.LogWarning("Query refinement returned empty content for input: {Input}. Tool calls: {ToolCalls}. Falling back to original.",
                original, result.ToolCalls.Count);
            return original;
        }

        _logger.LogDebug("Query refined from '{Original}' to '{Refined}'", original, refined);
        return refined;
    }

    private async Task<string> RephraseForRetryAsync(
        string previous,
        List<ChatMessage> history,
        List<Source>? previousResults,
        CancellationToken ct)
    {
        var systemPrompt = DocDuck.Providers.Ai.SystemPrompts.Refine;

        var builder = new System.Text.StringBuilder();

        if (history.Count > 0)
        {
            builder.AppendLine("Conversation context:");
            foreach (var msg in history.TakeLast(4))
            {
                builder.AppendLine($"{msg.Role}: {msg.Content}");
            }
            builder.AppendLine();
        }

        builder.AppendLine($"Previous search phrase: {previous}");

        if (previousResults != null && previousResults.Count > 0)
        {
            builder.AppendLine("Previous search found these results (but may not be sufficient):");
            foreach (var source in previousResults.Take(3))
            {
                var preview = source.Text.Length > 100 ? string.Concat(source.Text.AsSpan(0, 100), "...") : source.Text;
                builder.AppendLine($"- {source.Filename}: \"{preview}\" (distance: {source.Distance:F4})");
            }
        }
        else
        {
            builder.AppendLine("No results were found for the previous phrase.");
        }

        var messages = new List<ChatMessagePayload>
        {
            new(SystemRole, systemPrompt),
            new("user", builder.ToString())
        };

        var result = await _aiService.CompleteChatAsync(
            messages,
            TaskComplexity.Simple,
            strategy: null,
            options: null,
            ct: ct);

        var rephrased = result.Content?.Trim();

        if (string.IsNullOrWhiteSpace(rephrased))
        {
            _logger.LogWarning("Query rephrase returned empty content for input: {Input}. Tool calls: {ToolCalls}. Falling back to previous.",
                previous, result.ToolCalls.Count);
            return previous;
        }

        _logger.LogDebug("Query rephrased from '{Previous}' to '{Rephrased}'", previous, rephrased);
        return rephrased;
    }

    private async Task<(RefinementDecision Decision, int TokensUsed)> EvaluateWithToolsAsync(
        string query,
        List<string> chunks,
        CancellationToken ct)
    {
        var context = string.Join("\n\n", chunks.Select((chunk, index) => $"[{index + 1}] {chunk}"));

        var systemPrompt = """
            You are an expert evaluator determining if retrieved document chunks can answer a user's question.

            Evaluate the context and choose ONE action:
            - answer_ready: Context is sufficient to answer confidently
            - needs_more_context: Context is related but incomplete (need broader/different search)
            - refine_query: Context is off-topic or irrelevant (need better search phrase)
            - cannot_answer: Question is fundamentally unanswerable with this knowledge base

            Be decisive. Choose the action that best reflects the context quality.
            """;

        var userPrompt = $"Query: {query}\n\nRetrieved context:\n{context}";

        var messages = new List<ChatMessagePayload>
        {
            new(SystemRole, systemPrompt),
            new("user", userPrompt)
        };

        var tools = RefinementTools.AllTools.Select(ConvertToToolDefinition).ToList();

        var options = new ChatCompletionOptions
        {
            Tools = tools,
            ToolChoice = "auto"
        };

        var result = await _aiService.CompleteChatAsync(
            messages,
            TaskComplexity.Moderate,
            strategy: null,
            options: options,
            ct: ct);

        if (result.ToolCalls.Count > 0)
        {
            var toolCall = result.ToolCalls[0];
            var decision = ParseToolCall(toolCall);
            _logger.LogInformation("Model chose tool: {Tool} - {Reasoning}", toolCall.FunctionName, decision.Reasoning);
            return (decision, result.TotalTokens);
        }

        _logger.LogWarning("No tool call received from model, defaulting to answer_ready");
        return (new RefinementDecision(RefinementAction.AnswerReady, "No tool call received"), result.TotalTokens);
    }

    private async Task<(string Answer, int TokensUsed)> GenerateAnswerAsync(
        string question,
        List<string> contextChunks,
        List<(string Role, string Content)> history,
        bool useLargeModel,
        CancellationToken ct)
    {
        var promptBuilder = new System.Text.StringBuilder();

        if (history.Count > 0)
        {
            promptBuilder.AppendLine("Conversation history:");
            foreach (var (role, content) in history)
            {
                promptBuilder.AppendLine($"{role}: {content}");
            }
            promptBuilder.AppendLine();
        }

        var context = string.Join("\n\n", contextChunks.Select((chunk, index) => $"[{index + 1}] {chunk}"));
        promptBuilder.AppendLine($"Retrieved context from knowledge base:\n{context}");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine($"Current question: {question}");

        var systemPrompt = history.Count > 0
            ? "You are a helpful assistant that answers questions based on provided document excerpts and conversation history. " +
              "Use the conversation context to resolve pronouns (like 'it', 'that', 'them') and understand follow-up questions. " +
              "Answer concisely and cite document numbers like [1] when referencing specific information."
            : "You are a helpful assistant that answers questions based on the provided document excerpts. " +
              "Answer concisely and cite document numbers like [1] when referencing specific information.";

        var messages = new List<ChatMessagePayload>
        {
            new(SystemRole, systemPrompt),
            new("user", promptBuilder.ToString())
        };

        var complexity = useLargeModel ? TaskComplexity.Complex : TaskComplexity.Moderate;

        var result = await _aiService.CompleteChatAsync(
            messages,
            complexity,
            strategy: null,
            options: null,
            ct: ct);

        return (result.Content ?? string.Empty, result.TotalTokens);
    }

    private static ToolDefinition ConvertToToolDefinition(OpenAI.Chat.ChatTool chatTool)
    {
        // ChatTool from OpenAI SDK has FunctionName, FunctionDescription, and FunctionParameters properties
        var name = chatTool.FunctionName ?? string.Empty;
        var description = chatTool.FunctionDescription ?? string.Empty;
        var parameters = chatTool.FunctionParameters?.ToString() ?? "{}";

        return new ToolDefinition(name, description, parameters);
    }

    private static RefinementDecision ParseToolCall(ToolCall toolCall)
    {
        return toolCall.FunctionName switch
        {
            "answer_ready" => ParseAnswerReady(toolCall.ArgumentsJson),
            "needs_more_context" => ParseNeedsMoreContext(toolCall.ArgumentsJson),
            "refine_query" => ParseRefineQuery(toolCall.ArgumentsJson),
            "cannot_answer" => ParseCannotAnswer(toolCall.ArgumentsJson),
            _ => new RefinementDecision(
                Action: RefinementAction.CannotAnswer,
                Reasoning: $"Unknown tool: {toolCall.FunctionName}",
                CannotAnswerReason: "internal_error"
            )
        };
    }

    private static RefinementDecision ParseAnswerReady(string argsJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(argsJson);
            var root = doc.RootElement;

            var confidence = root.TryGetProperty("confidence", out var confProp)
                ? confProp.GetString() ?? "medium"
                : "medium";
            var reasoning = root.TryGetProperty("reasoning", out var reasonProp)
                ? reasonProp.GetString() ?? "Context appears sufficient"
                : "Context appears sufficient";

            return new RefinementDecision(
                Action: RefinementAction.AnswerReady,
                Reasoning: $"[{confidence} confidence] {reasoning}"
            );
        }
        catch
        {
            return new RefinementDecision(
                Action: RefinementAction.AnswerReady,
                Reasoning: "Model signaled context is sufficient"
            );
        }
    }

    private static RefinementDecision ParseNeedsMoreContext(string argsJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(argsJson);
            var root = doc.RootElement;

            var missing = root.TryGetProperty("what_is_missing", out var missProp)
                ? missProp.GetString() ?? "additional context"
                : "additional context";
            var reasoning = root.TryGetProperty("reasoning", out var reasonProp)
                ? reasonProp.GetString() ?? "Context incomplete"
                : "Context incomplete";

            return new RefinementDecision(
                Action: RefinementAction.NeedsMoreContext,
                Reasoning: $"{reasoning} (Missing: {missing})"
            );
        }
        catch
        {
            return new RefinementDecision(
                Action: RefinementAction.NeedsMoreContext,
                Reasoning: "Model requested more context"
            );
        }
    }

    private static RefinementDecision ParseRefineQuery(string argsJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(argsJson);
            var root = doc.RootElement;

            var newQuery = root.TryGetProperty("new_query", out var queryProp)
                ? queryProp.GetString()
                : null;
            var reasoning = root.TryGetProperty("reasoning", out var reasonProp)
                ? reasonProp.GetString() ?? "Refinement suggested"
                : "Refinement suggested";
            var strategy = root.TryGetProperty("strategy", out var stratProp)
                ? stratProp.GetString() ?? "rephrase"
                : "rephrase";

            return new RefinementDecision(
                Action: RefinementAction.RefineQuery,
                Reasoning: $"[{strategy}] {reasoning}",
                SuggestedQuery: newQuery
            );
        }
        catch
        {
            return new RefinementDecision(
                Action: RefinementAction.RefineQuery,
                Reasoning: "Model suggested query refinement"
            );
        }
    }

    private static RefinementDecision ParseCannotAnswer(string argsJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(argsJson);
            var root = doc.RootElement;

            var reason = root.TryGetProperty("reason", out var reasonProp)
                ? reasonProp.GetString() ?? "no_relevant_documents"
                : "no_relevant_documents";
            var explanation = root.TryGetProperty("explanation", out var explProp)
                ? explProp.GetString() ?? "Question cannot be answered with available knowledge"
                : "Question cannot be answered with available knowledge";

            return new RefinementDecision(
                Action: RefinementAction.CannotAnswer,
                Reasoning: explanation,
                CannotAnswerReason: reason
            );
        }
        catch
        {
            return new RefinementDecision(
                Action: RefinementAction.CannotAnswer,
                Reasoning: "Model indicated question cannot be answered",
                CannotAnswerReason: "unknown"
            );
        }
    }
}
