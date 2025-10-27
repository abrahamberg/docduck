using DocDuck.Providers.Ai;
using FluentAssertions;

namespace Api.Tests.Unit.Ai;

/// <summary>
/// Unit tests for CurlImportService covering CURL command parsing and model configuration extraction.
/// </summary>
public class CurlImportServiceTests
{
    #region Validation

    [Fact]
    public void ParseCurl_WithNullCommand_ThrowsArgumentException()
    {
        // Act
        var act = () => CurlImportService.ParseCurl(null!, "model-id", "Display Name");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ParseCurl_WithEmptyCommand_ThrowsArgumentException()
    {
        // Act
        var act = () => CurlImportService.ParseCurl("", "model-id", "Display Name");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ParseCurl_WithNullModelId_ThrowsArgumentException()
    {
        // Arrange
        var curl = "curl -X POST https://api.openai.com/v1/chat/completions";

        // Act
        var act = () => CurlImportService.ParseCurl(curl, null!, "Display Name");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ParseCurl_WithNullDisplayName_ThrowsArgumentException()
    {
        // Arrange
        var curl = "curl -X POST https://api.openai.com/v1/chat/completions";

        // Act
        var act = () => CurlImportService.ParseCurl(curl, "model-id", null!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region URL Extraction

    [Fact]
    public void ParseCurl_ExtractsHttpsUrl()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.Url.Should().Be("https://api.openai.com/v1/chat/completions");
    }

    [Fact]
    public void ParseCurl_ExtractsQuotedUrl()
    {
        // Arrange
        var curl = "curl \"https://api.openai.com/v1/chat/completions\"";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.Url.Should().Be("https://api.openai.com/v1/chat/completions");
    }

    [Fact]
    public void ParseCurl_ExtractsUrlWithUrlFlag()
    {
        // Arrange
        var curl = "curl --url https://api.openai.com/v1/chat/completions";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.Url.Should().Be("https://api.openai.com/v1/chat/completions");
    }

    [Fact]
    public void ParseCurl_WithoutUrl_ThrowsInvalidOperationException()
    {
        // Arrange
        var curl = "curl -H 'Authorization: Bearer token'";

        // Act
        var act = () => CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Could not extract URL*");
    }

    #endregion

    #region Header Extraction

    [Fact]
    public void ParseCurl_ExtractsAuthorizationHeader()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions -H \"Authorization: Bearer sk-test123\"";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.Headers.Should().ContainKey("Authorization");
        result.Headers!["Authorization"].Should().Be("Bearer sk-test123");
    }

    [Fact]
    public void ParseCurl_ExtractsMultipleHeaders()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions " +
                   "-H \"Authorization: Bearer sk-test\" " +
                   "-H \"X-Custom-Header: custom-value\"";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.Headers.Should().ContainKey("Authorization");
        result.Headers.Should().ContainKey("X-Custom-Header");
        result.Headers!["X-Custom-Header"].Should().Be("custom-value");
    }

    [Fact]
    public void ParseCurl_ExtractsHeadersWithLongFlag()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions --header \"Authorization: Bearer sk-test\"";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.Headers.Should().ContainKey("Authorization");
    }

    [Fact]
    public void ParseCurl_DefaultContentTypeHeader()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.Headers.Should().ContainKey("Content-Type");
        result.Headers!["Content-Type"].Should().Be("application/json");
    }

    #endregion

    #region Body Extraction

