using System.Text.Json;

namespace DocDuck.Providers.Ai;

/// <summary>
/// Test status for AI models.
/// </summary>
public enum ModelTestStatus
{
    /// <summary>
    /// Model has not been tested yet.
    /// </summary>
    Untested = 0,

    /// <summary>
    /// Model passed the last test.
    /// </summary>
    Passed = 1,

    /// <summary>
    /// Model failed the last test.
    /// </summary>
    Failed = 2
}

/// <summary>
/// Represents an assigned AI model with flexible endpoint configuration.
/// Supports any OpenAI-compatible HTTP API with customizable request/response templates.
/// </summary>
public sealed class AiModelAssignment
{
    /// <summary>
    /// Unique identifier for this model assignment (for admin UI and logging).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name for this model (e.g., "GPT-4o", "Qwen-30B", "DeepSeek-18B").
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Model identifier used in API requests (e.g., "gpt-4o", "qwen-2.5-32b-instruct").
    /// This is the value sent to the inference API.
    /// </summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>
    /// Full API endpoint URL (e.g., "https://api.openai.com/v1/chat/completions").
    /// Replaces BaseUrl to support full endpoint specification.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// HTTP headers as dictionary (e.g., Authorization, Content-Type, custom headers).
    /// Default: {"Content-Type": "application/json"}
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new()
    {
        ["Content-Type"] = "application/json"
    };

    /// <summary>
    /// Request body template with variable placeholders.
    /// Supports: {MODEL_ID}, {MESSAGES}, {TEMPERATURE}, {MAX_TOKENS}, {TOOLS}, {TOOL_CHOICE}
    /// </summary>
    public JsonDocument? RequestTemplate { get; set; } = null;

    /// <summary>
    /// Response structure mapping (JSONPath expressions).
    /// Auto-detected on first successful call, can be manually overridden.
    /// </summary>
    public ResponseMapping? ResponseMapping { get; set; }

    /// <summary>
    /// Default parameters for this model (temperature, top_p, etc.).
    /// Merged with per-request parameters.
    /// </summary>
    public Dictionary<string, JsonElement> DefaultParams { get; set; } = new();

