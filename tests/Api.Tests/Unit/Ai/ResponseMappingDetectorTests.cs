using DocDuck.Providers.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.Tests.Unit.Ai;

/// <summary>
/// Unit tests for ResponseMappingDetector covering auto-detection of API response structures.
/// </summary>
public class ResponseMappingDetectorTests
{
    private readonly ResponseMappingDetector _detector;

    public ResponseMappingDetectorTests()
    {
        _detector = new ResponseMappingDetector(NullLogger<ResponseMappingDetector>.Instance);
    }

    #region DetectMapping Tests

    [Fact]
    public void DetectMapping_WithOpenAIFormat_DetectsCorrectPaths()
    {
        // Arrange
        var response = """
        {
            "choices": [{
                "message": {
                    "role": "assistant",
                    "content": "Hello, world!"
                }
            }],
            "usage": {
                "prompt_tokens": 10,
                "completion_tokens": 5,
                "total_tokens": 15
            }
        }
        """;

        // Act
        var mapping = _detector.DetectMapping(response);

        // Assert
        mapping.Should().NotBeNull();
        mapping.ContentPath.Should().Be("choices[0].message.content");
        mapping.RolePath.Should().Be("choices[0].message.role");
        mapping.AutoDetected.Should().BeTrue();
    }

    [Fact]
    public void DetectMapping_WithAnthropicFormat_DetectsCorrectPaths()
    {
        // Arrange
        var response = """
        {
            "content": [{
                "text": "Hello from Claude!"
            }],
            "role": "assistant",
            "usage": {
                "input_tokens": 10,
                "output_tokens": 5
            }
        }
        """;

        // Act
        var mapping = _detector.DetectMapping(response);

        // Assert
        mapping.Should().NotBeNull();
        mapping.ContentPath.Should().Be("content[0].text");
        mapping.AutoDetected.Should().BeTrue();
    }

    [Fact]
    public void DetectMapping_WithSimpleContentField_DetectsCorrectPath()
    {
        // Arrange
        var response = """
        {
            "content": "Simple response",
            "role": "assistant"
        }
        """;

        // Act
        var mapping = _detector.DetectMapping(response);

        // Assert
        mapping.Should().NotBeNull();
        mapping.ContentPath.Should().Be("content");
    }

    [Fact]
    public void DetectMapping_WithTextField_DetectsCorrectPath()
    {
        // Arrange
        var response = """
        {
            "text": "Text response",
            "role": "bot"
        }
        """;

        // Act
        var mapping = _detector.DetectMapping(response);

        // Assert
        mapping.Should().NotBeNull();
        mapping.ContentPath.Should().Be("text");
    }

    [Fact]
    public void DetectMapping_WithResponseField_DetectsCorrectPath()
    {
        // Arrange
        var response = """
        {
            "response": "Response text",
            "metadata": {}
        }
        """;

        // Act
        var mapping = _detector.DetectMapping(response);

        // Assert
        mapping.Should().NotBeNull();
        mapping.ContentPath.Should().Be("response");
    }

    [Fact]
    public void DetectMapping_WithToolCalls_DetectsToolCallsPath()
    {
        // Arrange
        var response = """
        {
            "choices": [{
                "message": {
                    "role": "assistant",
                    "content": null,
                    "tool_calls": [{
                        "function": {
                            "name": "get_weather",
                            "arguments": "{}"
                        }
                    }]
                }
            }]
        }
        """;

        // Act
        var mapping = _detector.DetectMapping(response);

        // Assert
        mapping.Should().NotBeNull();
        mapping.ToolCallsPath.Should().Be("choices[0].message.tool_calls");
    }

    [Fact]
    public void DetectMapping_WithUsageTokens_DetectsUsagePaths()
    {
        // Arrange
        var response = """
        {
            "choices": [{
                "message": {
                    "role": "assistant",
                    "content": "Test"
                }
            }],
            "usage": {
                "prompt_tokens": 100,
                "completion_tokens": 50,
                "total_tokens": 150
            }
        }
        """;

        // Act
        var mapping = _detector.DetectMapping(response);

        // Assert
        mapping.Should().NotBeNull();
        mapping.UsagePromptTokensPath.Should().Be("usage.prompt_tokens");
        mapping.UsageCompletionTokensPath.Should().Be("usage.completion_tokens");
        mapping.UsageTotalTokensPath.Should().Be("usage.total_tokens");
    }

    [Fact]
    public void DetectMapping_WithInvalidJson_ReturnsDefaultMapping()
    {
        // Arrange
        var response = "{ invalid json";

        // Act
        var mapping = _detector.DetectMapping(response);

        // Assert
        mapping.Should().NotBeNull();
        mapping.ContentPath.Should().Be("choices[0].message.content"); // Default OpenAI format
    }

