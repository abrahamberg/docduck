using DocDuck.Providers.Ai;
using FluentAssertions;

namespace Api.Tests.Unit.Ai;

/// <summary>
/// Unit tests for AI service components covering configuration, validation, and data structures.
/// Note: Full integration tests requiring database and HTTP clients are in the Integration folder.
/// </summary>
public class ModelAgnosticAiServiceTests
{
    #region Enums and Constants

    [Fact]
    public void TaskComplexity_HasExpectedValues()
    {
        // Assert
        Enum.IsDefined(typeof(TaskComplexity), TaskComplexity.Simple).Should().BeTrue();
        Enum.IsDefined(typeof(TaskComplexity), TaskComplexity.Moderate).Should().BeTrue();
        Enum.IsDefined(typeof(TaskComplexity), TaskComplexity.Complex).Should().BeTrue();
    }

    [Fact]
    public void ModelSelectionStrategy_HasExpectedValues()
    {
        // Assert
        Enum.IsDefined(typeof(ModelSelectionStrategy), ModelSelectionStrategy.Eco).Should().BeTrue();
        Enum.IsDefined(typeof(ModelSelectionStrategy), ModelSelectionStrategy.Standard).Should().BeTrue();
        Enum.IsDefined(typeof(ModelSelectionStrategy), ModelSelectionStrategy.Turbo).Should().BeTrue();
    }

    [Fact]
    public void AiModelTier_HasExpectedValues()
    {
        // Assert
        Enum.IsDefined(typeof(AiModelTier), AiModelTier.Micro).Should().BeTrue();
        Enum.IsDefined(typeof(AiModelTier), AiModelTier.Mini).Should().BeTrue();
        Enum.IsDefined(typeof(AiModelTier), AiModelTier.Full).Should().BeTrue();
    }

    #endregion

    #region ChatCompletionOptions

    [Fact]
    public void ChatCompletionOptions_DefaultValues()
    {
        // Arrange & Act
        var options = new ChatCompletionOptions();

        // Assert
        options.Temperature.Should().BeNull();
        options.MaxTokens.Should().BeNull();
        options.Tools.Should().BeNull();
        options.ToolChoice.Should().BeNull();
    }

    [Fact]
    public void ChatCompletionOptions_WithValues()
    {
        // Arrange & Act
        var options = new ChatCompletionOptions
        {
            Temperature = 0.7,
            MaxTokens = 1000,
            Tools = [new ToolDefinition("test", "test function", "{}")],
            ToolChoice = "auto"
        };

        // Assert
        options.Temperature.Should().Be(0.7);
        options.MaxTokens.Should().Be(1000);
        options.Tools.Should().HaveCount(1);
        options.ToolChoice.Should().Be("auto");
    }

    #endregion

    #region ChatMessagePayload

    [Fact]
    public void ChatMessagePayload_CreatesCorrectly()
    {
        // Arrange & Act
        var message = new ChatMessagePayload("user", "Hello, world!");

        // Assert
        message.Role.Should().Be("user");
        message.Content.Should().Be("Hello, world!");
    }

    [Fact]
    public void ChatMessagePayload_WithSystemRole()
    {
        // Arrange & Act
        var message = new ChatMessagePayload("system", "You are a helpful assistant");

        // Assert
        message.Role.Should().Be("system");
        message.Content.Should().Be("You are a helpful assistant");
    }

    #endregion

    #region AiProviderConfiguration

