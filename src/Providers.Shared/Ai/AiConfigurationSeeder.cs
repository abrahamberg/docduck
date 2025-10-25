using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DocDuck.Providers.Ai;

/// <summary>
/// Seeds initial AI provider configuration from environment variables.
/// </summary>
public sealed class AiConfigurationSeeder
{
    private readonly AiProviderConfigurationStore _store;
    private readonly ILogger<AiConfigurationSeeder> _logger;

    public AiConfigurationSeeder(
        AiProviderConfigurationStore store,
        ILogger<AiConfigurationSeeder> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Seed AI configuration from environment if not already present.
    /// </summary>
    public async Task SeedFromEnvironmentAsync(CancellationToken ct = default)
    {
        var existing = await _store.GetAsync(ct);
        if (existing != null)
        {
            _logger.LogInformation("AI provider configuration already exists, skipping seeding");
            return;
        }

        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var baseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "https://api.openai.com/v1";
        
        // Model configuration from environment or OpenAI defaults
        var microModel = Environment.GetEnvironmentVariable("OPENAI_MICRO_MODEL") ?? "gpt-5-nano";
        var miniModel = Environment.GetEnvironmentVariable("OPENAI_MINI_MODEL") ?? "gpt-5-mini";
        var fullModel = Environment.GetEnvironmentVariable("OPENAI_FULL_MODEL") ?? "gpt-5";
        
        var embeddingModel = Environment.GetEnvironmentVariable("OPENAI_EMBEDDING_MODEL") ?? "text-embedding-3-small";
        var embeddingDimensions = int.TryParse(Environment.GetEnvironmentVariable("OPENAI_EMBEDDING_DIMENSIONS"), out var dims) ? dims : 1536;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("OPENAI_API_KEY not set. AI configuration will be disabled until configured via admin API");
            
            // Create disabled config as placeholder with empty registries
            var placeholderEmbedding = new AiEmbeddingModelAssignment
            {
                Id = "placeholder-embedding",
                DisplayName = "Placeholder Embedding Model",
                ModelId = embeddingModel,
                Url = $"{baseUrl.TrimEnd('/')}/embeddings",
                Headers = new Dictionary<string, string>(),
                RequestTemplate = WrapTemplateAsJson("""
                {
                  "model": "{MODEL_ID}",
                  "input": "{INPUT}",
                  "encoding_format": "float"
                }
                """),
                ResponseMapping = new Dictionary<string, string>
                {
                    ["embedding"] = "$.data[0].embedding",
                    ["usage.total_tokens"] = "$.usage.total_tokens"
                },
                Dimensions = embeddingDimensions,
                Enabled = false
            };
            
            var disabledConfig = new AiProviderConfiguration
            {
                Enabled = false,
                DefaultSelectionStrategy = ModelSelectionStrategy.Standard,
                ModelRegistry = new List<AiModelAssignment>(),
                MicroModelId = null,
                MiniModelId = null,
                FullModelId = null,
                EmbeddingRegistry = new List<AiEmbeddingModelAssignment> { placeholderEmbedding },
                ActiveEmbeddingModelId = "placeholder-embedding"
            };

            await _store.UpsertAsync(disabledConfig, ct);
            _logger.LogInformation("Created disabled AI configuration placeholder");
            return;
        }

        // Helper to convert template string to JsonDocument containing a string value
        static JsonDocument WrapTemplateAsJson(string template) =>
            JsonDocument.Parse(JsonSerializer.Serialize(template));

        // Create full 3-tier OpenAI configuration
        var microModelAssignment = new AiModelAssignment
        {
            Id = "openai-micro",
            DisplayName = "OpenAI GPT-5 Nano",
            ModelId = microModel,
            Url = $"{baseUrl.TrimEnd('/')}/chat/completions",
            Headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["Authorization"] = $"Bearer {apiKey}"
            },
            RequestTemplate = WrapTemplateAsJson(DefaultRequestTemplates.OpenAiChat),
            ResponseMapping = DefaultRequestTemplates.OpenAiResponseMapping,
            DefaultParams = new Dictionary<string, JsonElement>
            {
                ["temperature"] = JsonDocument.Parse("0.0").RootElement.Clone()
            },
            MaxContextTokens = 128000,
            MaxOutputTokens = 16000,
            SupportsFunctionCalling = true,
            CostFactor = 0.1,
            Enabled = true
        };
        
