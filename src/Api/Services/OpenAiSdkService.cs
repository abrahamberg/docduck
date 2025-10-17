using System.Linq;
using System.Text;
using DocDuck.Providers.Ai;
using System.ClientModel;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;

namespace Api.Services;

public sealed class OpenAiSdkService
{
    private readonly AiConfigurationService _aiConfig;
    private readonly ILogger<OpenAiSdkService> _logger;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);

    private EmbeddingClient? _embeddingClient;
    private OpenAiProviderSettings? _settings;
    private DateTimeOffset _settingsVersion;

    public OpenAiSdkService(AiConfigurationService aiConfig, ILogger<OpenAiSdkService> logger)
    {
        _aiConfig = aiConfig;
        _logger = logger;
    }

    private async Task<(OpenAiProviderSettings Settings, EmbeddingClient Embedding)> EnsureClientsAsync(CancellationToken ct)
    {
        var currentVersion = _aiConfig.LoadedAt;

        if (_embeddingClient != null && _settings != null && currentVersion <= _settingsVersion)
        {
            return (_settings, _embeddingClient);
        }

        await _initializationLock.WaitAsync(ct);
        try
        {
            currentVersion = _aiConfig.LoadedAt;
            if (_embeddingClient != null && _settings != null && currentVersion <= _settingsVersion)
            {
                return (_settings, _embeddingClient);
            }

            var settings = await _aiConfig.GetOpenAiAsync(ct);
            if (settings is null || !settings.Enabled)
            {
                throw new InvalidOperationException("OpenAI provider is not configured or enabled.");
            }

            settings.Validate();

            var options = CreateClientOptions(settings.BaseUrl);
            var credential = new ApiKeyCredential(settings.ApiKey);
            var embeddingClient = options is null
                ? new EmbeddingClient(settings.EmbedModel, credential)
                : new EmbeddingClient(settings.EmbedModel, credential, options);

            _settings = settings;
            _embeddingClient = embeddingClient;
            _settingsVersion = currentVersion;

            return (settings, embeddingClient);
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private async Task<OpenAiProviderSettings> GetSettingsAsync(CancellationToken ct)
    {
        var (settings, _) = await EnsureClientsAsync(ct);
        return settings;
    }

    private static OpenAIClientOptions? CreateClientOptions(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        var normalized = baseUrl.EndsWith("/", StringComparison.Ordinal)
            ? baseUrl
            : baseUrl + "/";
        return new OpenAIClientOptions { Endpoint = new Uri(normalized, UriKind.Absolute) };
    }

    private static ChatClient CreateChatClient(OpenAiProviderSettings settings, string model)
    {
        var options = CreateClientOptions(settings.BaseUrl);
        var credential = new ApiKeyCredential(settings.ApiKey);
        return options is null
            ? new ChatClient(model, credential)
            : new ChatClient(model, credential, options);
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var (_, embeddingClient) = await EnsureClientsAsync(ct);
        var result = await embeddingClient.GenerateEmbeddingAsync(text, options: null, cancellationToken: ct);
        return result.Value.ToFloats().ToArray();
    }

    public async Task<float[][]> EmbedBatchedAsync(IEnumerable<string> inputs, CancellationToken ct = default)
    {
        var (_, embeddingClient) = await EnsureClientsAsync(ct);
        var items = inputs.ToList();
        if (items.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        var outputs = new List<float[]>(items.Count);
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            var result = await embeddingClient.GenerateEmbeddingAsync(item, options: null, cancellationToken: ct);
            outputs.Add(result.Value.ToFloats().ToArray());
        }

        return outputs.ToArray();
    }

    private static int GetTotalTokensFromUsage(object? usage)
    {
        if (usage == null) return 0;

        var type = usage.GetType();

        var totalProp = type.GetProperty("TotalTokens");
        if (totalProp != null && totalProp.PropertyType == typeof(int))
        {
            return (int)totalProp.GetValue(usage)!;
        }

        var promptProp = type.GetProperty("PromptTokens");
        var completionProp = type.GetProperty("CompletionTokens");
        if (promptProp != null && completionProp != null)
        {
            var promptTokens = promptProp.GetValue(usage) is int p ? p : 0;
            var completionTokens = completionProp.GetValue(usage) is int c ? c : 0;
            return promptTokens + completionTokens;
        }

        return 0;
    }

    public async Task<(string Answer, int TokensUsed)> GenerateAnswerAsync(
        string question,
        List<string> contextChunks,
        List<(string Role, string Content)>? history = null,
        CancellationToken ct = default,
        bool useLargeModel = false)
    {
        var settings = await GetSettingsAsync(ct);
        var model = useLargeModel ? settings.ChatModelLarge : settings.ChatModelSmall;
        var chatClient = CreateChatClient(settings, model);

        var promptBuilder = new StringBuilder();
        if (history != null)
        {
            foreach (var (_, content) in history)
            {
                promptBuilder.AppendLine(content);
            }
        }

        var context = string.Join("\n\n", contextChunks.Select((chunk, index) => $"[{index + 1}] {chunk}"));
        promptBuilder.AppendLine($"Context:\n{context}\n\nQuestion: {question}");

        var sdkMessages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage("You are a helpful assistant that answers questions based on the provided document excerpts."),
            ChatMessage.CreateUserMessage(promptBuilder.ToString())
        };

        var completionResult = await chatClient.CompleteChatAsync(sdkMessages, options: null, cancellationToken: ct);
        var completion = completionResult.Value;
        var text = completion.Content.FirstOrDefault()?.Text ?? string.Empty;
        var tokens = GetTotalTokensFromUsage(completion.Usage);

        return (text, tokens);
    }

    public async Task<string> RefineQueryPhraseAsync(string original, List<Api.Models.ChatMessage> history, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        var chatClient = CreateChatClient(settings, settings.ChatModelSmall);

        var sdkMessages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage(settings.RefineSystemPrompt),
            ChatMessage.CreateUserMessage(original)
        };

        var completionResult = await chatClient.CompleteChatAsync(sdkMessages, options: null, cancellationToken: ct);
        var completion = completionResult.Value;
        return completion.Content.FirstOrDefault()?.Text?.Trim() ?? original;
    }

    public async Task<string> RephraseForRetryAsync(string previous, List<Api.Models.ChatMessage> history, List<Api.Models.Source>? previousResults = null, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        var chatClient = CreateChatClient(settings, settings.ChatModelSmall);

        var builder = new StringBuilder();
        builder.AppendLine($"Original phrase: {previous}");
        if (previousResults != null && previousResults.Count > 0)
        {
            builder.AppendLine("Previous search results (top results with distance):");
            foreach (var result in previousResults.Take(5))
            {
                builder.AppendLine($"- {result.Text} (distance: {result.Distance:F4})");
            }
        }
        else
        {
            builder.AppendLine("No results were found for the previous phrase.");
        }

        var sdkMessages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage(settings.RefineSystemPrompt),
            ChatMessage.CreateUserMessage(builder.ToString())
        };

        var completionResult = await chatClient.CompleteChatAsync(sdkMessages, options: null, cancellationToken: ct);
        var completion = completionResult.Value;
        return completion.Content.FirstOrDefault()?.Text?.Trim() ?? previous;
    }

    public async Task<(bool Answerable, string? SuggestedQuery, int TokensUsed)> EvaluateAnswerabilityAsync(string query, List<string> chunks, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        var chatClient = CreateChatClient(settings, settings.ChatModelSmall);

        var context = string.Join("\n\n", chunks.Select((chunk, index) => $"[{index + 1}] {chunk}"));
        var sdkMessages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage("Determine if answer can be produced ONLY from given context. Reply JSON with fields: answerable:boolean, suggested_query:string|null."),
            ChatMessage.CreateUserMessage($"Query: {query}\nContext:\n{context}")
        };

        var completionResult = await chatClient.CompleteChatAsync(sdkMessages, options: null, cancellationToken: ct);
        var completion = completionResult.Value;
        var text = completion.Content.FirstOrDefault()?.Text?.Trim() ?? string.Empty;
        var tokens = GetTotalTokensFromUsage(completion.Usage);

        bool answerable = false;
        string? suggested = null;

        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("answerable", out var answerableProp) && answerableProp.ValueKind == System.Text.Json.JsonValueKind.True)
            {
                answerable = true;
            }

            if (root.TryGetProperty("suggested_query", out var suggestedProp) && suggestedProp.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                suggested = suggestedProp.GetString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse answerability JSON: {Text}", text);
        }

        return (answerable, suggested, tokens);
    }
}
