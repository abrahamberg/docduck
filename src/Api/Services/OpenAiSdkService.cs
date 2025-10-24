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
        
        // Include conversation history first
        if (history != null && history.Count > 0)
        {
            promptBuilder.AppendLine("Conversation history:");
            foreach (var (role, content) in history)
            {
                promptBuilder.AppendLine($"{role}: {content}");
            }
            promptBuilder.AppendLine();
        }

        // Add retrieved document context
        var context = string.Join("\n\n", contextChunks.Select((chunk, index) => $"[{index + 1}] {chunk}"));
        promptBuilder.AppendLine($"Retrieved context from knowledge base:\n{context}");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine($"Current question: {question}");

        var systemPrompt = history != null && history.Count > 0
            ? "You are a helpful assistant that answers questions based on provided document excerpts and conversation history. Use the conversation context to resolve pronouns (like 'it', 'that', 'them') and understand follow-up questions. Answer concisely and cite document numbers like [1] when referencing specific information."
            : "You are a helpful assistant that answers questions based on the provided document excerpts. Answer concisely and cite document numbers like [1] when referencing specific information.";

        var sdkMessages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage(systemPrompt),
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
            ChatMessage.CreateSystemMessage(settings.RefineSystemPrompt)
        };

        // Include conversation history for context-aware refinement
        if (history != null && history.Count > 0)
        {
            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine("Conversation context:");
            foreach (var msg in history.TakeLast(4)) // Last 4 messages for context
            {
                contextBuilder.AppendLine($"{msg.Role}: {msg.Content}");
            }
            contextBuilder.AppendLine();
            contextBuilder.AppendLine($"Current question: {original}");
            sdkMessages.Add(ChatMessage.CreateUserMessage(contextBuilder.ToString()));
        }
        else
        {
            sdkMessages.Add(ChatMessage.CreateUserMessage(original));
        }

        var completionResult = await chatClient.CompleteChatAsync(sdkMessages, options: null, cancellationToken: ct);
        var completion = completionResult.Value;
        return completion.Content.FirstOrDefault()?.Text?.Trim() ?? original;
    }

    public async Task<string> RephraseForRetryAsync(string previous, List<Api.Models.ChatMessage> history, List<Api.Models.Source>? previousResults = null, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        var chatClient = CreateChatClient(settings, settings.ChatModelSmall);

        var builder = new StringBuilder();
        
        // Include conversation history for context
        if (history != null && history.Count > 0)
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
            foreach (var result in previousResults.Take(3))
            {
                builder.AppendLine($"- {result.Filename}: \"{result.Text.Substring(0, Math.Min(100, result.Text.Length))}...\" (distance: {result.Distance:F4})");
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

    /// <summary>
    /// Evaluate context using function calling tools (modern approach).
    /// The model explicitly chooses an action: answer_ready, needs_more_context, refine_query, or cannot_answer.
    /// </summary>
    public async Task<(RefinementDecision Decision, int TokensUsed)> EvaluateWithToolsAsync(
        string query, 
        List<string> chunks, 
        CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        var chatClient = CreateChatClient(settings, settings.ChatModelSmall);

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

        var messages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage(systemPrompt),
            ChatMessage.CreateUserMessage(userPrompt)
        };

        var options = new ChatCompletionOptions
        {
            Tools = { RefinementTools.AnswerReadyTool, RefinementTools.NeedsMoreContextTool, 
                      RefinementTools.RefineQueryTool, RefinementTools.CannotAnswerTool },
            ToolChoice = ChatToolChoice.CreateAutoChoice() // Let model decide which tool
        };

        var completionResult = await chatClient.CompleteChatAsync(messages, options, cancellationToken: ct);
        var completion = completionResult.Value;
        var tokens = GetTotalTokensFromUsage(completion.Usage);

        // Parse tool calls
        var toolCalls = completion.ToolCalls?.ToList() ?? new List<ChatToolCall>();
        
        if (toolCalls.Count > 0)
        {
            var toolCall = toolCalls[0]; // Take first tool call
            var decision = RefinementTools.ParseToolCall(toolCall);
            _logger.LogInformation("Model chose tool: {Tool} - {Reasoning}", 
                toolCall.FunctionName, decision.Reasoning);
            return (decision, tokens);
        }

        // Fallback if no tool was called (shouldn't happen with tool_choice=auto and tools provided)
        _logger.LogWarning("No tool call received from model, defaulting to answer_ready");
        return (new RefinementDecision(RefinementAction.AnswerReady, "No tool call received"), tokens);
    }

    /// <summary>
    /// Legacy method - deprecated in favor of EvaluateWithToolsAsync.
    /// Kept for backward compatibility during migration.
    /// </summary>
    [Obsolete("Use EvaluateWithToolsAsync instead for better structured decision making")]
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
