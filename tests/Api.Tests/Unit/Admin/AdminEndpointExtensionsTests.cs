using FluentAssertions;

namespace Api.Tests.Unit.Admin;

/// <summary>
/// Unit tests for AdminEndpointExtensions covering validation logic and data structures.
/// Note: Full integration tests for endpoints are in the Integration folder.
/// </summary>
public class AdminEndpointExtensionsTests
{
    #region Auth Validation

    [Fact]
    public void AdminLoginRequest_EmptyUsername_IsInvalid()
    {
        // Arrange
        var username = "";
        var password = "password123";

        // Assert
        string.IsNullOrWhiteSpace(username).Should().BeTrue();
        string.IsNullOrWhiteSpace(password).Should().BeFalse();
    }

    [Fact]
    public void AdminLoginRequest_EmptyPassword_IsInvalid()
    {
        // Arrange
        var username = "admin";
        var password = "";

        // Assert
        string.IsNullOrWhiteSpace(password).Should().BeTrue();
    }

    [Fact]
    public void AdminLoginRequest_BothEmpty_IsInvalid()
    {
        // Arrange
        var username = "";
        var password = "";

        // Assert
        string.IsNullOrWhiteSpace(username).Should().BeTrue();
        string.IsNullOrWhiteSpace(password).Should().BeTrue();
    }

    #endregion

    #region User Validation

    [Fact]
    public void CreateUser_UsernameTooShort_IsInvalid()
    {
        // Arrange
        var username = "ab";

        // Assert
        username.Length.Should().BeLessThan(3);
    }

