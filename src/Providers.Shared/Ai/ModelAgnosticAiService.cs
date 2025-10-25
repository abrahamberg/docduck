using Microsoft.Extensions.Logging;

namespace DocDuck.Providers.Ai;

/// <summary>
/// Options for chat completion requests.
/// </summary>
public sealed class ChatCompletionOptions
{
    public double? Temperature { get; set; }
    public int? MaxTokens { get; set; }
    public List<ToolDefinition>? Tools { get; set; }
    public string? ToolChoice { get; set; }
}

/// <summary>
/// Unified service for model-agnostic AI operations.
/// Manages model selection, client creation, and intelligent fallback.
/// Replaces the old OpenAI-specific service with flexible multi-provider support.
/// </summary>
public sealed class ModelAgnosticAiService : IAsyncDisposable
{
    private readonly AiProviderConfigurationStore _store;
    private readonly ILogger<ModelAgnosticAiService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    
    private AiProviderConfiguration? _config;
    private AiModelSelector? _selector;
    private DateTimeOffset _loadedAt;
    private readonly Dictionary<string, GenericAiHttpClient> _clientCache = new();

    public ModelAgnosticAiService(
        AiProviderConfigurationStore store,
        ILogger<ModelAgnosticAiService> logger,
        ILoggerFactory loggerFactory)
    {
        _store = store;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public DateTimeOffset LoadedAt => _loadedAt;

    /// <summary>
    /// Ensure configuration is loaded and create selector.
    /// </summary>
    private async Task<(AiProviderConfiguration Config, AiModelSelector Selector)> EnsureConfigAsync(CancellationToken ct)
    {
        if (_config != null && _selector != null)
        {
            return (_config, _selector);
        }

        await ReloadAsync(ct);

        if (_config == null || _selector == null)
        {
            throw new InvalidOperationException("AI provider is not configured.");
        }

        return (_config, _selector);
    }

    /// <summary>
    /// Reload configuration from database.
    /// </summary>
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await _reloadLock.WaitAsync(ct);
        try
        {
            var config = await _store.GetAsync(ct);
            
            if (config == null || !config.Enabled)
            {
                _logger.LogWarning("AI configuration not found or disabled");
                _config = null;
                _selector = null;
                _loadedAt = DateTimeOffset.UtcNow;
                
                // Clear client cache
                foreach (var client in _clientCache.Values)
                {
                    client.Dispose();
                }
                _clientCache.Clear();
                
                return;
            }

            config.Validate();

            _config = config;
            _selector = new AiModelSelector(config, _loggerFactory.CreateLogger<AiModelSelector>());
            _loadedAt = DateTimeOffset.UtcNow;

            // Clear client cache on config change (models might have changed)
            foreach (var client in _clientCache.Values)
            {
                client.Dispose();
            }
            _clientCache.Clear();

            _logger.LogInformation("AI configuration loaded: Micro={Micro}, Mini={Mini}, Full={Full}, Embedding={Embed}",
                config.MicroModel?.DisplayName ?? "none",
                config.MiniModel?.DisplayName ?? "none",
                config.FullModel?.DisplayName ?? "none",
                config.EmbeddingModel?.DisplayName ?? "none");
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    /// <summary>
    /// Get or create HTTP client for a specific model.
    /// </summary>
    private GenericAiHttpClient GetOrCreateClient(AiModelAssignment model)
    {
        if (_clientCache.TryGetValue(model.Id, out var existing))
        {
            return existing;
        }

        var client = new GenericAiHttpClient(model, _logger);
        _clientCache[model.Id] = client;
        return client;
    }

    /// <summary>
    /// Generate embeddings for a single text.
    /// </summary>
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var (config, _) = await EnsureConfigAsync(ct);

        if (config.EmbeddingModel == null)
        {
            throw new InvalidOperationException("Embedding model is not configured");
        }

        // For embedding, we can use any chat model's HTTP client
        var anyModel = config.MicroModel ?? config.MiniModel ?? config.FullModel 
            ?? config.ModelRegistry.FirstOrDefault(m => m.Enabled);
        
        if (anyModel == null)
        {
            throw new InvalidOperationException("No chat model available to use for embedding client");
        }

        var client = GetOrCreateClient(anyModel);
        return await client.EmbedAsync(config.EmbeddingModel, text, ct);
    }

    /// <summary>
    /// Generate embeddings for multiple texts.
    /// </summary>
    public async Task<float[][]> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct = default)
    {
        var (config, _) = await EnsureConfigAsync(ct);

        if (config.EmbeddingModel == null)
        {
            throw new InvalidOperationException("Embedding model is not configured");
        }

        var anyModel = config.MicroModel ?? config.MiniModel ?? config.FullModel
            ?? config.ModelRegistry.FirstOrDefault(m => m.Enabled);
        
        if (anyModel == null)
        {
            throw new InvalidOperationException("No chat model available to use for embedding client");
        }

        var client = GetOrCreateClient(anyModel);
        return await client.EmbedBatchAsync(config.EmbeddingModel, texts, ct);
    }

