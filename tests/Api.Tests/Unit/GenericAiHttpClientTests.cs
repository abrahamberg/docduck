using System.Text.Json;
using Xunit;

namespace Api.Tests.Unit;

public class JsonPathExtractionTests
{
    [Fact]
    public void ExtractJsonPath_ShouldExtractArrayContent()
    {
        // This is the actual response structure we're getting
        var json = """
        {
          "id": "chatcmpl-test",
          "object": "chat.completion",
          "created": 1761445126,
          "model": "gpt-5-2025-08-07",
          "choices": [
            {
              "index": 0,
              "message": {
                "role": "assistant",
                "content": "I couldn't find an onboarding guide in the provided excerpts.",
                "refusal": null
              },
              "finish_reason": "stop"
            }
          ],
          "usage": {
            "prompt_tokens": 245,
            "completion_tokens": 807,
            "total_tokens": 1052
          }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Test extracting content
        var content = ExtractJsonPath(root, "choices[0].message.content");
        Assert.NotNull(content);
        Assert.Equal("I couldn't find an onboarding guide in the provided excerpts.", content);

        // Test extracting role
        var role = ExtractJsonPath(root, "choices[0].message.role");
        Assert.NotNull(role);
        Assert.Equal("assistant", role);

        // Test extracting usage
        var totalTokens = ExtractJsonPath(root, "usage.total_tokens");
        Assert.NotNull(totalTokens);
        Assert.Equal("1052", totalTokens);
    }

    [Theory]
    [InlineData("choices[0]")]
    [InlineData("data[0]")]  // Fixed: use valid index
    [InlineData("simple.path")]
    [InlineData("nested[0].more[0].deep")]  // Fixed: use valid indices
    public void ExtractJsonPath_ShouldHandleArraySyntax(string path)
    {
        var json = """
        {
          "choices": [{"value": "test"}],
          "data": [{"x": 1}],
          "simple": {"path": "value"},
          "nested": [{"more": [{"deep": "found"}]}]
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var result = ExtractJsonPath(root, path);
        Assert.NotNull(result);
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

    [Fact]
    public void DebugArrayParsing()
    {
        var part = "choices[0]";
        var bracketStart = part.IndexOf('[');
        var bracketEnd = part.IndexOf(']');
        var propertyName = part.Substring(0, bracketStart);
        var indexStr = part.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
        
        Assert.Equal("choices", propertyName);
        Assert.Equal("0", indexStr);
        
        Assert.True(int.TryParse(indexStr, out var index));
        Assert.Equal(0, index);
    }
}
