using System.Text.Json;
using DocDuck.Providers.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Api.Tests.Integration;

/// <summary>
/// Integration tests for the refactored AI configuration system.
/// Tests the full stack: seeder, store, service, and HTTP client.
/// </summary>
public class AiConfigurationIntegrationTests : IAsyncLifetime
{
    private readonly string _connectionString;
    private readonly string _apiKey;
    private AiProviderConfigurationStore? _store;
    private AiConfigurationSeeder? _seeder;

    public AiConfigurationIntegrationTests()
    {
        _connectionString = Environment.GetEnvironmentVariable("TEST_DB_CONNECTION_STRING") 
            ?? "Host=localhost;Database=docduck_test;Username=postgres;Password=postgres";
        
        _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") 
            ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is required for integration tests");
    }

    public async Task InitializeAsync()
    {
        _store = new AiProviderConfigurationStore(_connectionString);
        _seeder = new AiConfigurationSeeder(_store, NullLogger<AiConfigurationSeeder>.Instance);
        
        // Clean up any existing configuration
        await _store.DeleteAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Seeder_CreatesConfigurationWithNewFlexibleStructure()
    {
        // Arrange
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", _apiKey);
        Environment.SetEnvironmentVariable("OPENAI_MICRO_MODEL", "gpt-4o-mini");
        Environment.SetEnvironmentVariable("OPENAI_MINI_MODEL", "gpt-4o-mini");
        Environment.SetEnvironmentVariable("OPENAI_FULL_MODEL", "gpt-4o");

        // Act
        await _seeder!.SeedFromEnvironmentAsync(CancellationToken.None);

        // Assert
        var config = await _store!.GetAsync();
        Assert.NotNull(config);
        Assert.True(config.Enabled);
        Assert.Equal(3, config.ModelRegistry.Count);

        // Verify micro model has new structure
        var microModel = config.ModelRegistry.First(m => m.Id == "openai-micro");
        Assert.NotNull(microModel);
        Assert.NotNull(microModel.Url);
        Assert.Contains("/chat/completions", microModel.Url);
        Assert.NotNull(microModel.Headers);
        Assert.True(microModel.Headers.ContainsKey("Authorization"));
        Assert.NotNull(microModel.RequestTemplate);
        Assert.NotNull(microModel.ResponseMapping);
        Assert.NotNull(microModel.DefaultParams);
        
        // Verify temperature is in DefaultParams, not global config
        Assert.True(microModel.DefaultParams.ContainsKey("temperature"));
        Assert.Equal(0.0, microModel.GetDefaultTemperature());

        // Verify embedding model
        Assert.Single(config.EmbeddingRegistry);
        var embeddingModel = config.EmbeddingRegistry[0];
        Assert.Equal("openai-embedding", embeddingModel.Id);
    }

    [Fact]
    public async Task Store_PersistsAndLoadsNewColumns()
    {
        // Arrange
        var config = new AiProviderConfiguration
        {
            Enabled = true,
            DefaultSelectionStrategy = ModelSelectionStrategy.Standard,
            ModelRegistry = new List<AiModelAssignment>
            {
                new()
                {
                    Id = "test-model",
                    DisplayName = "Test Model",
                    ModelId = "test-gpt",
                    Url = "https://api.test.com/v1/chat/completions",
                    Headers = new Dictionary<string, string>
                    {
                        ["Content-Type"] = "application/json",
                        ["Authorization"] = "Bearer test-key-123"
                    },
                    // Wrap template as JSON string value
                    RequestTemplate = JsonDocument.Parse(JsonSerializer.Serialize(DefaultRequestTemplates.OpenAiChat)),
                    ResponseMapping = DefaultRequestTemplates.OpenAiResponseMapping,
                    DefaultParams = new Dictionary<string, JsonElement>
                    {
                        ["temperature"] = JsonDocument.Parse("0.5").RootElement.Clone(),
                        ["top_p"] = JsonDocument.Parse("0.9").RootElement.Clone()
                    },
                    MaxContextTokens = 8000,
                    MaxOutputTokens = 4000,
                    SupportsFunctionCalling = true,
                    CostFactor = 1.5,
                    Enabled = true
                }
            },
            MicroModelId = "test-model",
            MiniModelId = "test-model",
            FullModelId = "test-model",
            EmbeddingRegistry = new List<AiEmbeddingModelAssignment>(),
            ActiveEmbeddingModelId = null
        };

        // Act
        await _store!.UpsertAsync(config, CancellationToken.None);
        var loaded = await _store.GetAsync();

        // Assert
        Assert.NotNull(loaded);
        Assert.Single(loaded.ModelRegistry);
        
        var model = loaded.ModelRegistry[0];
        Assert.Equal("test-model", model.Id);
        Assert.Equal("https://api.test.com/v1/chat/completions", model.Url);
        Assert.NotNull(model.Headers);
        Assert.Equal(2, model.Headers.Count);
        Assert.Equal("Bearer test-key-123", model.Headers["Authorization"]);
        Assert.NotNull(model.RequestTemplate);
        Assert.NotNull(model.ResponseMapping);
        Assert.Equal("choices[0].message.content", model.ResponseMapping.ContentPath);
        Assert.NotNull(model.DefaultParams);
        Assert.Equal(2, model.DefaultParams.Count);
        Assert.Equal(0.5, model.GetDefaultTemperature());
    }

    [Fact]
    public void TemplateSubstitution_WorksWithRealTemplate()
    {
        // Arrange
        var messages = new List<ChatMessagePayload>
        {
            new("system", "You are a helpful assistant."),
            new("user", "What is 2+2?")
        };

        var context = new TemplateContext(
            ModelId: "gpt-4o-mini",
            Messages: messages,
            Temperature: 0.0,
            MaxTokens: 100
        );

        // Act
        var result = TemplateSubstitutionService.Substitute(
            DefaultRequestTemplates.OpenAiChat,
            context
        );

        // Assert
        var json = JsonDocument.Parse(result);
        Assert.Equal("gpt-4o-mini", json.RootElement.GetProperty("model").GetString());
        Assert.Equal(0.0, json.RootElement.GetProperty("temperature").GetDouble());
        Assert.Equal(100, json.RootElement.GetProperty("max_tokens").GetInt32());
        
        var messagesArray = json.RootElement.GetProperty("messages");
        Assert.Equal(2, messagesArray.GetArrayLength());
        Assert.Equal("system", messagesArray[0].GetProperty("role").GetString());
        Assert.Equal("user", messagesArray[1].GetProperty("role").GetString());
    }

    [Fact]
    public void ResponseMappingDetector_DetectsOpenAiFormat()
    {
        // Arrange
        var sampleResponse = @"{
            ""id"": ""chatcmpl-123"",
            ""object"": ""chat.completion"",
            ""created"": 1677652288,
            ""model"": ""gpt-4o-mini"",
            ""choices"": [{
                ""index"": 0,
                ""message"": {
                    ""role"": ""assistant"",
                    ""content"": ""Hello! How can I help you today?""
                },
                ""finish_reason"": ""stop""
            }],
            ""usage"": {
                ""prompt_tokens"": 9,
                ""completion_tokens"": 12,
                ""total_tokens"": 21
            }
        }";

        var detector = new ResponseMappingDetector();

        // Act
        var mapping = detector.DetectMapping(sampleResponse);

        // Assert
        Assert.NotNull(mapping);
        Assert.Equal("choices[0].message.content", mapping.ContentPath);
        Assert.Equal("choices[0].message.role", mapping.RolePath);
        Assert.Equal("usage.prompt_tokens", mapping.UsagePromptTokensPath);
        Assert.Equal("usage.completion_tokens", mapping.UsageCompletionTokensPath);
        Assert.Equal("usage.total_tokens", mapping.UsageTotalTokensPath);
    }

