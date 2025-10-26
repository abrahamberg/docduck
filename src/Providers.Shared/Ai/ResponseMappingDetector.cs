using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DocDuck.Providers.Ai;

/// <summary>
/// Auto-detects response structure from API responses and creates ResponseMapping.
/// </summary>
public sealed class ResponseMappingDetector
{
    private const string ContentProperty = "content";
    private const string UsageProperty = "usage";
    
    private readonly ILogger<ResponseMappingDetector>? _logger;

    public ResponseMappingDetector(ILogger<ResponseMappingDetector>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyze a successful API response and detect the structure.
    /// Returns a ResponseMapping with JSONPath expressions to extract data.
    /// </summary>
    public ResponseMapping DetectMapping(string jsonResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            var root = doc.RootElement;

            var mapping = new ResponseMapping
            {
                AutoDetected = true,
                DetectedAt = DateTimeOffset.UtcNow
            };

            // Try to detect content path
            mapping.ContentPath = DetectContentPath(root);
            mapping.RolePath = DetectRolePath(root);
            mapping.ToolCallsPath = DetectToolCallsPath(root);
            mapping.UsagePromptTokensPath = DetectUsagePromptTokensPath(root);
            mapping.UsageCompletionTokensPath = DetectUsageCompletionTokensPath(root);
            mapping.UsageTotalTokensPath = DetectUsageTotalTokensPath(root);

            _logger?.LogInformation(
                "Auto-detected response mapping: content={Content}, role={Role}, usage={Usage}",
                mapping.ContentPath,
                mapping.RolePath,
                mapping.UsagePromptTokensPath);

            return mapping;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to auto-detect response mapping, falling back to OpenAI defaults");
            return DefaultRequestTemplates.OpenAiResponseMapping;
        }
    }

    /// <summary>
    /// Extract the actual content value from a response using a detected mapping.
    /// </summary>
    public string? ExtractContent(string jsonResponse, ResponseMapping mapping)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            return ExtractValueByPath(doc.RootElement, mapping.ContentPath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to extract content using path {Path}", mapping.ContentPath);
            return null;
        }
    }

    private static string DetectContentPath(JsonElement root)
    {
        // Pattern 1: OpenAI format - choices[0].message.content
        if (TryGetPropertyPath(root, ["choices", "0", "message", "content"], out _))
        {
            return "choices[0].message.content";
        }

        // Pattern 2: Anthropic format - content[0].text
        if (TryGetPropertyPath(root, ["content", "0", "text"], out _))
        {
            return "content[0].text";
        }

        // Pattern 3: Simple response - content or text or response
        if (root.TryGetProperty(ContentProperty, out _))
        {
            return ContentProperty;
        }
        if (root.TryGetProperty("text", out _))
        {
            return "text";
        }
        if (root.TryGetProperty("response", out _))
        {
            return "response";
        }

        // Pattern 4: Nested - data.content, result.content, output.text
        if (TryGetPropertyPath(root, ["data", ContentProperty], out _))
        {
            return "data.content";
        }
        if (TryGetPropertyPath(root, ["result", ContentProperty], out _))
        {
            return "result.content";
        }
        if (TryGetPropertyPath(root, ["output", "text"], out _))
        {
            return "output.text";
        }

        // Default fallback
        return "choices[0].message.content";
    }

    private static string DetectRolePath(JsonElement root)
    {
        // OpenAI format
        if (TryGetPropertyPath(root, ["choices", "0", "message", "role"], out _))
        {
            return "choices[0].message.role";
        }

        // Anthropic format
        if (TryGetPropertyPath(root, ["role"], out _))
        {
            return "role";
        }

        return "choices[0].message.role";
    }

    private static string? DetectToolCallsPath(JsonElement root)
    {
        if (TryGetPropertyPath(root, ["choices", "0", "message", "tool_calls"], out _))
        {
            return "choices[0].message.tool_calls";
        }

        return null;
    }

    private static string? DetectUsagePromptTokensPath(JsonElement root)
    {
        if (TryGetPropertyPath(root, [UsageProperty, "prompt_tokens"], out _))
        {
            return "usage.prompt_tokens";
        }

        if (TryGetPropertyPath(root, [UsageProperty, "input_tokens"], out _))
        {
            return "usage.input_tokens";
        }

        return null;
    }

    private static string? DetectUsageCompletionTokensPath(JsonElement root)
    {
        if (TryGetPropertyPath(root, [UsageProperty, "completion_tokens"], out _))
        {
            return "usage.completion_tokens";
        }

        if (TryGetPropertyPath(root, [UsageProperty, "output_tokens"], out _))
        {
            return "usage.output_tokens";
        }

        return null;
    }

    private static string? DetectUsageTotalTokensPath(JsonElement root)
    {
        if (TryGetPropertyPath(root, [UsageProperty, "total_tokens"], out _))
        {
            return "usage.total_tokens";
        }

        return null;
    }

    private static bool TryGetPropertyPath(JsonElement element, string[] pathParts, out string jsonPath)
    {
        jsonPath = string.Empty;
        var current = element;

        for (int i = 0; i < pathParts.Length; i++)
        {
            var part = pathParts[i];

            // Handle array index
            if (int.TryParse(part, out var index))
            {
                if (current.ValueKind != JsonValueKind.Array || current.GetArrayLength() <= index)
                {
                    return false;
                }
                current = current[index];
            }
            // Handle property
            else
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out var next))
                {
                    return false;
                }
                current = next;
            }
        }

        // Build JSONPath notation
        var pathBuilder = new System.Text.StringBuilder();
        for (int i = 0; i < pathParts.Length; i++)
        {
            if (int.TryParse(pathParts[i], out var index))
            {
                pathBuilder.Append($"[{index}]");
            }
            else
            {
                if (i > 0)
                {
                    pathBuilder.Append('.');
                }
                pathBuilder.Append(pathParts[i]);
            }
        }

        jsonPath = pathBuilder.ToString();
        return true;
    }

    private static string? ExtractValueByPath(JsonElement root, string jsonPath)
    {
        var current = root;
        var parts = ParseJsonPath(jsonPath);

        foreach (var part in parts)
        {
            if (part.IsIndex)
            {
                if (current.ValueKind != JsonValueKind.Array || current.GetArrayLength() <= part.Index)
                {
                    return null;
                }
                current = current[part.Index];
            }
            else
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part.PropertyName, out var next))
                {
                    return null;
                }
                current = next;
            }
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : current.GetRawText();
    }

    private static List<PathPart> ParseJsonPath(string jsonPath)
    {
        var parts = new List<PathPart>();
        var segments = jsonPath.Split('.');

        foreach (var segment in segments)
        {
            // Check for array notation: property[0] or just [0]
            var bracketStart = segment.IndexOf('[');
            if (bracketStart >= 0)
            {
                var bracketEnd = segment.IndexOf(']');
                if (bracketEnd > bracketStart)
                {
                    // Add property part if exists
                    if (bracketStart > 0)
                    {
                        parts.Add(new PathPart { PropertyName = segment.Substring(0, bracketStart) });
                    }

                    // Add index part
                    var indexStr = segment.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
                    if (int.TryParse(indexStr, out var index))
                    {
                        parts.Add(new PathPart { IsIndex = true, Index = index });
                    }
                }
            }
            else
            {
                parts.Add(new PathPart { PropertyName = segment });
            }
        }

        return parts;
    }

    private sealed class PathPart
    {
        public bool IsIndex { get; set; }
        public int Index { get; set; }
        public string PropertyName { get; set; } = string.Empty;
    }
}
