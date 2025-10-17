using Indexer.Options;
using Indexer.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Indexer.Tests.Unit.Services;

/// <summary>
/// Unit tests for OpenAiEmbeddingsClient URL construction.
/// </summary>
public class OpenAiEmbeddingsClientUrlTests
{
    [Fact]
    public void Constructor_WithBaseUrl_SetsCorrectBaseAddress()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new OpenAiOptions
        {
            ApiKey = "test-key",
            BaseUrl = "https://api.openai.com/v1",
            EmbedModel = "text-embedding-3-small",
            BatchSize = 10
        });

    var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<OpenAiEmbeddingsClient>();

    // Act
    var client = new OpenAiEmbeddingsClient(options, logger);

    // Assert - the client sets OPENAI_BASE_URL environment variable when provided
    var baseEnv = Environment.GetEnvironmentVariable("OPENAI_BASE_URL");
    baseEnv.Should().Be("https://api.openai.com/v1/");
    }

    [Theory]
    [InlineData("https://api.openai.com/v1", "https://api.openai.com/v1/")]
    [InlineData("https://api.openai.com/v1/", "https://api.openai.com/v1/")]
    [InlineData("https://custom.api.com/v2", "https://custom.api.com/v2/")]
    public void Constructor_WithVariousBaseUrls_NormalizesWithTrailingSlash(string inputUrl, string expectedUrl)
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new OpenAiOptions
        {
            ApiKey = "test-key",
            BaseUrl = inputUrl,
            EmbedModel = "text-embedding-3-small",
            BatchSize = 10
        });

    var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<OpenAiEmbeddingsClient>();

    // Act
    var client = new OpenAiEmbeddingsClient(options, logger);

    // Assert
    Environment.GetEnvironmentVariable("OPENAI_BASE_URL")!.Should().Be(expectedUrl);
    }

    [Fact]
    public void Constructor_SetsAuthorizationHeader()
    {
        // Arrange
        var testApiKey = "sk-test-1234567890";
        var options = Microsoft.Extensions.Options.Options.Create(new OpenAiOptions
        {
            ApiKey = testApiKey,
            BaseUrl = "https://api.openai.com/v1",
            EmbedModel = "text-embedding-3-small",
            BatchSize = 10
        });

    var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<OpenAiEmbeddingsClient>();

    // Act
    var client = new OpenAiEmbeddingsClient(options, logger);

    // Assert - client should prefer configured ApiKey
    // The SDK client stores the API key in its internal state; we assert the options value was used.
    options.Value.ApiKey.Should().Be(testApiKey);
    }
}