    [Fact]
    public void AiProviderConfiguration_DefaultValues()
    {
        // Arrange & Act
        var config = new AiProviderConfiguration();

        // Assert
        config.Enabled.Should().BeTrue();
        config.DefaultSelectionStrategy.Should().Be(ModelSelectionStrategy.Standard);
        config.ModelRegistry.Should().NotBeNull().And.BeEmpty();
        config.EmbeddingRegistry.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void AiProviderConfiguration_WithModels()
    {
        // Arrange & Act
        var config = new AiProviderConfiguration
        {
            Enabled = true,
            ModelRegistry =
            [
                new AiModelAssignment
                {
                    Id = "micro-model",
                    DisplayName = "Micro Model",
                    ModelId = "gpt-4-mini",
                    Url = "https://api.openai.com/v1/chat/completions",
                    Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer test" },
                    Enabled = true
                }
            ],
            MicroModelId = "micro-model"
        };

        // Assert
        config.ModelRegistry.Should().HaveCount(1);
        config.MicroModel.Should().NotBeNull();
        config.MicroModel!.DisplayName.Should().Be("Micro Model");
    }

    [Fact]
    public void AiProviderConfiguration_HelperProperties_ReturnCorrectModels()
    {
        // Arrange
        var microModel = new AiModelAssignment { Id = "micro", DisplayName = "Micro", ModelId = "m1", Url = "http://test", Enabled = true };
        var miniModel = new AiModelAssignment { Id = "mini", DisplayName = "Mini", ModelId = "m2", Url = "http://test", Enabled = true };

        var config = new AiProviderConfiguration
        {
            ModelRegistry = [microModel, miniModel],
            MicroModelId = "micro",
            MiniModelId = "mini"
        };

        // Assert
        config.MicroModel.Should().Be(microModel);
        config.MiniModel.Should().Be(miniModel);
        config.FullModel.Should().BeNull();
    }

    #endregion

    #region AiModelAssignment

    [Fact]
    public void AiModelAssignment_DefaultTemperature()
    {
        // Arrange
        var model = new AiModelAssignment
        {
            Id = "test",
            DisplayName = "Test",
            ModelId = "gpt-4",
            Url = "http://test",
            Enabled = true
        };

        // Act
        var temp = model.GetDefaultTemperature();

        // Assert
        temp.Should().Be(0.7); // Default from GetDefaultTemperature()
    }

    [Fact]
    public void AiModelAssignment_WithCustomDefaultParams()
    {
        // Arrange
        var model = new AiModelAssignment
        {
            Id = "test",
            DisplayName = "Test",
            ModelId = "gpt-4",
            Url = "http://test",
            DefaultParams = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["temperature"] = System.Text.Json.JsonDocument.Parse("0.7").RootElement
            },
            Enabled = true
        };

        // Assert
        model.DefaultParams.Should().ContainKey("temperature");
    }

    #endregion

    #region AiEmbeddingModelAssignment

    [Fact]
    public void AiEmbeddingModelAssignment_DefaultDimensions()
    {
        // Arrange & Act
        var embedding = new AiEmbeddingModelAssignment
        {
            Id = "test-embedding",
            DisplayName = "Test Embedding",
            ModelId = "text-embedding-3-small",
            Url = "http://test",
            Dimensions = 1536,
            Enabled = true
        };

        // Assert
        embedding.Dimensions.Should().Be(1536);
    }

    [Fact]
    public void AiEmbeddingModelAssignment_WithHeaders()
    {
        // Arrange & Act
        var embedding = new AiEmbeddingModelAssignment
        {
            Id = "test",
            DisplayName = "Test",
            ModelId = "embed-model",
            Url = "http://test",
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer test-key"
            },
            Dimensions = 1536,
            Enabled = true
        };

        // Assert
        embedding.Headers.Should().ContainKey("Authorization");
        embedding.Headers!["Authorization"].Should().Be("Bearer test-key");
    }

    #endregion

    #region ToolDefinition

    [Fact]
    public void ToolDefinition_CreatesCorrectly()
    {
        // Arrange & Act
        var tool = new ToolDefinition("get_weather", "Get current weather", "{\"type\":\"object\"}");

        // Assert
        tool.Name.Should().Be("get_weather");
        tool.Description.Should().Be("Get current weather");
        tool.ParametersJson.Should().Be("{\"type\":\"object\"}");
    }

    #endregion

    #region Token Estimation

    [Fact]
    public void EstimateTokenCount_EmptyMessages_ReturnsZero()
    {
        // Arrange
        var messages = new List<ChatMessagePayload>();

        // Act
        var totalChars = messages.Sum(m => m.Content?.Length ?? 0);
        var tokens = totalChars / 4;

        // Assert
        tokens.Should().Be(0);
    }

    [Fact]
    public void EstimateTokenCount_WithMessages_ReturnsApproximation()
    {
        // Arrange
        var messages = new List<ChatMessagePayload>
        {
            new("user", "Hello"), // 5 chars
            new("assistant", "Hi there") // 8 chars
        };

        // Act
        var totalChars = messages.Sum(m => m.Content?.Length ?? 0);
        var tokens = totalChars / 4; // ~13 chars / 4 = ~3 tokens

        // Assert
        tokens.Should().Be(3);
    }

    #endregion
}
