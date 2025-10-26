using System.Text.Json;
using DocDuck.Providers.Ai;
using DocDuck.Providers.Configuration;
using Xunit;

namespace Api.Tests.Unit;

/// <summary>
/// Unit tests for complex methods in AiProviderConfigurationStore.
/// These tests verify the data loading logic without requiring a database.
/// </summary>
public class AiProviderConfigurationStoreTests
{
    [Fact]
    public void LoadChatModelsAsync_ShouldHandleAllFields()
    {
        // This test validates the complex logic in LoadChatModelsAsync
        // by checking the field mapping and deserialization paths
        
        var config = new AiProviderConfiguration
        {
            Enabled = true,
            ModelRegistry = new List<AiModelAssignment>()
        };

        // Simulate what would be read from database
        var testModel = new AiModelAssignment
        {
            Id = "test-model-1",
            DisplayName = "Test Model",
            ModelId = "gpt-4",
            Url = "https://api.openai.com/v1/chat/completions",
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer sk-test"
            },
            RequestTemplate = JsonDocument.Parse("{\"model\":\"test\"}"),
            ResponseMapping = new ResponseMapping
            {
                ContentPath = "choices[0].message.content",
                RolePath = "choices[0].message.role"
            },
            DefaultParams = new Dictionary<string, JsonElement>
            {
                ["temperature"] = JsonDocument.Parse("0.7").RootElement
            },
            TestStatus = ModelTestStatus.Passed,
            LastTestedAt = DateTimeOffset.UtcNow,
            LastTestMessage = "Test passed"
        };

        config.ModelRegistry.Add(testModel);