    [Fact]
    public void DetectMapping_WithEmptyResponse_ReturnsDefaultMapping()
    {
        // Arrange
        var response = "{}";

        // Act
        var mapping = _detector.DetectMapping(response);

        // Assert
        mapping.Should().NotBeNull();
    }

    [Fact]
    public void DetectMapping_SetsDetectedAtTimestamp()
    {
        // Arrange
        var response = """{"content": "test"}""";

        // Act
        var mapping = _detector.DetectMapping(response);

        // Assert
        mapping.DetectedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    #endregion

    #region ExtractContent Tests

    [Fact]
    public void ExtractContent_WithValidMapping_ExtractsContent()
    {
        // Arrange
        var response = """
        {
            "choices": [{
                "message": {
                    "content": "Extracted content"
                }
            }]
        }
        """;
        var mapping = new ResponseMapping
        {
            ContentPath = "choices[0].message.content"
        };

        // Act
        var content = _detector.ExtractContent(response, mapping);

        // Assert
        content.Should().Be("Extracted content");
    }

    [Fact]
    public void ExtractContent_WithSimplePath_ExtractsContent()
    {
        // Arrange
        var response = """{"content": "Simple content"}""";
        var mapping = new ResponseMapping
        {
            ContentPath = "content"
        };

        // Act
        var content = _detector.ExtractContent(response, mapping);

        // Assert
        content.Should().Be("Simple content");
    }

    [Fact]
    public void ExtractContent_WithInvalidPath_ReturnsNull()
    {
        // Arrange
        var response = """{"content": "test"}""";
        var mapping = new ResponseMapping
        {
            ContentPath = "nonexistent.path"
        };

        // Act
        var content = _detector.ExtractContent(response, mapping);

        // Assert
        content.Should().BeNull();
    }

    [Fact]
    public void ExtractContent_WithInvalidJson_ReturnsNull()
    {
        // Arrange
        var response = "invalid json";
        var mapping = new ResponseMapping
        {
            ContentPath = "content"
        };

        // Act
        var content = _detector.ExtractContent(response, mapping);

        // Assert
        content.Should().BeNull();
    }

    [Fact]
    public void ExtractContent_WithNestedPath_ExtractsCorrectly()
    {
        // Arrange
        var response = """
        {
            "data": {
                "result": {
                    "text": "Nested content"
                }
            }
        }
        """;
        var mapping = new ResponseMapping
        {
            ContentPath = "data.result.text"
        };

        // Act
        var content = _detector.ExtractContent(response, mapping);

        // Assert
        content.Should().Be("Nested content");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void DetectMapping_WithNullContent_HandlesGracefully()
    {
        // Arrange
        var response = """
        {
            "choices": [{
                "message": {
                    "content": null,
                    "role": "assistant"
                }
            }]
        }
        """;

        // Act
        var mapping = _detector.DetectMapping(response);

        // Assert
        mapping.Should().NotBeNull();
        mapping.ContentPath.Should().Be("choices[0].message.content");
    }

    [Fact]
    public void DetectMapping_WithEmptyChoicesArray_HandlesGracefully()
    {
        // Arrange
        var response = """{"choices": []}""";

        // Act
        var mapping = _detector.DetectMapping(response);

        // Assert
        mapping.Should().NotBeNull();
    }

    [Fact]
    public void DetectMapping_WithComplexNestedStructure_DetectsCorrectly()
    {
        // Arrange
        var response = """
        {
            "id": "chatcmpl-123",
            "object": "chat.completion",
            "created": 1677652288,
            "model": "gpt-4",
            "choices": [{
                "index": 0,
                "message": {
                    "role": "assistant",
                    "content": "Complex response"
                },
                "finish_reason": "stop"
            }],
            "usage": {
                "prompt_tokens": 20,
                "completion_tokens": 10,
                "total_tokens": 30
            }
        }
        """;

        // Act
        var mapping = _detector.DetectMapping(response);

        // Assert
        mapping.Should().NotBeNull();
        mapping.ContentPath.Should().Be("choices[0].message.content");
        mapping.RolePath.Should().Be("choices[0].message.role");
        mapping.UsagePromptTokensPath.Should().Be("usage.prompt_tokens");
        mapping.UsageCompletionTokensPath.Should().Be("usage.completion_tokens");
        mapping.UsageTotalTokensPath.Should().Be("usage.total_tokens");
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLogger_DoesNotThrow()
    {
        // Act
        var act = () => new ResponseMappingDetector(null);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_WithLogger_CreatesInstance()
    {
        // Act
        var detector = new ResponseMappingDetector(NullLogger<ResponseMappingDetector>.Instance);

        // Assert
        detector.Should().NotBeNull();
    }

    #endregion
}