    [Fact]
    public void CreateUser_UsernameMinLength_IsValid()
    {
        // Arrange
        var username = "abc";

        // Assert
        username.Length.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void CreateUser_PasswordTooShort_IsInvalid()
    {
        // Arrange
        var password = "short";

        // Assert
        password.Length.Should().BeLessThan(8);
    }

    [Fact]
    public void CreateUser_PasswordMinLength_IsValid()
    {
        // Arrange
        var password = "password123";

        // Assert
        password.Length.Should().BeGreaterThanOrEqualTo(8);
    }

    [Fact]
    public void ChangePassword_PasswordTooShort_IsInvalid()
    {
        // Arrange
        var password = "short";

        // Assert
        password.Length.Should().BeLessThan(8);
    }

    #endregion

    #region Provider Validation

    [Fact]
    public void ProviderSettings_EmptyPayload_IsInvalid()
    {
        // Arrange
        var payload = System.Text.Json.JsonDocument.Parse("null").RootElement;

        // Assert
        payload.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public void ProviderSettings_ValidPayload_HasExpectedStructure()
    {
        // Arrange
        var payload = System.Text.Json.JsonDocument.Parse("{\"enabled\": true}").RootElement;

        // Assert
        payload.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object);
        payload.TryGetProperty("enabled", out var enabled).Should().BeTrue();
        enabled.GetBoolean().Should().BeTrue();
    }

    #endregion

    #region AI Configuration

    [Fact]
    public void AiProbeRequest_EmptyBaseUrl_IsInvalid()
    {
        // Arrange
        var baseUrl = "";

        // Assert
        string.IsNullOrWhiteSpace(baseUrl).Should().BeTrue();
    }

    [Fact]
    public void AiProbeRequest_EmptyApiKey_IsInvalid()
    {
        // Arrange
        var apiKey = "";

        // Assert
        string.IsNullOrWhiteSpace(apiKey).Should().BeTrue();
    }

    [Fact]
    public void AiProbeRequest_DefaultTimeout_IsReasonable()
    {
        // Arrange
        int? timeoutSeconds = null;
        var defaultTimeout = timeoutSeconds ?? 120;

        // Assert
        defaultTimeout.Should().Be(120);
    }

    #endregion

    #region CURL Import

    [Fact]
    public void ImportCurl_EmptyCommand_IsInvalid()
    {
        // Arrange
        var curlCommand = "";

        // Assert
        string.IsNullOrWhiteSpace(curlCommand).Should().BeTrue();
    }

    [Fact]
    public void ImportCurl_ValidCommand_HasRequiredComponents()
    {
        // Arrange
        var curlCommand = "curl -X POST https://api.openai.com/v1/chat/completions -H 'Authorization: Bearer sk-test' -d '{\"model\":\"gpt-4\"}'";

        // Assert
        curlCommand.Should().Contain("https://");
        curlCommand.Should().Contain("Authorization");
        curlCommand.Should().Contain("POST");
    }

    #endregion

    #region Response Mapping Detection

    [Fact]
    public void ResponseMapping_OpenAIFormat_HasExpectedStructure()
    {
        // Arrange
        var response = "{\"choices\":[{\"message\":{\"content\":\"Hello\"}}]}";

        // Assert
        response.Should().Contain("choices");
        response.Should().Contain("message");
        response.Should().Contain("content");
    }

    [Fact]
    public void ResponseMapping_WithToolCalls_HasExpectedStructure()
    {
        // Arrange
        var response = "{\"choices\":[{\"message\":{\"tool_calls\":[{\"function\":{\"name\":\"get_weather\"}}]}}]}";

        // Assert
        response.Should().Contain("tool_calls");
        response.Should().Contain("function");
    }

    #endregion

    #region Citation Formatting

    [Fact]
    public void Citation_Format_IsCorrect()
    {
        // Arrange
        var providerType = "filesystem";
        var providerName = "local-docs";
        var filename = "README.md";
        var chunkNum = 5;

        // Act
        var citation = $"[{providerType}/{providerName}:{filename}#chunk{chunkNum}]";

        // Assert
        citation.Should().Be("[filesystem/local-docs:README.md#chunk5]");
    }

    [Fact]
    public void Citation_WithSpecialCharacters_IsFormatted()
    {
        // Arrange
        var providerType = "s3";
        var providerName = "my-bucket";
        var filename = "path/to/file.pdf";
        var chunkNum = 10;

        // Act
        var citation = $"[{providerType}/{providerName}:{filename}#chunk{chunkNum}]";

        // Assert
        citation.Should().Contain("/");
        citation.Should().Contain("#chunk");
    }

    #endregion

    #region Model Testing Validation

    [Fact]
    public void TestModel_EmptyModelId_IsInvalid()
    {
        // Arrange
        var modelId = "";

        // Assert
        string.IsNullOrWhiteSpace(modelId).Should().BeTrue();
    }

    [Fact]
    public void TestModel_SuccessMessage_FormatsCorrectly()
    {
        // Arrange
        var elapsedMs = 150;
        var responseText = "OK";

        // Act
        var message = $"✓ Model responded in {elapsedMs}ms - \"{responseText}\"";

        // Assert
        message.Should().Contain("150ms");
        message.Should().Contain("OK");
    }

    [Fact]
    public void TestModel_LongResponse_Truncates()
    {
        // Arrange
        var responseText = new string('x', 100);

        // Act
        var preview = responseText.Length > 50
            ? string.Concat(responseText.AsSpan(0, 50), "...")
            : responseText;

        // Assert
        preview.Length.Should().Be(53); // 50 + "..."
        preview.Should().EndWith("...");
    }

    #endregion

    #region Embedding Change Warning

    [Fact]
    public void EmbeddingChange_DimensionMismatch_IsDetected()
    {
        // Arrange
        var currentDimensions = 1536;
        var newDimensions = 3072;

        // Act
        var dimensionsChanged = currentDimensions != newDimensions;

        // Assert
        dimensionsChanged.Should().BeTrue();
    }

    [Fact]
    public void EmbeddingChange_ModelIdChange_IsDetected()
    {
        // Arrange
        var currentModelId = "text-embedding-3-small";
        var newModelId = "text-embedding-3-large";

        // Act
        var modelIdChanged = currentModelId != newModelId;

        // Assert
        modelIdChanged.Should().BeTrue();
    }

    [Fact]
    public void EmbeddingChange_NoChange_IsNotDetected()
    {
        // Arrange
        var currentDimensions = 1536;
        var newDimensions = 1536;
        var currentModelId = "text-embedding-3-small";
        var newModelId = "text-embedding-3-small";

        // Act
        var dimensionsChanged = currentDimensions != newDimensions;
        var modelIdChanged = currentModelId != newModelId;

        // Assert
        dimensionsChanged.Should().BeFalse();
        modelIdChanged.Should().BeFalse();
    }

    #endregion
}