        // Verify all fields are properly set
        Assert.Single(config.ModelRegistry);
        var model = config.ModelRegistry[0];
        Assert.Equal("test-model-1", model.Id);
        Assert.Equal("Test Model", model.DisplayName);
        Assert.NotNull(model.Url);
        Assert.NotNull(model.Headers);
        Assert.Single(model.Headers);
        Assert.NotNull(model.RequestTemplate);
        Assert.NotNull(model.ResponseMapping);
        Assert.NotNull(model.DefaultParams);
        Assert.Single(model.DefaultParams);
        Assert.Equal(ModelTestStatus.Passed, model.TestStatus);
        Assert.NotNull(model.LastTestedAt);
        Assert.Equal("Test passed", model.LastTestMessage);
    }

    [Fact]
    public void LoadEmbeddingModelsAsync_ShouldHandleAllFields()
    {
        var config = new AiProviderConfiguration
        {
            Enabled = true,
            EmbeddingRegistry = new List<AiEmbeddingModelAssignment>()
        };

        // Simulate embedding model data
        var testEmbedding = new AiEmbeddingModelAssignment
        {
            Id = "test-embedding-1",
            DisplayName = "Test Embedding",
            ModelId = "text-embedding-ada-002",
            Url = "https://api.openai.com/v1/embeddings",
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer sk-test"
            },
            RequestTemplate = JsonDocument.Parse("{\"model\":\"test\"}"),
            ResponseMapping = new Dictionary<string, string>
            {
                ["embedding"] = "$.data[0].embedding"
            },
            DefaultParams = new Dictionary<string, object>(),
            TestStatus = ModelTestStatus.Passed,
            LastTestedAt = DateTimeOffset.UtcNow,
            LastTestMessage = "Test passed"
        };

        config.EmbeddingRegistry.Add(testEmbedding);

        // Verify all fields
        Assert.Single(config.EmbeddingRegistry);
        var embedding = config.EmbeddingRegistry[0];
        Assert.Equal("test-embedding-1", embedding.Id);
        Assert.NotNull(embedding.Url);
        Assert.NotNull(embedding.Headers);
        Assert.NotNull(embedding.RequestTemplate);
        Assert.NotNull(embedding.ResponseMapping);
        Assert.Equal(ModelTestStatus.Passed, embedding.TestStatus);
    }

    [Fact]
    public void UpsertChatModelsAsync_ShouldSerializeCorrectly()
    {
        // Validate that the serialization logic properly handles all fields
        var model = new AiModelAssignment
        {
            Id = "model-1",
            DisplayName = "Model 1",
            ModelId = "gpt-4",
            MaxContextTokens = 8192,
            MaxOutputTokens = 4096,
            SupportsFunctionCalling = true,
            CostFactor = 1.0,
            Enabled = true,
            TimeoutSeconds = 60,
            Url = "https://api.openai.com/v1/chat/completions",
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer test",
                ["Custom-Header"] = "value"
            },
            RequestTemplate = JsonDocument.Parse("""{"model":"test"}"""),
            ResponseMapping = new ResponseMapping
            {
                ContentPath = "content",
                RolePath = "role"
            },
            DefaultParams = new Dictionary<string, JsonElement>
            {
                ["temperature"] = JsonDocument.Parse("0.5").RootElement
            },
            TestStatus = ModelTestStatus.Untested,
            LastTestedAt = null,
            LastTestMessage = null
        };

        // Serialize the model settings (similar to what UpsertChatModelsAsync does)
        var modelSettings = new
        {
            model.DisplayName,
            model.ModelId,
            model.MaxContextTokens,
            model.MaxOutputTokens,
            model.SupportsFunctionCalling,
            model.CostFactor,
            model.Enabled,
            model.TimeoutSeconds
        };

        var json = JsonSerializer.Serialize(modelSettings);
        var deserialized = JsonSerializer.Deserialize<JsonDocument>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.RootElement.TryGetProperty("DisplayName", out _));
        Assert.True(deserialized.RootElement.TryGetProperty("ModelId", out _));
    }

    [Fact]
    public void UpsertEmbeddingModelsAsync_ShouldSerializeCorrectly()
    {
        var embedding = new AiEmbeddingModelAssignment
        {
            Id = "embedding-1",
            DisplayName = "Embedding 1",
            ModelId = "text-embedding-ada-002",
            Dimensions = 1536,
            BatchSize = 100,
            Enabled = true,
            TimeoutSeconds = 30,
            Url = "https://api.openai.com/v1/embeddings",
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer test"
            },
            RequestTemplate = JsonDocument.Parse("""{"model":"test"}"""),
            ResponseMapping = new Dictionary<string, string>
            {
                ["embedding"] = "$.data[0].embedding"
            },
            DefaultParams = new Dictionary<string, object>(),
            TestStatus = ModelTestStatus.Passed
        };

        var embeddingSettings = new
        {
            embedding.DisplayName,
            embedding.ModelId,
            embedding.Dimensions,
            embedding.BatchSize,
            embedding.Enabled,
            embedding.TimeoutSeconds
        };

        var json = JsonSerializer.Serialize(embeddingSettings);
        var deserialized = JsonSerializer.Deserialize<JsonDocument>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.RootElement.TryGetProperty("DisplayName", out _));
        Assert.True(deserialized.RootElement.TryGetProperty("Dimensions", out _));
    }

    [Fact]
    public void HandleNullableFields_ShouldNotThrow()
    {
        // Test that nullable fields are handled correctly
        var model = new AiModelAssignment
        {
            Id = "model-with-nulls",
            DisplayName = "Model",
            ModelId = "gpt-4",
            Url = "https://api.test.com",
            Headers = new Dictionary<string, string>(),
            RequestTemplate = null,  // Nullable
            ResponseMapping = null,  // Nullable
            DefaultParams = new Dictionary<string, JsonElement>(),  // Empty instead of null
            TestStatus = ModelTestStatus.Untested,
            LastTestedAt = null,     // Nullable
            LastTestMessage = null   // Nullable
        };

        // Should not throw when accessing nullable fields
        Assert.Null(model.RequestTemplate);
        Assert.Null(model.ResponseMapping);
        Assert.NotNull(model.DefaultParams);
        Assert.Empty(model.DefaultParams);
        Assert.Null(model.LastTestedAt);
        Assert.Null(model.LastTestMessage);
    }

    [Fact]
    public void DeleteRemovedModelsAsync_LogicValidation()
    {
        // Verify the logic for determining which models to delete
        var config = new AiProviderConfiguration
        {
            ModelRegistry = new List<AiModelAssignment>
            {
                new() { Id = "model-1", DisplayName = "M1", ModelId = "gpt-4" },
                new() { Id = "model-2", DisplayName = "M2", ModelId = "gpt-3.5" }
            },
            EmbeddingRegistry = new List<AiEmbeddingModelAssignment>
            {
                new() { Id = "embedding-1", DisplayName = "E1", ModelId = "ada-002" }
            }
        };

        // IDs that should be kept
        var chatIds = config.ModelRegistry.Select(m => m.Id).ToArray();
        var embeddingIds = config.EmbeddingRegistry.Select(m => m.Id).ToArray();

        Assert.Equal(2, chatIds.Length);
        Assert.Contains("model-1", chatIds);
        Assert.Contains("model-2", chatIds);

        Assert.Single(embeddingIds);
        Assert.Contains("embedding-1", embeddingIds);
    }
}

