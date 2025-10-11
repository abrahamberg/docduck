using Api.Models;
using Api.Options;
using Microsoft.Extensions.Options;

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
    private readonly OpenAiClient _openAiClient;
    private readonly OpenAiOptions _options;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        VectorSearchService searchService,
        OpenAiClient openAiClient,
        IOptions<OpenAiOptions> options,
        ILogger<ChatService> logger)
    {
        _searchService = searchService;
        _openAiClient = openAiClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ChatResponse> ProcessAsync(
        ChatRequest request,
        CancellationToken ct = default)
    {
        // History list to extend
        var history = request.History ?? new List<ChatMessage>();
        var attempts = new List<string>();

        string currentPhrase = request.Message.Trim();
        // Step 1: Digest user input into effective search phrase (small model prompt)
        currentPhrase = await _openAiClient.RefineQueryPhraseAsync(currentPhrase, history, ct);
        attempts.Add(currentPhrase);

        var allSources = new List<Source>();
        ChatMessage? assistantFinal = null;
        int totalTokens = 0;

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            _logger.LogInformation("Chat attempt {Attempt} with phrase: {Phrase}", attempt, currentPhrase);

            var embedding = await _openAiClient.EmbedAsync(currentPhrase, ct);
            var sources = await _searchService.SearchAsync(
                embedding,
                request.TopK,
                request.ProviderType,
                request.ProviderName,
                ct);
            allSources = sources; // overwrite latest

            if (sources.Count == 0)
            {
                _logger.LogInformation("No sources found on attempt {Attempt}", attempt);
                if (attempt == 2)
                {
                    return BuildFailureResponse("I couldn't find anything relevant. Could you rephrase your question?", history, totalTokens);
                }
                // Rephrase and retry
                currentPhrase = await _openAiClient.RephraseForRetryAsync(currentPhrase, history, ct);
                attempts.Add(currentPhrase);
                continue;
            }

            // Evaluate answerability & maybe generate answer (small model first)
            var eval = await _openAiClient.EvaluateAnswerabilityAsync(currentPhrase, sources.Select(s => s.Text).ToList(), ct);
            totalTokens += eval.TokensUsed;

            if (!eval.Answerable && attempt < 2)
            {
                _logger.LogInformation("Model suggests more context or refinement before answering (attempt {Attempt})", attempt);
                currentPhrase = eval.SuggestedQuery ?? await _openAiClient.RephraseForRetryAsync(currentPhrase, history, ct);
                attempts.Add(currentPhrase);
                continue;
            }

            // Use large model for final answer if answerable or last attempt
            var (answer, answerTokens) = await _openAiClient.GenerateAnswerAsync(
                currentPhrase,
                sources.Select(s => s.Text).ToList(),
                history.Select(h => (h.Role, h.Content)).ToList(),
                ct,
                useLargeModel: true);
            totalTokens += answerTokens;

            assistantFinal = new ChatMessage("assistant", answer);
            break;
        }

        if (assistantFinal == null)
        {
            return BuildFailureResponse("I couldn't confidently answer. Please rephrase your question.", history, totalTokens);
        }

        var updatedHistory = new List<ChatMessage>(history)
        {
            new ChatMessage("user", request.Message),
            assistantFinal
        };

        return new ChatResponse(
            Answer: assistantFinal.Content,
            Sources: allSources,
            TokensUsed: totalTokens,
            History: updatedHistory
        );
    }

    private ChatResponse BuildFailureResponse(string message, List<ChatMessage> history, int tokens)
    {
        var updatedHistory = new List<ChatMessage>(history)
        {
            new ChatMessage("assistant", message)
        };
        return new ChatResponse(
            Answer: message,
            Sources: new List<Source>(),
            TokensUsed: tokens,
            History: updatedHistory
        );
    }
}