    [Fact]
    public void ParseCurl_ExtractsBodyWithDataFlag()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions -d '{\"model\":\"gpt-4\"}'";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.RequestTemplate.Should().NotBeNull();
    }

    [Fact]
    public void ParseCurl_ExtractsBodyWithDataRawFlag()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions --data-raw '{\"model\":\"gpt-4\"}'";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.RequestTemplate.Should().NotBeNull();
    }

    [Fact]
    public void ParseCurl_WithoutBody_UsesEmptyObject()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.RequestTemplate.Should().NotBeNull();
    }

    #endregion

    #region Model ID Extraction

    [Fact]
    public void ParseCurl_ExtractsModelIdFromBody()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions -d '{\"model\":\"gpt-4-turbo\"}'";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.ModelId.Should().Be("gpt-4-turbo");
    }

    [Fact]
    public void ParseCurl_WithoutModelInBody_UsesProvidedModelId()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions -d '{\"messages\":[]}'";

        // Act
        var result = CurlImportService.ParseCurl(curl, "fallback-model", "Test Model");

        // Assert
        result.ModelId.Should().Be("fallback-model");
    }

    #endregion

    #region Template Creation

    [Fact]
    public void ParseCurl_ConvertsModelToTemplate()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions -d '{\"model\":\"gpt-4\"}'";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.RequestTemplate.RootElement.GetString().Should().Contain("{MODEL_ID}");
    }

    [Fact]
    public void ParseCurl_ConvertsMessagesToTemplate()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions " +
                   "-d '{\"model\":\"gpt-4\",\"messages\":[{\"role\":\"user\",\"content\":\"test\"}]}'";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.RequestTemplate.RootElement.GetString().Should().Contain("{MESSAGES}");
    }

    [Fact]
    public void ParseCurl_ConvertsTemperatureToTemplate()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions " +
                   "-d '{\"model\":\"gpt-4\",\"temperature\":0.7}'";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.RequestTemplate.RootElement.GetString().Should().Contain("{TEMPERATURE}");
    }

    [Fact]
    public void ParseCurl_ConvertsMaxTokensToTemplate()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions " +
                   "-d '{\"model\":\"gpt-4\",\"max_tokens\":1000}'";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.RequestTemplate.RootElement.GetString().Should().Contain("{MAX_TOKENS}");
    }

    [Fact]
    public void ParseCurl_PreservesExistingTemplateVariables()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions " +
                   "-d '{\"model\":\"{MODEL_ID}\",\"messages\":{MESSAGES}}'";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        var template = result.RequestTemplate.RootElement.GetString();
        template.Should().Contain("{MODEL_ID}");
        template.Should().Contain("{MESSAGES}");
    }

    #endregion

    #region Default Parameters Extraction

    [Fact]
    public void ParseCurl_ExtractsTemperatureParameter()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions " +
                   "-d '{\"model\":\"gpt-4\",\"temperature\":0.5}'";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.DefaultParams.Should().ContainKey("temperature");
        result.DefaultParams!["temperature"].GetDouble().Should().Be(0.5);
    }

    [Fact]
    public void ParseCurl_ExtractsTopPParameter()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions " +
                   "-d '{\"model\":\"gpt-4\",\"top_p\":0.9}'";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.DefaultParams.Should().ContainKey("top_p");
        result.DefaultParams!["top_p"].GetDouble().Should().Be(0.9);
    }

    [Fact]
    public void ParseCurl_ExtractsFrequencyPenalty()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions " +
                   "-d '{\"model\":\"gpt-4\",\"frequency_penalty\":0.5}'";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.DefaultParams.Should().ContainKey("frequency_penalty");
    }

    [Fact]
    public void ParseCurl_ExtractsPresencePenalty()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions " +
                   "-d '{\"model\":\"gpt-4\",\"presence_penalty\":0.5}'";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.DefaultParams.Should().ContainKey("presence_penalty");
    }

    [Fact]
    public void ParseCurl_ExtractsMaxTokensParameter()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions " +
                   "-d '{\"model\":\"gpt-4\",\"max_tokens\":2000}'";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.DefaultParams.Should().ContainKey("max_tokens");
        result.DefaultParams!["max_tokens"].GetInt32().Should().Be(2000);
    }

    [Fact]
    public void ParseCurl_WithMultipleParameters_ExtractsAll()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions " +
                   "-d '{\"model\":\"gpt-4\",\"temperature\":0.7,\"max_tokens\":1000,\"top_p\":0.9}'";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.DefaultParams.Should().HaveCount(3);
        result.DefaultParams.Should().ContainKey("temperature");
        result.DefaultParams.Should().ContainKey("max_tokens");
        result.DefaultParams.Should().ContainKey("top_p");
    }

    [Fact]
    public void ParseCurl_WithInvalidJson_HandlesGracefully()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions -d 'invalid json'";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.Should().NotBeNull();
        result.DefaultParams.Should().BeEmpty();
    }

    #endregion

    #region Model Configuration

    [Fact]
    public void ParseCurl_SetsModelIdAndDisplayName()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions";

        // Act
        var result = CurlImportService.ParseCurl(curl, "custom-id", "Custom Display Name");

        // Assert
        result.Id.Should().Be("custom-id");
        result.DisplayName.Should().Be("Custom Display Name");
    }

    [Fact]
    public void ParseCurl_SetsDefaultResponseMapping()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.ResponseMapping.Should().NotBeNull();
        result.ResponseMapping.ContentPath.Should().Be("choices[0].message.content");
    }

    [Fact]
    public void ParseCurl_SetsTestStatusToUntested()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.TestStatus.Should().Be(ModelTestStatus.Untested);
    }

    #endregion

    #region Complex Real-World Examples

    [Fact]
    public void ParseCurl_CompleteOpenAIExample_ParsesCorrectly()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions " +
                   "-H \"Content-Type: application/json\" " +
                   "-H \"Authorization: Bearer sk-proj-test123\" " +
                   "-d '{" +
                   "\"model\":\"gpt-4-turbo-preview\"," +
                   "\"messages\":[{\"role\":\"user\",\"content\":\"Hello!\"}]," +
                   "\"temperature\":0.7," +
                   "\"max_tokens\":1000" +
                   "}'";

        // Act
        var result = CurlImportService.ParseCurl(curl, "imported-gpt4", "GPT-4 Turbo");

        // Assert
        result.Id.Should().Be("imported-gpt4");
        result.DisplayName.Should().Be("GPT-4 Turbo");
        result.ModelId.Should().Be("gpt-4-turbo-preview");
        result.Url.Should().Be("https://api.openai.com/v1/chat/completions");
        result.Headers!["Authorization"].Should().Be("Bearer sk-proj-test123");
        result.DefaultParams.Should().ContainKey("temperature");
        result.DefaultParams.Should().ContainKey("max_tokens");
    }

    [Fact]
    public void ParseCurl_WithEscapedQuotes_ParsesCorrectly()
    {
        // Arrange
        var curl = "curl https://api.openai.com/v1/chat/completions " +
                   "-d '{\\\"model\\\":\\\"gpt-4\\\"}'";

        // Act
        var result = CurlImportService.ParseCurl(curl, "test-model", "Test Model");

        // Assert
        result.Should().NotBeNull();
        result.ModelId.Should().Be("gpt-4");
    }

    #endregion
}
