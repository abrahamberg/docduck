using Api.Models;
using System.Linq;

namespace Api.Services;

/// <summary>
/// Orchestrates multi-step chat interaction:
/// 1. Digest user input -> refine phrase for embedding (small model)
/// 2. Vector search for candidate chunks
/// 3. Evaluate if answerable; may request more context or rephrase and retry (max 2 attempts)
/// 4. Produce final answer or ask user to rephrase.
/// </summary>
public class ChatService
{
    private readonly VectorSearchService _searchService;
    private readonly OpenAiSdkService _openAiClient;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        VectorSearchService searchService,
        OpenAiSdkService openAiClient,
        ILogger<ChatService> logger)
    {
        _searchService = searchService;
        _openAiClient = openAiClient;
        _logger = logger;
    }

    public async Task<ChatResponse> ProcessAsync(
        ChatRequest request,
        Func<ChatStreamUpdate, Task>? progress = null,
        CancellationToken ct = default)
    {
        var history = request.History ?? new List<ChatMessage>();
        var steps = new List<string>();

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

        string? finalAnswer = null;
        int totalTokens = 0;

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            _logger.LogInformation("Chat attempt {Attempt} with phrase: {Phrase}", attempt, currentPhrase);
            await RecordStepAsync($"Attempt {attempt}: searching the index with \"{currentPhrase}\".");

            var embedding = await _openAiClient.EmbedAsync(currentPhrase, ct);
            latestSources = await _searchService.SearchAsync(
                embedding,
                request.TopK,
                request.ProviderType,
                request.ProviderName,
                ct);

            if (latestSources.Count == 0)
            {
                _logger.LogInformation("No sources found on attempt {Attempt}", attempt);
                await RecordStepAsync("No matching passages came back.");

                if (attempt == 2)
                {
                    await RecordStepAsync("Still nothing after two tries. Handing control back to the user.");
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

            var eval = await _openAiClient.EvaluateAnswerabilityAsync(currentPhrase, latestSources.Select(s => s.Text).ToList(), ct);
            totalTokens += eval.TokensUsed;

            if (!eval.Answerable && attempt < 2)
            {
                _logger.LogInformation("Model suggests more context or refinement before answering (attempt {Attempt})", attempt);
                await RecordStepAsync("Context isn't strong enough yet; refining the search phrase.");
                currentPhrase = eval.SuggestedQuery ?? await _openAiClient.RephraseForRetryAsync(currentPhrase, history, latestSources, ct);
                await RecordStepAsync($"Switching to \"{currentPhrase}\" for the next search.");
                continue;
            }

            await RecordStepAsync("Context looks solid — drafting the answer.");
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
