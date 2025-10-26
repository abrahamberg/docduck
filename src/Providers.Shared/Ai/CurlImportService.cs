using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DocDuck.Providers.Ai;

/// <summary>
/// Parses cURL commands and converts them to FlexibleAiModel configuration.
/// </summary>
public static class CurlImportService
{
    /// <summary>
    /// Parse a cURL command and extract model configuration.
    /// </summary>
    public static FlexibleAiModel ParseCurl(string curlCommand, string modelId, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(curlCommand);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var url = ExtractUrl(curlCommand);
        var headers = ExtractHeaders(curlCommand);
        var body = ExtractBody(curlCommand);

        // Parse body to extract model configuration hints
        var requestTemplate = CreateRequestTemplate(body);
        var defaultParams = ExtractDefaultParams(body);

        // Wrap template as a JSON string value
        var templateDoc = JsonDocument.Parse(JsonSerializer.Serialize(requestTemplate));

        return new FlexibleAiModel
        {
            Id = modelId,
            DisplayName = displayName,
            ModelId = ExtractModelIdFromBody(body) ?? modelId,
            Url = url,
            Headers = headers,
            RequestTemplate = templateDoc,
            DefaultParams = defaultParams,
            ResponseMapping = DefaultRequestTemplates.OpenAiResponseMapping,
            TestStatus = ModelTestStatus.Untested
        };
    }

    private static string ExtractUrl(string curlCommand)
    {
        // Match: curl "https://..." or curl https://... or -url https://...
        var urlPattern = @"(?:curl\s+['""]?(https?://[^\s'""]+)|--url\s+['""]?(https?://[^\s'""]+))";
        var match = Regex.Match(curlCommand, urlPattern);

        if (match.Success)
        {
            return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        }

        throw new InvalidOperationException("Could not extract URL from cURL command");
    }

    private static Dictionary<string, string> ExtractHeaders(string curlCommand)
    {
        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "application/json"
        };

        // Match: -H "Header: Value" or --header "Header: Value"
        var headerPattern = @"(?:-H|--header)\s+['""]([^:]+):\s*([^'""]+)['""]";
        var matches = Regex.Matches(curlCommand, headerPattern);

        foreach (var (key, value) in matches.Select(m => (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim())))
        {
            headers[key] = value;
        }

        return headers;
    }

    private static string ExtractBody(string curlCommand)
    {
        // Match: -d '{"key": "value"}' or --data '...' or --data-raw '...'
        var dataPattern = @"(?:-d|--data|--data-raw)\s+['""](.+?)['""](?:\s+|$)";
        var match = Regex.Match(curlCommand, dataPattern, RegexOptions.Singleline);

        if (match.Success)
        {
            var body = match.Groups[1].Value;
            // Unescape common shell escapes
            body = body.Replace("\\'", "'").Replace("\\\"", "\"");
            return body;
        }

        // No body found, return empty object
        return "{}";
    }

    private static string CreateRequestTemplate(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // Check if this is already a template (has placeholder variables)
            var rawBody = root.GetRawText();
            if (rawBody.Contains("{MODEL") || rawBody.Contains("{MESSAGES"))
            {
                return rawBody;
            }

            // Convert concrete values to template variables
            var template = new StringBuilder(rawBody);

            // Replace model value with template variable
            if (root.TryGetProperty("model", out var modelProp))
            {
                var modelValue = modelProp.GetString() ?? "";
                if (!string.IsNullOrWhiteSpace(modelValue))
                {
                    template.Replace($"\"{modelValue}\"", "\"{MODEL_ID}\"");
                }
            }

            // Replace messages array with template variable
            if (root.TryGetProperty("messages", out _))
            {
                // Find and replace the entire messages array
                var messagesPattern = @"""messages"":\s*\[[^\]]*\]";
                var templateStr = template.ToString();
                templateStr = Regex.Replace(templateStr, messagesPattern, "\"messages\": {MESSAGES}");
                template = new StringBuilder(templateStr);
            }

            // Replace temperature with template variable
            if (root.TryGetProperty("temperature", out _))
            {
                var tempPattern = @"""temperature"":\s*[\d.]+";
                var templateStr = template.ToString();
                templateStr = Regex.Replace(templateStr, tempPattern, "\"temperature\": {TEMPERATURE}");
                template = new StringBuilder(templateStr);
            }

            // Replace max_tokens with template variable
            if (root.TryGetProperty("max_tokens", out _))
            {
                var maxTokensPattern = @"""max_tokens"":\s*-?\d+";
                var templateStr = template.ToString();
                templateStr = Regex.Replace(templateStr, maxTokensPattern, "\"max_tokens\": {MAX_TOKENS}");
                template = new StringBuilder(templateStr);
            }

            return template.ToString();
        }
        catch
        {
            // If parsing fails, assume it's already in template format
            return body;
        }
    }

    private static Dictionary<string, JsonElement> ExtractDefaultParams(string body)
    {
        var defaultParams = new Dictionary<string, JsonElement>();

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // Extract temperature if present
            if (root.TryGetProperty("temperature", out var temp))
            {
                defaultParams["temperature"] = temp.Clone();
            }

            // Extract top_p if present
            if (root.TryGetProperty("top_p", out var topP))
            {
                defaultParams["top_p"] = topP.Clone();
            }

            // Extract frequency_penalty if present
            if (root.TryGetProperty("frequency_penalty", out var freqPenalty))
            {
                defaultParams["frequency_penalty"] = freqPenalty.Clone();
            }

            // Extract presence_penalty if present
            if (root.TryGetProperty("presence_penalty", out var presPenalty))
            {
                defaultParams["presence_penalty"] = presPenalty.Clone();
            }

            // Extract max_tokens if present
            if (root.TryGetProperty("max_tokens", out var maxTokens))
            {
                defaultParams["max_tokens"] = maxTokens.Clone();
            }
        }
        catch
        {
            // Ignore parsing errors, return empty defaults
        }

        return defaultParams;
    }

    private static string? ExtractModelIdFromBody(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("model", out var modelProp))
            {
                return modelProp.GetString();
            }
        }
        catch
        {
            // Ignore
        }

        return null;
    }
}