    [Fact]
    public void CurlImportService_ParsesValidCurl()
    {
        // Arrange
        var curlCommand = @"curl https://api.openai.com/v1/chat/completions \
            -H ""Content-Type: application/json"" \
            -H ""Authorization: Bearer sk-test123"" \
            -d '{""model"":""gpt-4"",""messages"":[{""role"":""user"",""content"":""Hello""}]}'";

        // Act
        var model = CurlImportService.ParseCurl(curlCommand, "imported-gpt4", "Imported GPT-4");

        // Assert
        Assert.Equal("imported-gpt4", model.Id);
        Assert.Equal("Imported GPT-4", model.DisplayName);
        Assert.Equal("https://api.openai.com/v1/chat/completions", model.Url);
        Assert.NotNull(model.Headers);
        Assert.True(model.Headers.ContainsKey("Authorization"));
        Assert.Equal("Bearer sk-test123", model.Headers["Authorization"]);
        Assert.NotNull(model.RequestTemplate);
    }

    [Fact]
    public void SystemPrompts_AreAccessible()
    {
        // Assert
        Assert.NotNull(SystemPrompts.Refine);
        Assert.NotNull(SystemPrompts.Chat);
        Assert.NotNull(SystemPrompts.Evaluation);
        Assert.Contains("semantic search", SystemPrompts.Refine);
    }

    [Fact(Skip = "Requires live OpenAI API - enable manually by removing Skip attribute")]
    public async Task EndToEnd_ChatCompletion_WithRealOpenAI()
    {
        // Arrange
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", _apiKey);
        Environment.SetEnvironmentVariable("OPENAI_MICRO_MODEL", "gpt-4o-mini");
        await _seeder!.SeedFromEnvironmentAsync(CancellationToken.None);
        
        var config = await _store!.GetAsync();
        var microModel = config!.ModelRegistry.First(m => m.Id == "openai-micro");
        
        using var client = new GenericAiHttpClient(microModel, null);

        // Act
        var result = await client.CompleteChatAsync(
            new List<ChatMessagePayload>
            {
                new("user", "Say 'test successful' if you can read this.")
            },
            temperature: 0.0,
            maxTokens: 20
        );

        // Assert
        Assert.NotNull(result);
        Assert.Equal("assistant", result.Role);
        Assert.NotEmpty(result.Content);
        Assert.Contains("test", result.Content.ToLower());
        Assert.True(result.TotalTokens > 0);
    }

    [Fact(Skip = "Requires live OpenAI API - enable manually by removing Skip attribute")]
    public async Task EndToEnd_EmbeddingGeneration_WithRealOpenAI()
    {
        // Arrange
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", _apiKey);
        await _seeder!.SeedFromEnvironmentAsync(CancellationToken.None);
        
        var config = await _store!.GetAsync();
        var microModel = config!.ModelRegistry.First(m => m.Id == "openai-micro");
        var embeddingModel = config.EmbeddingRegistry[0];
        
        using var client = new GenericAiHttpClient(microModel, null);

        // Act
        var embedding = await client.EmbedAsync(embeddingModel, "test embedding text");

        // Assert
        Assert.NotNull(embedding);
        Assert.Equal(embeddingModel.Dimensions, embedding.Length);
        Assert.All(embedding, value => Assert.True(value >= -1.0 && value <= 1.0));
    }
}
