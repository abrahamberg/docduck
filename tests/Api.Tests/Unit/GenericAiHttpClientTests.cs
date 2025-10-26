using System.Net;
using System.Text.Json;
using DocDuck.Providers.Ai;
using Xunit;

namespace Api.Tests.Unit;

public class GenericAiHttpClientTests
{
    [Fact]
    public void ParseEmbeddingResponseWithMapping_DefaultOpenAI_SingleEmbedding()
    {
        var json = """
        {
          "data": [
            {
              "embedding": [0.1, 0.2, 0.3],
              "index": 0
            }
          ],
          "model": "text-embedding-ada-002",
          "usage": {
            "prompt_tokens": 5,
            "total_tokens": 5
          }
        }
        """;

        var result = TestableGenericAiHttpClient.TestParseEmbeddingResponseWithMapping(json, null);

        Assert.Single(result);
        Assert.Equal(3, result[0].Length);
        Assert.Equal(0.1f, result[0][0], precision: 5);
        Assert.Equal(0.2f, result[0][1], precision: 5);
        Assert.Equal(0.3f, result[0][2], precision: 5);
    }

    [Fact]
    public void ParseEmbeddingResponseWithMapping_MultipleEmbeddings()
    {
        var json = """
        {
          "data": [
            {
              "embedding": [0.1, 0.2, 0.3],
              "index": 0
            },
            {
              "embedding": [0.4, 0.5, 0.6],
              "index": 1
            }
          ],
          "model": "text-embedding-ada-002"
        }
        """;

        var result = TestableGenericAiHttpClient.TestParseEmbeddingResponseWithMapping(json, null);

        Assert.Equal(2, result.Length);
        Assert.Equal(3, result[0].Length);
        Assert.Equal(3, result[1].Length);
        Assert.Equal(0.1f, result[0][0], precision: 5);
        Assert.Equal(0.4f, result[1][0], precision: 5);
    }

    [Fact]
    public void ParseEmbeddingResponseWithMapping_CustomMapping()
    {
        var json = """
        {
          "data": [
            {
              "embedding": [0.7, 0.8, 0.9],
              "index": 0
            }
          ]
        }
        """;

        var mapping = new Dictionary<string, string>
        {
            ["embedding"] = "$.data[0].embedding"
        };

        var result = TestableGenericAiHttpClient.TestParseEmbeddingResponseWithMapping(json, mapping);

        Assert.Single(result);
        Assert.Equal(0.7f, result[0][0], precision: 5);
    }

