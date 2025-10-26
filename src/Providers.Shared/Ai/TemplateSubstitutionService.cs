using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DocDuck.Providers.Ai;

/// <summary>
/// Context for template variable substitution.
/// </summary>
public sealed record TemplateContext(
    string ModelId,
    List<ChatMessagePayload>? Messages = null,
    double? Temperature = null,
    int? MaxTokens = null,
    List<ToolDefinition>? Tools = null,
    string? ToolChoice = null,
    Dictionary<string, string>? CustomVariables = null
);

/// <summary>
/// Service for substituting template variables in request bodies.
/// Supports: {MODEL_ID}, {MESSAGES}, {TEMPERATURE}, {MAX_TOKENS}, {TOOLS}, {TOOL_CHOICE}, {SYSTEM_PROMPT}, {USER_PROMPT}
/// </summary>
public static class TemplateSubstitutionService
{
    /// <summary>
    /// Substitute variables in a request template.
    /// </summary>
    public static string Substitute(string template, TemplateContext context)
    {
        var result = template;

        // Simple string replacement for scalar values
        result = result.Replace("{MODEL_ID}", context.ModelId);

        if (context.Temperature.HasValue)
        {
            result = result.Replace("{TEMPERATURE}", context.Temperature.Value.ToString("F1"));
        }

        if (context.MaxTokens.HasValue)
        {
            result = result.Replace("{MAX_TOKENS}", context.MaxTokens.Value.ToString());
        }

        // Complex JSON replacements
        if (context.Messages != null && context.Messages.Count > 0)
        {
            var messagesJson = SerializeMessages(context.Messages);
            result = result.Replace("{MESSAGES}", messagesJson);
        }

        if (context.Tools != null && context.Tools.Count > 0)
        {
            var toolsJson = SerializeTools(context.Tools);
            result = result.Replace("{TOOLS}", toolsJson);
        }

        if (!string.IsNullOrWhiteSpace(context.ToolChoice))
        {
            result = result.Replace("{TOOL_CHOICE}", $"\"{context.ToolChoice}\"");
        }

        // Custom variables (e.g., {SYSTEM_PROMPT}, {USER_PROMPT})
        if (context.CustomVariables != null)
        {
            foreach (var (key, value) in context.CustomVariables)
            {
                var placeholder = $"{{{key}}}";

                // If value looks like JSON, insert as-is; otherwise quote it
                var substitution = IsJsonValue(value) ? value : JsonSerializer.Serialize(value);
                result = result.Replace(placeholder, substitution);
            }
        }

        return result;
    }

    /// <summary>
    /// Substitute variables in a JsonDocument template.
    /// </summary>
    public static JsonDocument SubstituteJson(JsonDocument template, TemplateContext context)
    {
        var templateString = template.RootElement.GetRawText();
        var substituted = Substitute(templateString, context);
        return JsonDocument.Parse(substituted);
    }

    private static string SerializeMessages(List<ChatMessagePayload> messages)
    {
        var array = new JsonArray();
        foreach (var msg in messages)
        {
            array.Add(new JsonObject
            {
                ["role"] = msg.Role,
                ["content"] = msg.Content
            });
        }
        return array.ToJsonString();
    }

    private static string SerializeTools(List<ToolDefinition> tools)
    {
        var array = new JsonArray();
        foreach (var tool in tools)
        {
            array.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = JsonNode.Parse(tool.ParametersJson)
                }
            });
        }
        return array.ToJsonString();
    }

    private static bool IsJsonValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return (trimmed.StartsWith('{') && trimmed.EndsWith('}')) ||
               (trimmed.StartsWith('[') && trimmed.EndsWith(']')) ||
               trimmed == "null" ||
               trimmed == "true" ||
               trimmed == "false" ||
               double.TryParse(trimmed, out _);
    }
}
