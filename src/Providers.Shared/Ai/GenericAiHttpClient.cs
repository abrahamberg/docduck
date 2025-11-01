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
public sealed class GenericAiHttpClient(AiModelAssignment model, ILogger? logger = null) : IDisposable
{
    private readonly AiModelAssignment _model = ValidateModel(model);
    private readonly HttpClient _httpClient = InitializeHttpClient(ValidateModel(model));
    private readonly ILogger? _logger = logger;

    private static AiModelAssignment ValidateModel(AiModelAssignment model)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(model.Url);
        return model;
    }

    private static HttpClient InitializeHttpClient(AiModelAssignment model)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(model.Url, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(model.TimeoutSeconds)
        };

        HttpClientConfigurator.ConfigureHeaders(client, model.Headers);
        return client;
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

        var requestBuilder = new ChatRequestBuilder(_model);
        var (json, endpoint) = requestBuilder.BuildRequest(messages, temperature, maxTokens, tools, toolChoice);

        if (tools != null && tools.Count > 0 && !_model.SupportsFunctionCalling)
        {
            _logger?.LogWarning("Model {Model} does not support function calling, ignoring tools", _model.ModelId);
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

        return JsonResponseParser.ParseChatCompletion(responseBody, _model.ResponseMapping);
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

        using var embedClient = CreateEmbeddingHttpClient(embeddingModel);

        var json = EmbeddingRequestBuilder.BuildRequest(embeddingModel, textList);

        _logger?.LogDebug("Embedding request JSON for {Model}: {Json}",
            embeddingModel.ModelId, json);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await embedClient.PostAsync(embeddingModel.Url, content, ct);

        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogError("Embedding failed for {Model} (status {Status}): {Body}. Request JSON: {RequestJson}",
                embeddingModel.ModelId, (int)response.StatusCode, responseBody, json);
            throw new HttpRequestException($"Embedding failed with status {(int)response.StatusCode}: {responseBody}\n\nRequest JSON was: {json}");
        }

        return JsonResponseParser.ParseEmbeddingResponse(responseBody, embeddingModel.ResponseMapping);
    }

    private static HttpClient CreateEmbeddingHttpClient(AiEmbeddingModelAssignment embeddingModel)
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(embeddingModel.TimeoutSeconds)
        };

        HttpClientConfigurator.ConfigureHeaders(client, embeddingModel.Headers);

        return client;
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