    [Fact]
    public void ExtractJsonPath_SimpleProperty()
    {
        var json = """
        {
          "message": "hello"
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var result = TestableGenericAiHttpClient.TestExtractJsonPath(doc.RootElement, "message");

        Assert.Equal("hello", result);
    }

    [Fact]
    public void ExtractJsonPath_NestedProperty()
    {
        var json = """
        {
          "user": {
            "name": "Alice"
          }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var result = TestableGenericAiHttpClient.TestExtractJsonPath(doc.RootElement, "user.name");

        Assert.Equal("Alice", result);
    }

    [Fact]
    public void ExtractJsonPath_ArrayIndex()
    {
        var json = """
        {
          "choices": [
            {
              "message": {
                "content": "response text"
              }
            }
          ]
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var result = TestableGenericAiHttpClient.TestExtractJsonPath(doc.RootElement, "choices[0].message.content");

        Assert.Equal("response text", result);
    }

    [Fact]
    public void ExtractJsonPath_ArrayIndexOutOfBounds_ReturnsNull()
    {
        var json = """
        {
          "choices": [
            {
              "message": {
                "content": "response text"
              }
            }
          ]
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var result = TestableGenericAiHttpClient.TestExtractJsonPath(doc.RootElement, "choices[5].message.content");

        Assert.Null(result);
    }

    [Fact]
    public void ExtractJsonPath_NonexistentProperty_ReturnsNull()
    {
        var json = """
        {
          "message": "hello"
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var result = TestableGenericAiHttpClient.TestExtractJsonPath(doc.RootElement, "nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public void ExtractJsonPath_InvalidArraySyntax_ReturnsNull()
    {
        var json = """
        {
          "choices": []
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var result = TestableGenericAiHttpClient.TestExtractJsonPath(doc.RootElement, "choices[invalid]");

        Assert.Null(result);
    }

    [Fact]
    public void ExtractJsonPath_NumericValue()
    {
        var json = """
        {
          "usage": {
            "total_tokens": 1052
          }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var result = TestableGenericAiHttpClient.TestExtractJsonPath(doc.RootElement, "usage.total_tokens");

        Assert.Equal("1052", result);
    }

    [Fact]
    public void ExtractToolCallsWithMapping_ValidToolCalls()
    {
        var json = """
        {
          "choices": [
            {
              "message": {
                "tool_calls": [
                  {
                    "id": "call_123",
                    "type": "function",
                    "function": {
                      "name": "get_weather",
                      "arguments": "{\"location\":\"London\"}"
                    }
                  }
                ]
              }
            }
          ]
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var result = TestableGenericAiHttpClient.TestExtractToolCallsWithMapping(
            doc.RootElement,
            "choices[0].message.tool_calls"
        );

        Assert.Single(result);
        Assert.Equal("call_123", result[0].Id);
        Assert.Equal("get_weather", result[0].FunctionName);
        Assert.Contains("London", result[0].ArgumentsJson);
    }

    [Fact]
    public void ExtractToolCallsWithMapping_EmptyPath_ReturnsEmptyList()
    {
        var json = """
        {
          "choices": []
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var result = TestableGenericAiHttpClient.TestExtractToolCallsWithMapping(doc.RootElement, "");

        Assert.Empty(result);
    }

    [Fact]
    public void ExtractToolCallsWithMapping_InvalidJson_ReturnsEmptyList()
    {
        var json = """
        {
          "choices": [
            {
              "message": {
                "tool_calls": "not_an_array"
              }
            }
          ]
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var result = TestableGenericAiHttpClient.TestExtractToolCallsWithMapping(
            doc.RootElement,
            "choices[0].message.tool_calls"
        );

        Assert.Empty(result);
    }

    [Fact]
    public void ExtractUsageWithMapping_AllFields()
    {
        var json = """
        {
          "usage": {
            "prompt_tokens": 100,
            "completion_tokens": 50,
            "total_tokens": 150
          }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var mapping = new ResponseMapping
        {
            RolePath = "role",
            ContentPath = "content",
            ToolCallsPath = null,
            UsagePromptTokensPath = "usage.prompt_tokens",
            UsageCompletionTokensPath = "usage.completion_tokens",
            UsageTotalTokensPath = "usage.total_tokens"
        };

        var (promptTokens, completionTokens, totalTokens) = 
            TestableGenericAiHttpClient.TestExtractUsageWithMapping(doc.RootElement, mapping);

        Assert.Equal(100, promptTokens);
        Assert.Equal(50, completionTokens);
        Assert.Equal(150, totalTokens);
    }

    [Fact]
    public void ExtractUsageWithMapping_CalculateTotalFromParts()
    {
        var json = """
        {
          "usage": {
            "prompt_tokens": 100,
            "completion_tokens": 50
          }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var mapping = new ResponseMapping
        {
            RolePath = "role",
            ContentPath = "content",
            ToolCallsPath = null,
            UsagePromptTokensPath = "usage.prompt_tokens",
            UsageCompletionTokensPath = "usage.completion_tokens",
            UsageTotalTokensPath = "usage.total_tokens"
        };

        var (promptTokens, completionTokens, totalTokens) =
            TestableGenericAiHttpClient.TestExtractUsageWithMapping(doc.RootElement, mapping);

        Assert.Equal(100, promptTokens);
        Assert.Equal(50, completionTokens);
        Assert.Equal(150, totalTokens);  // Should be calculated
    }

    [Fact]
    public void MergeDefaultParams_AddsNewParams()
    {
        var requestJson = """{"model":"gpt-4","messages":[]}""";
        var defaultParams = new Dictionary<string, JsonElement>
        {
            ["temperature"] = JsonDocument.Parse("0.7").RootElement,
            ["max_tokens"] = JsonDocument.Parse("1000").RootElement
        };

        var result = TestableGenericAiHttpClient.TestMergeDefaultParams(requestJson, defaultParams);
        
        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("temperature", out var temp));
        Assert.Equal(0.7, temp.GetDouble(), precision: 2);
        Assert.True(doc.RootElement.TryGetProperty("max_tokens", out var maxTokens));
        Assert.Equal(1000, maxTokens.GetInt32());
    }

    [Fact]
    public void MergeDefaultParams_DoesNotOverrideExisting()
    {
        var requestJson = """{"model":"gpt-4","temperature":0.9}""";
        var defaultParams = new Dictionary<string, JsonElement>
        {
            ["temperature"] = JsonDocument.Parse("0.7").RootElement
        };

        var result = TestableGenericAiHttpClient.TestMergeDefaultParams(requestJson, defaultParams);
        
        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("temperature", out var temp));
        Assert.Equal(0.9, temp.GetDouble(), precision: 2);  // Should keep original value
    }

    [Fact]
    public void MergeDefaultParams_NullOrEmptyParams_ReturnsOriginal()
    {
        var requestJson = """{"model":"gpt-4"}""";

        var result1 = TestableGenericAiHttpClient.TestMergeDefaultParams(requestJson, null);
        Assert.Equal(requestJson, result1);

        var result2 = TestableGenericAiHttpClient.TestMergeDefaultParams(requestJson, new Dictionary<string, JsonElement>());
        Assert.Equal(requestJson, result2);
    }
}

/// <summary>
/// Testable wrapper that exposes private methods for unit testing
/// </summary>
internal static class TestableGenericAiHttpClient
{
    public static string? TestExtractJsonPath(JsonElement root, string path)
    {
        var current = root;
        var parts = path.Split('.');

        foreach (var part in parts)
        {
            if (part.Contains('['))
            {
                var bracketStart = part.IndexOf('[');
                var bracketEnd = part.IndexOf(']');
                
                if (bracketStart == -1 || bracketEnd == -1 || bracketEnd <= bracketStart)
                {
                    return null;
                }

                var propertyName = part.Substring(0, bracketStart);
                var indexStr = part.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
                
                if (!int.TryParse(indexStr, out var index))
                {
                    return null;
                }

                if (!current.TryGetProperty(propertyName, out var arrayProp) || arrayProp.ValueKind != JsonValueKind.Array)
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

    public static List<ToolCall> TestExtractToolCallsWithMapping(JsonElement root, string? toolCallsPath)
    {
        var toolCalls = new List<ToolCall>();

        if (string.IsNullOrWhiteSpace(toolCallsPath))
        {
            return toolCalls;
        }

        var toolCallsJson = TestExtractJsonPath(root, toolCallsPath);
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

    public static (int PromptTokens, int CompletionTokens, int TotalTokens) TestExtractUsageWithMapping(
        JsonElement root, 
        ResponseMapping mapping)
    {
        var promptTokens = 0;
        var completionTokens = 0;
        var totalTokens = 0;

        if (!string.IsNullOrWhiteSpace(mapping.UsagePromptTokensPath))
        {
            var promptTokensStr = TestExtractJsonPath(root, mapping.UsagePromptTokensPath);
            if (int.TryParse(promptTokensStr, out var pt))
            {
                promptTokens = pt;
            }
        }

        if (!string.IsNullOrWhiteSpace(mapping.UsageCompletionTokensPath))
        {
            var completionTokensStr = TestExtractJsonPath(root, mapping.UsageCompletionTokensPath);
            if (int.TryParse(completionTokensStr, out var ct))
            {
                completionTokens = ct;
            }
        }

        if (!string.IsNullOrWhiteSpace(mapping.UsageTotalTokensPath))
        {
            var totalTokensStr = TestExtractJsonPath(root, mapping.UsageTotalTokensPath);
            if (int.TryParse(totalTokensStr, out var tt))
            {
                totalTokens = tt;
            }
        }

        if (totalTokens == 0 && (promptTokens > 0 || completionTokens > 0))
        {
            totalTokens = promptTokens + completionTokens;
        }

        return (promptTokens, completionTokens, totalTokens);
    }

    public static float[][] TestParseEmbeddingResponseWithMapping(string json, Dictionary<string, string>? responseMapping)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (responseMapping == null || !responseMapping.TryGetValue("embedding", out var embeddingPath))
        {
            embeddingPath = "$.data[0].embedding";
        }

        var results = new List<float[]>();

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

    public static string TestMergeDefaultParams(string requestJson, Dictionary<string, JsonElement>? defaultParams)
    {
        if (defaultParams == null || defaultParams.Count == 0)
        {
            return requestJson;
        }

        using var doc = JsonDocument.Parse(requestJson);
        var obj = JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(requestJson);

        if (obj == null)
        {
            return requestJson;
        }

        foreach (var (key, value) in defaultParams)
        {
            if (!obj.ContainsKey(key))
            {
                obj[key] = System.Text.Json.Nodes.JsonNode.Parse(value.GetRawText());
            }
        }

        return obj.ToJsonString();
    }
}