    /// <summary>
    /// Generate a chat completion using intelligent model selection.
    /// </summary>
    public async Task<ChatCompletionResult> CompleteChatAsync(
        List<ChatMessagePayload> messages,
        TaskComplexity complexity,
        ModelSelectionStrategy? strategy = null,
        ChatCompletionOptions? options = null,
        CancellationToken ct = default)
    {
        var (config, selector) = await EnsureConfigAsync(ct);

        var estimatedTokens = EstimateTokenCount(messages);
        var requiresTools = options?.Tools != null && options.Tools.Count > 0;

        var model = selector.SelectModel(complexity, strategy, estimatedTokens, requiresTools);
        if (model == null)
        {
            throw new InvalidOperationException(
                $"No suitable model available for complexity={complexity}, strategy={strategy}, requiresTools={requiresTools}");
        }

        var client = GetOrCreateClient(model);
        var effectiveTemp = options?.Temperature ?? model.GetDefaultTemperature();

        try
        {
            return await client.CompleteChatAsync(
                messages,
                effectiveTemp,
                options?.MaxTokens,
                options?.Tools,
                options?.ToolChoice,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Chat completion failed with {Model}, attempting fallback", model.DisplayName);
            
            // Try fallback to next available model
            var fallbackModel = TryGetFallbackModel(selector, model, requiresTools);
            if (fallbackModel != null)
            {
                _logger.LogInformation("Using fallback model: {Model}", fallbackModel.DisplayName);
                var fallbackClient = GetOrCreateClient(fallbackModel);
                return await fallbackClient.CompleteChatAsync(
                    messages,
                    effectiveTemp,
                    options?.MaxTokens,
                    options?.Tools,
                    options?.ToolChoice,
                    ct);
            }

            // No fallback available, rethrow with context
            throw new InvalidOperationException($"Chat completion failed with {model.DisplayName} and no fallback available", ex);
        }
    }

    /// <summary>
    /// Estimate token count for messages (rough approximation).
    /// </summary>
    private static int EstimateTokenCount(List<ChatMessagePayload> messages)
    {
        var totalChars = messages.Sum(m => m.Content?.Length ?? 0);
        return totalChars / 4; // Rough estimate: ~4 chars per token
    }

    /// <summary>
    /// Try to find a fallback model when primary fails.
    /// </summary>
    private static AiModelAssignment? TryGetFallbackModel(
        AiModelSelector selector,
        AiModelAssignment failedModel,
        bool requiresTools)
    {
        var allAssignments = selector.GetAllTierAssignments();
        
        // Try other enabled models
        foreach (var (_, model) in allAssignments)
        {
            if (model == null || !model.Enabled || model.Id == failedModel.Id)
            {
                continue;
            }

            if (requiresTools && !model.SupportsFunctionCalling)
            {
                continue;
            }

            return model;
        }

        return null;
    }

    /// <summary>
    /// Get current configuration (for admin display).
    /// </summary>
    public async Task<AiProviderConfiguration?> GetConfigurationAsync(CancellationToken ct = default)
    {
        var config = await _store.GetAsync(ct);
        return config?.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clientCache.Values)
        {
            client.Dispose();
        }
        _clientCache.Clear();
        _reloadLock.Dispose();
        
        await Task.CompletedTask;
    }
}
