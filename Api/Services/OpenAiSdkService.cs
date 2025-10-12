using System.Text;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;

namespace Api.Services;

/// <summary>
/// Thin wrapper around the official OpenAI .NET SDK exposing the methods used
/// by the rest of the application. Keeps the public surface minimal so callers
/// don't depend on SDK types directly.
/// </summary>
public class OpenAiSdkService
{
    private readonly EmbeddingClient _embeddingClient;
    private readonly Api.Options.OpenAiOptions _options;
    private readonly ILogger<OpenAiSdkService> _logger;
    private const string OpenAiEnvVar = "OPENAI_API_KEY";

    public OpenAiSdkService(IOptions<Api.Options.OpenAiOptions> options, ILogger<OpenAiSdkService> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Use the OpenAIClient convenience type which can produce feature clients.
        // Read API key from OPENAI_API_KEY environment variable as required.
        var apiKey = Environment.GetEnvironmentVariable(OpenAiEnvVar);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set");
        }

        // Create per-feature clients using model name + API key as documented in the SDK README.
        // The SDK supports specifying a custom base URL via environment or options; to keep this
        // simple we rely on the client's defaults unless BaseUrl is provided.
        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            // If a custom base URL is provided, set the OpenAI_API_BASE env var for the SDK.
            Environment.SetEnvironmentVariable("OPENAI_BASE_URL", _options.BaseUrl);
        }

        _embeddingClient = new EmbeddingClient(_options.EmbedModel, apiKey);
    }

    private static int GetTotalTokensFromUsage(object? usage)
    {
        if (usage == null) return 0;

        var type = usage.GetType();

        // Try common property names
        var totalProp = type.GetProperty("TotalTokens");
        if (totalProp != null && totalProp.PropertyType == typeof(int))
        {
            return (int)totalProp.GetValue(usage)!;
        }

        var promptProp = type.GetProperty("PromptTokens");
        var completionProp = type.GetProperty("CompletionTokens");
        if (promptProp != null && completionProp != null)
        {
            var p = promptProp.GetValue(usage) is int pi ? pi : 0;
            var c = completionProp.GetValue(usage) is int ci ? ci : 0;
            return p + c;
        }

        return 0;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        // Use the SDK's async API directly to avoid blocking threads.
    var result = await _embeddingClient.GenerateEmbeddingAsync(text, options: null, cancellationToken: ct);
        var embedding = result.Value;
        return embedding.ToFloats().ToArray();
    }

    public async Task<float[][]> EmbedBatchedAsync(IEnumerable<string> inputs, CancellationToken ct = default)
    {
        var list = inputs.ToList();
        if (list.Count == 0) return Array.Empty<float[]>();

        // Call the SDK async API sequentially to respect rate limits and cancellation.
        var outputs = new List<float[]>();
        foreach (var item in list)
        {
            ct.ThrowIfCancellationRequested();
            var embResult = await _embeddingClient.GenerateEmbeddingAsync(item, options: null, cancellationToken: ct);
            outputs.Add(embResult.Value.ToFloats().ToArray());
        }
        return outputs.ToArray();
    }

    public async Task<(string Answer, int TokensUsed)> GenerateAnswerAsync(
        string question,
        List<string> contextChunks,
        List<(string Role, string Content)>? history = null,
        CancellationToken ct = default,
        bool useLargeModel = false)
    {
        var model = useLargeModel ? _options.ChatModelLarge : _options.ChatModelSmall;
    var chatClient = new ChatClient(model, Environment.GetEnvironmentVariable(OpenAiEnvVar));

    var messageList = new List<string>();
        if (history != null)
        {
            foreach (var h in history)
            {
                messageList.Add(h.Content);
            }
        }

        var contextText = string.Join("\n\n", contextChunks.Select((c, i) => $"[{i+1}] {c}"));
        messageList.Add($"Context:\n{contextText}\n\nQuestion: {question}");

        var prompt = string.Join("\n\n", messageList);
        // Build SDK ChatMessage list
        var sdkMessages = new List<OpenAI.Chat.ChatMessage>
        {
            OpenAI.Chat.ChatMessage.CreateSystemMessage("You are a helpful assistant that answers questions based on the provided document excerpts."),
            OpenAI.Chat.ChatMessage.CreateUserMessage(prompt)
        };

        var completionResult = await chatClient.CompleteChatAsync(sdkMessages, options: null, cancellationToken: ct);
        var completion = completionResult.Value;
        var text = completion.Content.FirstOrDefault()?.Text ?? string.Empty;
        var tokens = GetTotalTokensFromUsage(completion.Usage);
        return (text, tokens);
    }

    public async Task<string> RefineQueryPhraseAsync(string original, List<Api.Models.ChatMessage> history, CancellationToken ct = default)
    {
        var client = new ChatClient(_options.ChatModelSmall, Environment.GetEnvironmentVariable(OpenAiEnvVar));
    var sdkMsg = new List<OpenAI.Chat.ChatMessage> { OpenAI.Chat.ChatMessage.CreateUserMessage(original) };
    var completionResult = await client.CompleteChatAsync(sdkMsg, options: null, cancellationToken: ct);
        var completion = completionResult.Value;
        return completion.Content.FirstOrDefault()?.Text?.Trim() ?? original;
    }

    public async Task<string> RephraseForRetryAsync(string previous, List<Api.Models.ChatMessage> history, CancellationToken ct = default)
    {
        var client = new ChatClient(_options.ChatModelSmall, Environment.GetEnvironmentVariable(OpenAiEnvVar));
    var sdkMsg2 = new List<OpenAI.Chat.ChatMessage> { OpenAI.Chat.ChatMessage.CreateUserMessage(previous) };
    var completionResult = await client.CompleteChatAsync(sdkMsg2, options: null, cancellationToken: ct);
        var completion = completionResult.Value;
        return completion.Content.FirstOrDefault()?.Text?.Trim() ?? previous;
    }

    public async Task<(bool Answerable, string? SuggestedQuery, int TokensUsed)> EvaluateAnswerabilityAsync(string query, List<string> chunks, CancellationToken ct = default)
    {
        var client = new ChatClient(_options.ChatModelSmall, Environment.GetEnvironmentVariable(OpenAiEnvVar));
        var context = string.Join("\n\n", chunks.Select((c, i) => $"[{i+1}] {c}"));
    var sdkEval = new List<OpenAI.Chat.ChatMessage> { OpenAI.Chat.ChatMessage.CreateUserMessage($"Query: {query}\nContext:\n{context}") };
    var completionResult = await client.CompleteChatAsync(sdkEval, options: null, cancellationToken: ct);
        var completion = completionResult.Value;
        var text = completion.Content.FirstOrDefault()?.Text?.Trim() ?? string.Empty;
        var tokens = GetTotalTokensFromUsage(completion.Usage);

        bool answerable = false; string? suggested = null;
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("answerable", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.True) answerable = true;
            if (doc.RootElement.TryGetProperty("suggested_query", out var sq) && sq.ValueKind == System.Text.Json.JsonValueKind.String) suggested = sq.GetString();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse answerability JSON: {Text}", text);
        }

        return (answerable, suggested, tokens);
    }
}
