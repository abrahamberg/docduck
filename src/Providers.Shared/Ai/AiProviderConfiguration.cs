using System.Text.Json;

namespace DocDuck.Providers.Ai;

/// <summary>
/// Complete AI configuration supporting model-agnostic multi-tier architecture.
/// Models are stored in a registry and can be assigned to tiers or left unassigned.
/// </summary>
public sealed class AiProviderConfiguration
{
    /// <summary>
    /// Globally enable/disable the AI system.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Default model selection strategy for users who don't specify one.
    /// </summary>
    public ModelSelectionStrategy DefaultSelectionStrategy { get; set; } = ModelSelectionStrategy.Standard;

    /// <summary>
    /// Registry of all available chat models.
    /// Models can be tested and assigned to tiers from this registry.
    /// </summary>
    public List<AiModelAssignment> ModelRegistry { get; set; } = new();

    /// <summary>
    /// ID of model assigned to Micro tier (optional).
    /// If null, system will try Mini, then Full as fallback.
    /// Must reference a model ID in ModelRegistry.
    /// </summary>
    public string? MicroModelId { get; set; }

    /// <summary>
    /// ID of model assigned to Mini tier (optional).
    /// If null, system will try Micro or Full as fallback.
    /// Must reference a model ID in ModelRegistry.
    /// </summary>
    public string? MiniModelId { get; set; }

    /// <summary>
    /// ID of model assigned to Full tier (optional).
    /// If null, system will try Mini, then Micro as fallback.
    /// Must reference a model ID in ModelRegistry.
    /// </summary>
    public string? FullModelId { get; set; }

    /// <summary>
    /// Registry of available embedding models.
    /// </summary>
    public List<AiEmbeddingModelAssignment> EmbeddingRegistry { get; set; } = new();

    /// <summary>
    /// ID of active embedding model.
    /// This is REQUIRED - system cannot function without embeddings.
    /// Must reference a model ID in EmbeddingRegistry.
    /// </summary>
    public string? ActiveEmbeddingModelId { get; set; }

    // Helper properties for backward compatibility and convenience
    public AiModelAssignment? MicroModel => ModelRegistry.FirstOrDefault(m => m.Id == MicroModelId);
    public AiModelAssignment? MiniModel => ModelRegistry.FirstOrDefault(m => m.Id == MiniModelId);
    public AiModelAssignment? FullModel => ModelRegistry.FirstOrDefault(m => m.Id == FullModelId);
    public AiEmbeddingModelAssignment? EmbeddingModel => EmbeddingRegistry.FirstOrDefault(m => m.Id == ActiveEmbeddingModelId);

    public void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        // Validate all models in registry
        foreach (var model in ModelRegistry)
        {
            model.Validate();
        }

        foreach (var model in EmbeddingRegistry)
        {
            model.Validate();
        }

        // At least one chat model must be assigned to a tier
        if (string.IsNullOrWhiteSpace(MicroModelId) &&
            string.IsNullOrWhiteSpace(MiniModelId) &&
            string.IsNullOrWhiteSpace(FullModelId))
        {
            throw new InvalidOperationException("At least one chat model must be assigned to a tier (Micro, Mini, or Full) when AI is enabled.");
        }

        // Verify assigned model IDs exist in registry
        if (!string.IsNullOrWhiteSpace(MicroModelId) && MicroModel == null)
        {
            throw new InvalidOperationException($"Micro tier references unknown model ID: {MicroModelId}");
        }
        if (!string.IsNullOrWhiteSpace(MiniModelId) && MiniModel == null)
        {
            throw new InvalidOperationException($"Mini tier references unknown model ID: {MiniModelId}");
        }
        if (!string.IsNullOrWhiteSpace(FullModelId) && FullModel == null)
        {
            throw new InvalidOperationException($"Full tier references unknown model ID: {FullModelId}");
        }

