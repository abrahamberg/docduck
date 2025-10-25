using System.Text.Json;

namespace DocDuck.Providers.Ai;

/// <summary>
/// Flexible AI model configuration supporting any OpenAI-compatible API.
/// Allows full customization of request/response structure via templates.
/// </summary>
public sealed class FlexibleAiModel
{
    /// <summary>
    /// Unique identifier for this model.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Model identifier for API requests (e.g., "gpt-4", "mistral-large").
    /// </summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>
    /// Full API endpoint URL (e.g., "https://api.openai.com/v1/chat/completions").
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// HTTP headers as JSON object.
    /// Default: {"Content-Type": "application/json"}
    /// Can include Authorization and custom headers.
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new()
    {
        ["Content-Type"] = "application/json"
    };

    /// <summary>
    /// Request body template with variable placeholders.
    /// Supports: {MODEL_ID}, {MESSAGES}, {TEMPERATURE}, {MAX_TOKENS}, {TOOLS}, {TOOL_CHOICE}
    /// </summary>
    public JsonDocument RequestTemplate { get; set; } = JsonDocument.Parse("{}");

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
    /// Maximum context window size in tokens.
    /// </summary>
    public int MaxContextTokens { get; set; } = 4096;

    /// <summary>
    /// Maximum output tokens.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 2048;

    /// <summary>
    /// Whether this model supports function/tool calling.
    /// </summary>
    public bool SupportsFunctionCalling { get; set; } = true;

    /// <summary>
    /// Relative cost factor (1.0 = baseline).
    /// </summary>
    public double CostFactor { get; set; } = 1.0;

    /// <summary>
    /// Whether this model is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Test status.
    /// </summary>
    public ModelTestStatus TestStatus { get; set; } = ModelTestStatus.Untested;

    /// <summary>
    /// Last test timestamp.
    /// </summary>
    public DateTimeOffset? LastTestedAt { get; set; }

    /// <summary>
    /// Last test message.
    /// </summary>
    public string? LastTestMessage { get; set; }

    /// <summary>
    /// Request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(ModelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Url);

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
}

/// <summary>
/// JSONPath-based mapping for extracting data from API responses.
/// Auto-detected on first successful call.
/// </summary>
public sealed class ResponseMapping
{
    /// <summary>
    /// JSONPath to extract assistant's response content.
    /// Default OpenAI: "choices[0].message.content"
    /// </summary>
    public string ContentPath { get; set; } = "choices[0].message.content";

    /// <summary>
    /// JSONPath to extract message role.
    /// Default OpenAI: "choices[0].message.role"
    /// </summary>
    public string RolePath { get; set; } = "choices[0].message.role";

    /// <summary>
    /// JSONPath to extract tool calls (if supported).
    /// Default OpenAI: "choices[0].message.tool_calls"
    /// </summary>
    public string? ToolCallsPath { get; set; } = "choices[0].message.tool_calls";

    /// <summary>
    /// JSONPath to extract prompt tokens count.
    /// Default OpenAI: "usage.prompt_tokens"
    /// </summary>
    public string? UsagePromptTokensPath { get; set; } = "usage.prompt_tokens";

    /// <summary>
    /// JSONPath to extract completion tokens count.
    /// Default OpenAI: "usage.completion_tokens"
    /// </summary>
    public string? UsageCompletionTokensPath { get; set; } = "usage.completion_tokens";

    /// <summary>
    /// JSONPath to extract total tokens count.
    /// Default OpenAI: "usage.total_tokens"
    /// </summary>
    public string? UsageTotalTokensPath { get; set; } = "usage.total_tokens";

    /// <summary>
    /// Whether this mapping was auto-detected (true) or manually configured (false).
    /// </summary>
    public bool AutoDetected { get; set; } = false;

    /// <summary>
    /// Timestamp when this mapping was created/updated.
    /// </summary>
    public DateTimeOffset? DetectedAt { get; set; }

    /// <summary>
    /// Creates a default OpenAI-compatible response mapping.
    /// </summary>
    public static ResponseMapping OpenAiDefault() => new()
    {
        ContentPath = "choices[0].message.content",
        RolePath = "choices[0].message.role",
        ToolCallsPath = "choices[0].message.tool_calls",
        UsagePromptTokensPath = "usage.prompt_tokens",
        UsageCompletionTokensPath = "usage.completion_tokens",
        UsageTotalTokensPath = "usage.total_tokens",
        AutoDetected = false
    };
}

/// <summary>
/// Default OpenAI-compatible request template.
/// </summary>
public static class DefaultRequestTemplates
{
    /// <summary>
    /// Standard OpenAI chat completions format.
    /// </summary>
    public static readonly string OpenAiChat = """
    {
      "model": "{MODEL_ID}",
      "messages": {MESSAGES},
      "temperature": {TEMPERATURE},
      "max_tokens": {MAX_TOKENS},
      "stream": false
    }
    """;

    /// <summary>
    /// OpenAI format with tools support.
    /// </summary>
    public static readonly string OpenAiChatWithTools = """
    {
      "model": "{MODEL_ID}",
      "messages": {MESSAGES},
      "temperature": {TEMPERATURE},
      "max_tokens": {MAX_TOKENS},
      "tools": {TOOLS},
      "tool_choice": {TOOL_CHOICE},
      "stream": false
    }
    """;

    /// <summary>
    /// Get default response mapping for OpenAI-compatible APIs.
    /// </summary>
    public static ResponseMapping OpenAiResponseMapping => new()
    {
        ContentPath = "choices[0].message.content",
        RolePath = "choices[0].message.role",
        ToolCallsPath = "choices[0].message.tool_calls",
        UsagePromptTokensPath = "usage.prompt_tokens",
        UsageCompletionTokensPath = "usage.completion_tokens",
        UsageTotalTokensPath = "usage.total_tokens",
        AutoDetected = false,
        DetectedAt = DateTimeOffset.UtcNow
    };
}