    /// <summary>
    /// DEPRECATED: Use Url instead. Kept for backward compatibility during migration.
    /// Will be removed in future version.
    /// </summary>
    [Obsolete("Use Url property instead. This will be removed in a future version.")]
    public string BaseUrl
    {
        get => ExtractBaseUrlFromUrl();
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(Url))
            {
                Url = $"{value.TrimEnd('/')}/chat/completions";
            }
        }
    }

    /// <summary>
    /// DEPRECATED: Use Headers["Authorization"] instead.
    /// </summary>
    [Obsolete("Use Headers[\"Authorization\"] instead. This will be removed in a future version.")]
    public string ApiKey
    {
        get => ExtractApiKeyFromHeaders();
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Headers["Authorization"] = $"Bearer {value}";
            }
        }
    }

    /// <summary>
    /// Maximum context window size in tokens for this model.
    /// Used to determine if context fits without truncation.
    /// </summary>
    public int MaxContextTokens { get; set; } = 4096;

    /// <summary>
    /// Maximum output tokens this model can generate.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 2048;

    /// <summary>
    /// Whether this model supports function/tool calling.
    /// Required for evaluation and structured output features.
    /// </summary>
    public bool SupportsFunctionCalling { get; set; } = true;

    /// <summary>
    /// Relative cost factor for this model (normalized, e.g., 1.0 = baseline, 10.0 = 10x cost).
    /// Used by ModelSelectionStrategy to make cost-aware decisions.
    /// </summary>
    public double CostFactor { get; set; } = 1.0;

    /// <summary>
    /// Whether this model is currently enabled and available for use.
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
    /// DEPRECATED: Use Headers dictionary instead.
    /// </summary>
    [Obsolete("Use Headers dictionary instead. This will be removed in a future version.")]
    public List<string> CustomHeaders
    {
        get => ConvertHeadersToList();
        set => ParseHeadersList(value);
    }

    /// <summary>
    /// Request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(ModelId);

        // Url is required
        if (string.IsNullOrWhiteSpace(Url))
        {
            throw new InvalidOperationException($"URL is required for model {Id}");
        }

        if (!Uri.TryCreate(Url, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException($"Invalid URL for model {Id}: {Url}");
        }

        if (MaxContextTokens < 512)
        {
            throw new InvalidOperationException($"MaxContextTokens must be at least 512 for model {Id}");
        }

        if (MaxOutputTokens < 128)
        {
            throw new InvalidOperationException($"MaxOutputTokens must be at least 128 for model {Id}");
        }

        if (CostFactor < 0)
        {
            throw new InvalidOperationException($"CostFactor cannot be negative for model {Id}");
        }
    }

    /// <summary>
    /// Get the default temperature for this model from DefaultParams.
    /// </summary>
    public double GetDefaultTemperature()
    {
        if (DefaultParams.TryGetValue("temperature", out var temp))
        {
            return temp.ValueKind == JsonValueKind.Number ? temp.GetDouble() : 0.7;
        }
        return 0.7;
    }

    /// <summary>
    /// Set the default temperature for this model.
    /// </summary>
    public void SetDefaultTemperature(double temperature)
    {
        DefaultParams["temperature"] = JsonDocument.Parse(temperature.ToString("F1")).RootElement.Clone();
    }

    private string ExtractBaseUrlFromUrl()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            return string.Empty;
        }

        // Extract base URL from full URL (e.g., https://api.openai.com/v1/chat/completions -> https://api.openai.com/v1)
        var uri = new Uri(Url);
        var pathParts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (pathParts.Length > 0)
        {
            // Remove last path segment (endpoint name)
            var basePath = string.Join("/", pathParts.Take(pathParts.Length - 1));
            return $"{uri.Scheme}://{uri.Host}{(string.IsNullOrEmpty(basePath) ? "" : "/" + basePath)}";
        }

        return $"{uri.Scheme}://{uri.Host}";
    }

    private string ExtractApiKeyFromHeaders()
    {
        if (Headers.TryGetValue("Authorization", out var authHeader))
        {
            return authHeader.Replace("Bearer ", "").Trim();
        }
        return string.Empty;
    }

    private List<string> ConvertHeadersToList()
    {
        return Headers
            .Where(h => h.Key != "Content-Type") // Exclude default header
            .Select(h => $"{h.Key}: {h.Value}")
            .ToList();
    }

    private void ParseHeadersList(List<string> headers)
    {
        foreach (var header in headers)
        {
            var parts = header.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                Headers[parts[0]] = parts[1];
            }
        }
    }

    public AiModelAssignment Clone() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        ModelId = ModelId,
        Url = Url,
        Headers = new Dictionary<string, string>(Headers),
        RequestTemplate = RequestTemplate == null ? null : JsonDocument.Parse(RequestTemplate.RootElement.GetRawText()),
        ResponseMapping = ResponseMapping == null ? null : new ResponseMapping
        {
            ContentPath = ResponseMapping.ContentPath,
            RolePath = ResponseMapping.RolePath,
            ToolCallsPath = ResponseMapping.ToolCallsPath,
            UsagePromptTokensPath = ResponseMapping.UsagePromptTokensPath,
            UsageCompletionTokensPath = ResponseMapping.UsageCompletionTokensPath,
            UsageTotalTokensPath = ResponseMapping.UsageTotalTokensPath,
            AutoDetected = ResponseMapping.AutoDetected,
            DetectedAt = ResponseMapping.DetectedAt
        },
        DefaultParams = new Dictionary<string, JsonElement>(
            DefaultParams.Select(kvp => new KeyValuePair<string, JsonElement>(kvp.Key, kvp.Value.Clone()))
        ),
        MaxContextTokens = MaxContextTokens,
        MaxOutputTokens = MaxOutputTokens,
        SupportsFunctionCalling = SupportsFunctionCalling,
        CostFactor = CostFactor,
        Enabled = Enabled,
        TestStatus = TestStatus,
        LastTestedAt = LastTestedAt,
        LastTestMessage = LastTestMessage,
        TimeoutSeconds = TimeoutSeconds
    };
}
