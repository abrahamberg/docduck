using System.Text.Json;

namespace DocDuck.Providers.Ai;

/// <summary>
/// Parses JSON responses from AI APIs using JSONPath extraction.
/// Reduces complexity by separating response parsing logic.
/// </summary>
internal static class JsonResponseParser
{
    public static ChatCompletionResult ParseChatCompletion(string json, ResponseMapping? mapping)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var effectiveMapping = mapping ?? ResponseMapping.OpenAiDefault();

        var role = ExtractJsonPath(root, effectiveMapping.RolePath) ?? "assistant";
        var content = ExtractJsonPath(root, effectiveMapping.ContentPath) ?? string.Empty;
        var toolCalls = ExtractToolCalls(root, effectiveMapping.ToolCallsPath);
        var (promptTokens, completionTokens, totalTokens) = ExtractUsage(root, effectiveMapping);

        return new ChatCompletionResult(
            Role: role,
            Content: content,
            ToolCalls: toolCalls,
            PromptTokens: promptTokens,
            CompletionTokens: completionTokens,
            TotalTokens: totalTokens
        );
    }

    public static float[][] ParseEmbeddingResponse(string json, Dictionary<string, string>? responseMapping)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var embeddingPath = GetEmbeddingPath(responseMapping);
        var results = new List<float[]>();

        if (ShouldIterateDataArray(embeddingPath))
        {
            ExtractEmbeddingsFromDataArray(root, embeddingPath, results);
        }

        return results.ToArray();
    }

    public static string? ExtractJsonPath(JsonElement root, string path)
    {
        var current = root;
        var parts = path.Split('.');

        foreach (var part in parts)
        {
            if (!TryProcessPathPart(ref current, part))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String
            ? current.GetString()
            : current.GetRawText();
    }

    private static bool TryProcessPathPart(ref JsonElement current, string part)
    {
        if (part.Contains('['))
        {
            return TryProcessArrayIndex(ref current, part);
        }

        if (!current.TryGetProperty(part, out var prop))
        {
            return false;
        }

        current = prop;
        return true;
    }

    private static bool TryProcessArrayIndex(ref JsonElement current, string part)
    {
        var bracketStart = part.IndexOf('[');
        var bracketEnd = part.IndexOf(']');

        if (bracketStart == -1 || bracketEnd == -1 || bracketEnd <= bracketStart)
        {
            return false;
        }

        var propertyName = part.Substring(0, bracketStart);
        var indexStr = part.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);

        if (!int.TryParse(indexStr, out var index))
        {
            return false;
        }

        if (!current.TryGetProperty(propertyName, out var arrayProp) || arrayProp.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var array = arrayProp.EnumerateArray().ToArray();
        if (index >= array.Length)
        {
            return false;
        }

        current = array[index];
        return true;
    }

    private static List<ToolCall> ExtractToolCalls(JsonElement root, string? toolCallsPath)
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
                var toolCall = ParseSingleToolCall(tc);
                if (toolCall != null)
                {
                    toolCalls.Add(toolCall);
                }
            }
        }
        catch (JsonException)
        {
            // Failed to parse tool calls, return empty list
        }

        return toolCalls;
    }

    private static ToolCall? ParseSingleToolCall(JsonElement tc)
    {
        try
        {
            var id = tc.TryGetProperty("id", out var idProp)
                ? idProp.GetString() ?? string.Empty
                : string.Empty;

            var function = tc.GetProperty("function");
            var name = function.GetProperty("name").GetString() ?? string.Empty;
            var args = function.TryGetProperty("arguments", out var argsProp) && argsProp.ValueKind == JsonValueKind.String
                ? argsProp.GetString() ?? "{}"
                : "{}";

            return new ToolCall(id, name, args);
        }
        catch
        {
            return null;
        }
    }

    private static (int PromptTokens, int CompletionTokens, int TotalTokens) ExtractUsage(
        JsonElement root,
        ResponseMapping mapping)
    {
        var promptTokens = TryExtractIntValue(root, mapping.UsagePromptTokensPath);
        var completionTokens = TryExtractIntValue(root, mapping.UsageCompletionTokensPath);
        var totalTokens = TryExtractIntValue(root, mapping.UsageTotalTokensPath);

        // Calculate total if not provided
        if (totalTokens == 0 && (promptTokens > 0 || completionTokens > 0))
        {
            totalTokens = promptTokens + completionTokens;
        }

        return (promptTokens, completionTokens, totalTokens);
    }

    private static int TryExtractIntValue(JsonElement root, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return 0;
        }

        var valueStr = ExtractJsonPath(root, path);
        return int.TryParse(valueStr, out var value) ? value : 0;
    }

    private static string GetEmbeddingPath(Dictionary<string, string>? responseMapping)
    {
        if (responseMapping == null || !responseMapping.TryGetValue("embedding", out var path))
        {
            return "$.data[0].embedding";
        }

        return path;
    }

    private static bool ShouldIterateDataArray(string embeddingPath)
    {
        return embeddingPath.StartsWith("$.data[") || embeddingPath.StartsWith("data[");
    }

    private static void ExtractEmbeddingsFromDataArray(
        JsonElement root,
        string embeddingPath,
        List<float[]> results)
    {
        var dataPath = GetDataPropertyName(embeddingPath);

        if (!root.TryGetProperty(dataPath, out var dataArray) || dataArray.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in dataArray.EnumerateArray())
        {
            if (item.TryGetProperty("embedding", out var embeddingElement))
            {
                var vector = ExtractFloatArray(embeddingElement);
                results.Add(vector);
            }
        }
    }

    private static string GetDataPropertyName(string embeddingPath)
    {
        if (embeddingPath.Contains("data[*]") || embeddingPath.Contains("data[0]"))
        {
            return "data";
        }

        return embeddingPath.Split('.')[0].TrimStart('$', '.');
    }

    private static float[] ExtractFloatArray(JsonElement embeddingElement)
    {
        var vector = new List<float>();

        foreach (var value in embeddingElement.EnumerateArray())
        {
            vector.Add((float)value.GetDouble());
        }

        return vector.ToArray();
    }
}