        var miniModelAssignment = new AiModelAssignment
        {
            Id = "openai-mini",
            DisplayName = "OpenAI GPT-5 Mini",
            ModelId = miniModel,
            Url = $"{baseUrl.TrimEnd('/')}/chat/completions",
            Headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["Authorization"] = $"Bearer {apiKey}"
            },
            RequestTemplate = WrapTemplateAsJson(DefaultRequestTemplates.OpenAiChat),
            ResponseMapping = DefaultRequestTemplates.OpenAiResponseMapping,
            DefaultParams = new Dictionary<string, JsonElement>
            {
                ["temperature"] = JsonDocument.Parse("0.0").RootElement.Clone()
            },
            MaxContextTokens = 128000,
            MaxOutputTokens = 16000,
            SupportsFunctionCalling = true,
            CostFactor = 1.0,
            Enabled = true
        };
        
        var fullModelAssignment = new AiModelAssignment
        {
            Id = "openai-full",
            DisplayName = "OpenAI GPT-5",
            ModelId = fullModel,
            Url = $"{baseUrl.TrimEnd('/')}/chat/completions",
            Headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["Authorization"] = $"Bearer {apiKey}"
            },
            RequestTemplate = WrapTemplateAsJson(DefaultRequestTemplates.OpenAiChat),
            ResponseMapping = DefaultRequestTemplates.OpenAiResponseMapping,
            DefaultParams = new Dictionary<string, JsonElement>
            {
                ["temperature"] = JsonDocument.Parse("0.0").RootElement.Clone()
            },
            MaxContextTokens = 200000,
            MaxOutputTokens = 100000,
            SupportsFunctionCalling = true,
            CostFactor = 10.0,
            Enabled = true
        };
        
        var embeddingModelAssignment = new AiEmbeddingModelAssignment
        {
            Id = "openai-embedding",
            DisplayName = "OpenAI Text Embedding 3 Small",
            ModelId = embeddingModel,
            Url = $"{baseUrl.TrimEnd('/')}/embeddings",
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {apiKey}"
            },
            RequestTemplate = WrapTemplateAsJson("""
            {
              "model": "{MODEL_ID}",
              "input": "{INPUT}",
              "encoding_format": "float"
            }
            """),
            ResponseMapping = new Dictionary<string, string>
            {
                ["embedding"] = "$.data[0].embedding",
                ["usage.total_tokens"] = "$.usage.total_tokens"
            },
            Dimensions = embeddingDimensions,
            Enabled = true
        };
        
        var config = new AiProviderConfiguration
        {
            Enabled = true,
            DefaultSelectionStrategy = ModelSelectionStrategy.Standard,
            
            // Registry: all available models
            ModelRegistry = new List<AiModelAssignment> { microModelAssignment, miniModelAssignment, fullModelAssignment },
            
            // Tier assignments by ID
            MicroModelId = "openai-micro",
            MiniModelId = "openai-mini",
            FullModelId = "openai-full",
            
            // Embedding registry and active selection
            EmbeddingRegistry = new List<AiEmbeddingModelAssignment> { embeddingModelAssignment },
            ActiveEmbeddingModelId = "openai-embedding"
        };

        await _store.UpsertAsync(config, ct);
        _logger.LogInformation(
            "Seeded full 3-tier AI configuration (Micro: {Micro}, Mini: {Mini}, Full: {Full}, Embedding: {Embedding})", 
            microModel, miniModel, fullModel, embeddingModel);
    }
}
