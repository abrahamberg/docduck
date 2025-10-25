using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace DocDuck.Providers.Ai;

/// <summary>
/// Generic HTTP client for OpenAI-compatible inference APIs.
/// Works with OpenAI, Azure Foundry, local servers (llama.cpp, vllm, ollama with OpenAI shim), and other compatible endpoints.
/// </summary>
public sealed class GenericAiHttpClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AiModelAssignment _model;
    private readonly ILogger? _logger;

    public GenericAiHttpClient(AiModelAssignment model, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.Validate();

        _model = model;
        _logger = logger;

        // Use new Url property, fall back to deprecated BaseUrl for backward compatibility
        var baseUrl = !string.IsNullOrWhiteSpace(model.Url) 
            ? model.Url 
            : model.BaseUrl;

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(model.TimeoutSeconds)
        };

        // Set headers from new Headers dictionary if available
        if (model.Headers != null && model.Headers.Count > 0)
        {
            foreach (var (key, value) in model.Headers)
            {
                if (key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = value.Split(' ', 2, StringSplitOptions.TrimEntries);
                    if (parts.Length == 2)
                    {
                        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(parts[0], parts[1]);
                    }
                }
                else if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    // Content-Type is set per-request, skip here
                    continue;
                }
                else
                {
                    _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
                }
            }
        }
        else
        {
            // Fall back to deprecated properties for backward compatibility
            if (!string.IsNullOrWhiteSpace(model.ApiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", model.ApiKey);
            }

            foreach (var header in model.CustomHeaders)
            {
                var parts = header.Split(':', 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2)
                {
                    _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(parts[0], parts[1]);
                }
            }
        }
    }

    /// <summary>
    /// Generate a chat completion using the assigned model.
    /// Uses RequestTemplate and ResponseMapping if configured, otherwise defaults to OpenAI-compatible format.
    /// </summary>
    public async Task<ChatCompletionResult> CompleteChatAsync(
        List<ChatMessagePayload> messages,
        double? temperature = null,
        int? maxTokens = null,
        List<ToolDefinition>? tools = null,
        string? toolChoice = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (messages.Count == 0)
        {
            throw new ArgumentException("At least one message is required", nameof(messages));
        }

        string json;
        string endpoint;

        // Use template-based request if configured
        if (_model.RequestTemplate != null)
        {
            var effectiveTemp = temperature ?? _model.GetDefaultTemperature();
            var effectiveMaxTokens = maxTokens ?? _model.MaxOutputTokens;

            var context = new TemplateContext(
                ModelId: _model.ModelId,
                Messages: messages,
                Temperature: effectiveTemp,
                MaxTokens: effectiveMaxTokens,
                Tools: tools,
                ToolChoice: toolChoice
            );

            // Template is stored as a JSON string value, so deserialize it first
            var templateString = _model.RequestTemplate.RootElement.GetString() 
                ?? _model.RequestTemplate.RootElement.GetRawText();
            json = TemplateSubstitutionService.Substitute(templateString, context);
            endpoint = string.Empty; // Url already includes full path
        }
        else
        {
            // Fall back to OpenAI-compatible format
            var payload = new JsonObject
            {
                ["model"] = _model.ModelId,
                ["messages"] = new JsonArray(messages.Select(m => new JsonObject
                {
                    ["role"] = m.Role,
                    ["content"] = m.Content
                }).ToArray())
            };

            if (temperature.HasValue)
            {
                payload["temperature"] = temperature.Value;
            }

            if (maxTokens.HasValue)
            {
                payload["max_tokens"] = maxTokens.Value;
            }
            else
            {
                payload["max_tokens"] = _model.MaxOutputTokens;
            }

            // Add tools if provided and supported
            if (tools != null && tools.Count > 0)
            {
                if (!_model.SupportsFunctionCalling)
                {
                    _logger?.LogWarning("Model {Model} does not support function calling, ignoring tools", _model.ModelId);
                }
                else
                {
                    payload["tools"] = new JsonArray(tools.Select(t => new JsonObject
                    {
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = t.Name,
                            ["description"] = t.Description,
                            ["parameters"] = JsonNode.Parse(t.ParametersJson)
                        }
                    }).ToArray());

                    if (!string.IsNullOrWhiteSpace(toolChoice))
                    {
                        payload["tool_choice"] = toolChoice;
                    }
                }
            }

            json = payload.ToJsonString();
            endpoint = "chat/completions";
        }

        _logger?.LogDebug("Chat completion request to {Model}: {Payload}", _model.ModelId, json);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(endpoint, content, ct);

        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogError("Chat completion failed for {Model} (status {Status}): {Body}", 
                _model.ModelId, (int)response.StatusCode, responseBody);
            throw new HttpRequestException($"Chat completion failed with status {(int)response.StatusCode}: {responseBody}");
        }

        _logger?.LogDebug("Chat completion response from {Model}: {Response}", _model.ModelId, responseBody);

        return ParseChatCompletionResponse(responseBody);
    }

    /// <summary>
    /// Generate embeddings for a single text input.
    /// </summary>
    public async Task<float[]> EmbedAsync(
        AiEmbeddingModelAssignment embeddingModel,
        string text,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(embeddingModel);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var results = await EmbedBatchAsync(embeddingModel, new[] { text }, ct);
        return results[0];
    }

    /// <summary>
    /// Generate embeddings for multiple text inputs.
    /// </summary>
    public async Task<float[][]> EmbedBatchAsync(
        AiEmbeddingModelAssignment embeddingModel,
        IEnumerable<string> texts,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(embeddingModel);
        ArgumentNullException.ThrowIfNull(texts);

        var textList = texts.ToList();
        if (textList.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        // Create a temporary client for embedding endpoint
        using var embedClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(embeddingModel.TimeoutSeconds)
        };

        // Apply headers (including Authorization)
        foreach (var header in embeddingModel.Headers)
        {
            if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                var authParts = header.Value.Split(' ', 2, StringSplitOptions.TrimEntries);
                if (authParts.Length == 2)
                {
                    embedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(authParts[0], authParts[1]);
                }
            }
            else
            {
                embedClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        // Build request body using template or default structure
        string json;
        if (embeddingModel.RequestTemplate != null)
        {
            // Extract template string (it's wrapped as JSON string)
            var templateString = embeddingModel.RequestTemplate.RootElement.GetString() 
                ?? embeddingModel.RequestTemplate.RootElement.GetRawText();

            // For batch embedding, we need to serialize the input array
            var inputJson = JsonSerializer.Serialize(textList);

            // Replace placeholders
            // Important: The template should have {INPUT} without quotes, or "{INPUT}" with quotes
            // If it has quotes, we need to replace the whole thing including quotes
            if (templateString.Contains("\"{INPUT}\""))
            {
                json = templateString
                    .Replace("{MODEL_ID}", embeddingModel.ModelId)
                    .Replace("\"{INPUT}\"", inputJson); // Replace quoted placeholder with raw JSON
            }
            else
            {
                json = templateString
                    .Replace("{MODEL_ID}", embeddingModel.ModelId)
                    .Replace("{INPUT}", inputJson); // Replace unquoted placeholder with raw JSON
            }

            _logger?.LogDebug("Embedding request JSON for {Model}: {Json}", 
                embeddingModel.ModelId, json);
        }
        else
        {
            // Fallback to default OpenAI structure
            var payload = new JsonObject
            {
                ["model"] = embeddingModel.ModelId,
                ["input"] = new JsonArray(textList.Select(t => JsonValue.Create(t)).ToArray())
            };
            json = payload.ToJsonString();
            _logger?.LogDebug("Using default embedding request for {Model} with {Count} texts", 
                embeddingModel.ModelId, textList.Count);
        }

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await embedClient.PostAsync(embeddingModel.Url, content, ct);

        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogError("Embedding failed for {Model} (status {Status}): {Body}. Request JSON: {RequestJson}",
                embeddingModel.ModelId, (int)response.StatusCode, responseBody, json);
            throw new HttpRequestException($"Embedding failed with status {(int)response.StatusCode}: {responseBody}\n\nRequest JSON was: {json}");
        }

        return ParseEmbeddingResponseWithMapping(responseBody, embeddingModel.ResponseMapping);
    }

    private ChatCompletionResult ParseChatCompletionResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Use ResponseMapping if configured, otherwise use default OpenAI paths
        var mapping = _model.ResponseMapping ?? ResponseMapping.OpenAiDefault();

        var role = ExtractJsonPath(root, mapping.RolePath) ?? "assistant";
        var content = ExtractJsonPath(root, mapping.ContentPath) ?? string.Empty;
        var toolCalls = ExtractToolCallsWithMapping(root, mapping.ToolCallsPath);

        var (promptTokens, completionTokens, totalTokens) = ExtractUsageWithMapping(root, mapping);

        return new ChatCompletionResult(
            Role: role,
            Content: content,
            ToolCalls: toolCalls,
            PromptTokens: promptTokens,
            CompletionTokens: completionTokens,
            TotalTokens: totalTokens
        );
    }

    private static string? ExtractJsonPath(JsonElement root, string path)
    {
        var current = root;
        var parts = path.Split('.');

        foreach (var part in parts)
        {
            // Handle array indexing like "choices[0]"
            if (part.Contains('['))
            {
                var arrayParts = part.Split('[', ']', StringSplitOptions.RemoveEmptyEntries);
                if (arrayParts.Length != 2 || !int.TryParse(arrayParts[1], out var index))
                {
                    return null;
                }

                if (!current.TryGetProperty(arrayParts[0], out var arrayProp) || arrayProp.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                var array = arrayProp.EnumerateArray().ToArray();
                if (index >= array.Length)
                {
                    return null;
                }

                current = array[index];
            }
            else
            {
                if (!current.TryGetProperty(part, out var prop))
                {
                    return null;
                }
                current = prop;
            }
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : current.GetRawText();
    }

    private static List<ToolCall> ExtractToolCallsWithMapping(JsonElement root, string? toolCallsPath)
    {
        var toolCalls = new List<ToolCall>();

        if (string.IsNullOrWhiteSpace(toolCallsPath))
        {
            return toolCalls;
        }

        var toolCallsJson = ExtractJsonPath(root, toolCallsPath);
        if (string.IsNullOrWhiteSpace(toolCallsJson))
        {
            return toolCalls;
        }

        try
        {
            using var toolCallsDoc = JsonDocument.Parse(toolCallsJson);
            if (toolCallsDoc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return toolCalls;
            }

            foreach (var tc in toolCallsDoc.RootElement.EnumerateArray())
            {
                var id = tc.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
                var function = tc.GetProperty("function");
                var name = function.GetProperty("name").GetString() ?? string.Empty;
                var args = function.TryGetProperty("arguments", out var argsProp) && argsProp.ValueKind == JsonValueKind.String
                    ? argsProp.GetString() ?? "{}"
                    : "{}";

                toolCalls.Add(new ToolCall(id, name, args));
            }
        }
        catch (JsonException)
        {
            // Failed to parse tool calls, return empty list
        }

        return toolCalls;
    }

    private static (int PromptTokens, int CompletionTokens, int TotalTokens) ExtractUsageWithMapping(JsonElement root, ResponseMapping mapping)
    {
        var promptTokens = 0;
        var completionTokens = 0;
        var totalTokens = 0;

        if (!string.IsNullOrWhiteSpace(mapping.UsagePromptTokensPath))
        {
            var promptTokensStr = ExtractJsonPath(root, mapping.UsagePromptTokensPath);
            if (int.TryParse(promptTokensStr, out var pt))
            {
                promptTokens = pt;
            }
        }

        if (!string.IsNullOrWhiteSpace(mapping.UsageCompletionTokensPath))
        {
            var completionTokensStr = ExtractJsonPath(root, mapping.UsageCompletionTokensPath);
            if (int.TryParse(completionTokensStr, out var ct))
            {
                completionTokens = ct;
            }
        }

        if (!string.IsNullOrWhiteSpace(mapping.UsageTotalTokensPath))
        {
            var totalTokensStr = ExtractJsonPath(root, mapping.UsageTotalTokensPath);
            if (int.TryParse(totalTokensStr, out var tt))
            {
                totalTokens = tt;
            }
        }

        // If total not provided, calculate from prompt + completion
        if (totalTokens == 0 && (promptTokens > 0 || completionTokens > 0))
        {
            totalTokens = promptTokens + completionTokens;
        }

        return (promptTokens, completionTokens, totalTokens);
    }

    private static float[][] ParseEmbeddingResponseWithMapping(string json, Dictionary<string, string>? responseMapping)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // If no mapping provided, use default OpenAI structure
        if (responseMapping == null || !responseMapping.TryGetValue("embedding", out var embeddingPath))
        {
            embeddingPath = "$.data[0].embedding";
        }

        var results = new List<float[]>();

        // Handle array of embeddings (batch response)
        // For OpenAI: $.data[*].embedding means iterate over data array
        if (embeddingPath.StartsWith("$.data[") || embeddingPath.StartsWith("data["))
        {
            var dataPath = embeddingPath.Contains("data[*]") || embeddingPath.Contains("data[0]")
                ? "data"
                : embeddingPath.Split('.')[0].TrimStart('$', '.');

            if (root.TryGetProperty(dataPath, out var dataArray) && dataArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in dataArray.EnumerateArray())
                {
                    if (item.TryGetProperty("embedding", out var embeddingElement))
                    {
                        var vector = new List<float>();
                        foreach (var value in embeddingElement.EnumerateArray())
                        {
                            vector.Add((float)value.GetDouble());
                        }
                        results.Add(vector.ToArray());
                    }
                }
            }
        }

        return results.ToArray();
    }

    [Obsolete("Use ParseEmbeddingResponseWithMapping instead")]
    private static float[][] ParseEmbeddingResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var data = root.GetProperty("data");
        var results = new List<float[]>();

        foreach (var item in data.EnumerateArray())
        {
            var embedding = item.GetProperty("embedding");
            var vector = new List<float>();

            foreach (var value in embedding.EnumerateArray())
            {
                vector.Add((float)value.GetDouble());
            }

            results.Add(vector.ToArray());
        }

        return results.ToArray();
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

/// <summary>
/// Represents a chat message in the conversation.
/// </summary>
public sealed record ChatMessagePayload(string Role, string Content);

/// <summary>
/// Result from a chat completion request.
/// </summary>
public sealed record ChatCompletionResult(
    string Role,
    string Content,
    List<ToolCall> ToolCalls,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens
);

/// <summary>
/// Represents a function/tool call from the model.
/// </summary>
public sealed record ToolCall(string Id, string FunctionName, string ArgumentsJson);

/// <summary>
/// Definition of a tool/function the model can call.
/// </summary>
public sealed record ToolDefinition(string Name, string Description, string ParametersJson);
