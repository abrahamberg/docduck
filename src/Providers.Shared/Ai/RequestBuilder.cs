using System.Text.Json;
using System.Text.Json.Nodes;

namespace DocDuck.Providers.Ai;

/// <summary>
/// Builds chat completion request payloads using templates or default structures.
/// Reduces complexity in GenericAiHttpClient by separating request construction logic.
/// </summary>
internal sealed class ChatRequestBuilder
{
    private readonly AiModelAssignment _model;

    public ChatRequestBuilder(AiModelAssignment model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public (string Json, string Endpoint) BuildRequest(
        List<ChatMessagePayload> messages,
        double? temperature,
        int? maxTokens,
        List<ToolDefinition>? tools,
        string? toolChoice)
    {
        if (_model.RequestTemplate != null)
        {
            return BuildTemplateBasedRequest(messages, temperature, maxTokens, tools, toolChoice);
        }

        return BuildDefaultOpenAiRequest(messages, temperature, maxTokens, tools, toolChoice);
    }

    private (string Json, string Endpoint) BuildTemplateBasedRequest(
        List<ChatMessagePayload> messages,
        double? temperature,
        int? maxTokens,
        List<ToolDefinition>? tools,
        string? toolChoice)
    {
        var context = new TemplateContext(
            ModelId: _model.ModelId,
            Messages: messages,
            Temperature: temperature,
            MaxTokens: maxTokens,
            Tools: tools,
            ToolChoice: toolChoice
        );

        var templateString = _model.RequestTemplate!.RootElement.GetString()
            ?? _model.RequestTemplate.RootElement.GetRawText();
        var substituted = TemplateSubstitutionService.Substitute(templateString, context);

        using var doc = JsonDocument.Parse(substituted);
        var payload = JsonSerializer.Deserialize<JsonObject>(doc.RootElement.GetRawText())
            ?? new JsonObject();

        AddToolsIfProvided(payload, tools, toolChoice);

        var json = MergeDefaultParams(payload.ToJsonString(), _model.DefaultParams);
        return (json, string.Empty); // Url already includes full path
    }

    private (string Json, string Endpoint) BuildDefaultOpenAiRequest(
        List<ChatMessagePayload> messages,
        double? temperature,
        int? maxTokens,
        List<ToolDefinition>? tools,
        string? toolChoice)
    {
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

        AddToolsIfProvided(payload, tools, toolChoice);

        return (payload.ToJsonString(), "chat/completions");
    }

    private void AddToolsIfProvided(JsonObject payload, List<ToolDefinition>? tools, string? toolChoice)
    {
        if (tools == null || tools.Count == 0)
        {
            return;
        }

        if (!_model.SupportsFunctionCalling)
        {
            // Skip adding tools if not supported
            return;
        }

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

    private static string MergeDefaultParams(string requestJson, Dictionary<string, JsonElement>? defaultParams)
    {
        if (defaultParams == null || defaultParams.Count == 0)
        {
            return requestJson;
        }

        var obj = JsonSerializer.Deserialize<JsonObject>(requestJson);
        if (obj == null)
        {
            return requestJson;
        }

        foreach (var (key, value) in defaultParams)
        {
            if (!obj.ContainsKey(key))
            {
                obj[key] = JsonNode.Parse(value.GetRawText());
            }
        }

        return obj.ToJsonString();
    }
}

/// <summary>
/// Builds embedding request payloads and handles template substitution.
/// </summary>
internal sealed class EmbeddingRequestBuilder
{
    public static string BuildRequest(
        AiEmbeddingModelAssignment model,
        List<string> texts)
    {
        if (model.RequestTemplate != null)
        {
            return BuildTemplateBasedRequest(model, texts);
        }

        return BuildDefaultOpenAiRequest(model, texts);
    }

    private static string BuildTemplateBasedRequest(
        AiEmbeddingModelAssignment model,
        List<string> texts)
    {
        var templateString = model.RequestTemplate!.RootElement.GetString()
            ?? model.RequestTemplate.RootElement.GetRawText();

        var inputJson = JsonSerializer.Serialize(texts);

        // Replace placeholders - handle both quoted and unquoted {INPUT}
        if (templateString.Contains("\"{INPUT}\""))
        {
            return templateString
                .Replace("{MODEL_ID}", model.ModelId)
                .Replace("\"{INPUT}\"", inputJson);
        }

        return templateString
            .Replace("{MODEL_ID}", model.ModelId)
            .Replace("{INPUT}", inputJson);
    }

    private static string BuildDefaultOpenAiRequest(
        AiEmbeddingModelAssignment model,
        List<string> texts)
    {
        var payload = new JsonObject
        {
            ["model"] = model.ModelId,
            ["input"] = new JsonArray(texts.Select(t => JsonValue.Create(t)).ToArray())
        };

        return payload.ToJsonString();
    }
}