        // Embedding model is mandatory
        if (string.IsNullOrWhiteSpace(ActiveEmbeddingModelId) || EmbeddingModel == null)
        {
            throw new InvalidOperationException("Active embedding model is required when AI is enabled.");
        }
    }

    public AiProviderConfiguration Clone() => new()
    {
        Enabled = Enabled,
        DefaultSelectionStrategy = DefaultSelectionStrategy,
        ModelRegistry = ModelRegistry.Select(m => m.Clone()).ToList(),
        MicroModelId = MicroModelId,
        MiniModelId = MiniModelId,
        FullModelId = FullModelId,
        EmbeddingRegistry = EmbeddingRegistry.Select(m => m.Clone()).ToList(),
        ActiveEmbeddingModelId = ActiveEmbeddingModelId
    };
}

/// <summary>
/// Configuration for embedding model (separate from chat models).
/// </summary>
public sealed class AiEmbeddingModelAssignment
{
    /// <summary>
    /// Unique identifier for this embedding model.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name (e.g., "text-embedding-3-small", "bge-large-en").
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Model identifier for API requests.
    /// </summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>
    /// Full URL for the embedding API endpoint (e.g., "https://api.openai.com/v1/embeddings").
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// HTTP headers for the request (e.g., {"Authorization": "Bearer sk-..."}).
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>
    /// JSON request template with placeholders like {MODEL_ID}, {INPUT}, {ENCODING_FORMAT}.
    /// Stored as JSON string to allow placeholders.
    /// </summary>
    public JsonDocument? RequestTemplate { get; set; }

    /// <summary>
    /// JSONPath expressions to extract embedding vector and usage from response.
    /// Example: {"embedding": "$.data[0].embedding", "usage.total_tokens": "$.usage.total_tokens"}
    /// </summary>
    public Dictionary<string, string> ResponseMapping { get; set; } = new()
    {
        ["embedding"] = "$.data[0].embedding",
        ["usage.total_tokens"] = "$.usage.total_tokens"
    };

    /// <summary>
    /// Default parameters to include in requests.
    /// </summary>
    public Dictionary<string, object> DefaultParams { get; set; } = new();

    /// <summary>
    /// Embedding vector dimensionality (e.g., 1536 for text-embedding-3-small).
    /// Used to validate database schema compatibility.
    /// </summary>
    public int Dimensions { get; set; } = 1536;

    /// <summary>
    /// Maximum batch size for embedding requests.
    /// </summary>
    public int BatchSize { get; set; } = 16;

    /// <summary>
    /// Whether this model is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Test status for this model (Untested, Passed, Failed).
    /// </summary>
    public ModelTestStatus TestStatus { get; set; } = ModelTestStatus.Untested;

    /// <summary>
    /// Last test timestamp (UTC).
    /// </summary>
    public DateTimeOffset? LastTestedAt { get; set; }

    /// <summary>
    /// Last test result message.
    /// </summary>
    public string? LastTestMessage { get; set; }

    /// <summary>
    /// Request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(ModelId);

        // Url is required
        if (string.IsNullOrWhiteSpace(Url))
        {
            throw new InvalidOperationException($"URL is required for embedding model {Id}");
        }

        if (!Uri.TryCreate(Url, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException($"Invalid URL for embedding model {Id}: {Url}");
        }

        if (Dimensions < 128 || Dimensions > 8192)
        {
            throw new InvalidOperationException($"Dimensions must be between 128 and 8192 for embedding model {Id}");
        }

        if (BatchSize < 1)
        {
            throw new InvalidOperationException($"BatchSize must be at least 1 for embedding model {Id}");
        }
    }

    public AiEmbeddingModelAssignment Clone() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        ModelId = ModelId,
        Url = Url,
        Headers = new Dictionary<string, string>(Headers),
        RequestTemplate = RequestTemplate == null ? null : JsonDocument.Parse(RequestTemplate.RootElement.GetRawText()),
        ResponseMapping = new Dictionary<string, string>(ResponseMapping),
        DefaultParams = new Dictionary<string, object>(DefaultParams),
        Dimensions = Dimensions,
        BatchSize = BatchSize,
        Enabled = Enabled,
        TestStatus = TestStatus,
        LastTestedAt = LastTestedAt,
        LastTestMessage = LastTestMessage,
        TimeoutSeconds = TimeoutSeconds
    };
}
