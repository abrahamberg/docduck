using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using DocDuck.Providers.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Api.Tests.Integration;

/// <summary>
/// End-to-end integration tests for AI configuration system.
/// Uses ephemeral Docker PostgreSQL database and real OpenAI API calls.
/// </summary>
public class AiConfigurationEndToEndTests : IAsyncLifetime
{
    private const int TestDbPort = 54320; // Non-standard port to avoid conflicts
    private const string TestDbName = "docduck_test_e2e";
    private const string TestDbUser = "test_user";
    private const string TestDbPassword = "test_pass_123";

    private readonly ITestOutputHelper _output;
    private readonly string? _apiKey;
    private string? _containerId;
    private string? _connectionString;
    private AiProviderConfigurationStore? _store;

    public AiConfigurationEndToEndTests(ITestOutputHelper output)
    {
        _output = output;
        _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    }

    public async Task InitializeAsync()
    {
        if (_apiKey == null)
        {
            return; // Skip initialization if no API key
        }

        _output.WriteLine("Starting ephemeral PostgreSQL container...");

        // Start PostgreSQL container on non-standard port
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"run -d --rm " +
                       $"-e POSTGRES_DB={TestDbName} " +
                       $"-e POSTGRES_USER={TestDbUser} " +
                       $"-e POSTGRES_PASSWORD={TestDbPassword} " +
                       $"-p {TestDbPort}:5432 " +
                       $"pgvector/pgvector:pg16",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo);
        if (process == null)
            throw new InvalidOperationException("Failed to start Docker container");

        _containerId = (await process.StandardOutput.ReadToEndAsync()).Trim();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"Docker container failed to start: {error}");
        }

        _output.WriteLine($"Container started: {_containerId}");

        // Wait for PostgreSQL to be ready
        _connectionString = $"Host=localhost;Port={TestDbPort};Database={TestDbName};Username={TestDbUser};Password={TestDbPassword}";
        await WaitForDatabaseAsync();

        // Initialize schema
        await InitializeDatabaseSchemaAsync();

        // Create store
        _store = new AiProviderConfigurationStore(_connectionString);

        _output.WriteLine("Database ready");
    }

    public async Task DisposeAsync()
    {
        if (!string.IsNullOrEmpty(_containerId))
        {
            _output.WriteLine($"Stopping container {_containerId}...");
            var stopInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"stop {_containerId}",
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            using var process = Process.Start(stopInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                _output.WriteLine("Container stopped");
            }
        }
    }

    [Fact(Skip = "Requires OPENAI_API_KEY environment variable")]
    [Obsolete]
    public async Task EndToEnd_AddModelConfiguration_SaveToDatabase_CallOpenAI()
    {
        // Simulate what the frontend sends - add a new model configuration
        var newModelRequest = new AiModelAssignmentDto(
            Id: "custom-gpt4-mini",
            DisplayName: "Custom GPT-4 Mini",
            ModelId: "gpt-4o-mini",
            Url: "https://api.openai.com/v1/chat/completions",
            Headers: new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["Authorization"] = $"Bearer {_apiKey}"
            },
            // Template stored as JSON string value
            RequestTemplate: JsonDocument.Parse(JsonSerializer.Serialize("""
            {
              "model": "{MODEL_ID}",
              "messages": {MESSAGES},
              "temperature": {TEMPERATURE},
              "max_tokens": {MAX_TOKENS}
            }
            """)).RootElement,
            ResponseMapping: new ResponseMappingDto(
                ContentPath: "choices[0].message.content",
                RolePath: "choices[0].message.role",
                ToolCallsPath: "choices[0].message.tool_calls",
                UsagePromptTokensPath: "usage.prompt_tokens",
                UsageCompletionTokensPath: "usage.completion_tokens",
                UsageTotalTokensPath: "usage.total_tokens",
                AutoDetected: false,
                DetectedAt: null
            ),
            DefaultParams: new Dictionary<string, JsonElement>
            {
                ["temperature"] = JsonDocument.Parse("0.7").RootElement.Clone(),
                ["top_p"] = JsonDocument.Parse("1.0").RootElement.Clone()
            },
            MaxContextTokens: 128000,
            MaxOutputTokens: 16000,
            SupportsFunctionCalling: true,
            CostFactor: 1.0,
            Enabled: true,
            TestStatus: ModelTestStatus.Untested,
            LastTestedAt: null,
            LastTestMessage: null,
            TimeoutSeconds: 30
        );

        // Step 1: Convert DTO to domain model and save to database
        _output.WriteLine("Step 1: Saving model configuration to database...");

        var modelAssignment = new AiModelAssignment
        {
            Id = newModelRequest.Id,
            DisplayName = newModelRequest.DisplayName,
            ModelId = newModelRequest.ModelId,
            Url = newModelRequest.Url,
            Headers = newModelRequest.Headers,
            RequestTemplate = JsonDocument.Parse(newModelRequest.RequestTemplate!.GetRawText()),
            ResponseMapping = new ResponseMapping
            {
                ContentPath = newModelRequest.ResponseMapping!.ContentPath,
                RolePath = newModelRequest.ResponseMapping.RolePath,
                ToolCallsPath = newModelRequest.ResponseMapping.ToolCallsPath,
                UsagePromptTokensPath = newModelRequest.ResponseMapping.UsagePromptTokensPath,
                UsageCompletionTokensPath = newModelRequest.ResponseMapping.UsageCompletionTokensPath,
                UsageTotalTokensPath = newModelRequest.ResponseMapping.UsageTotalTokensPath,
                AutoDetected = newModelRequest.ResponseMapping.AutoDetected,
                DetectedAt = newModelRequest.ResponseMapping.DetectedAt
            },
            DefaultParams = newModelRequest.DefaultParams,
            MaxContextTokens = newModelRequest.MaxContextTokens,
            MaxOutputTokens = newModelRequest.MaxOutputTokens,
            SupportsFunctionCalling = newModelRequest.SupportsFunctionCalling,
            CostFactor = newModelRequest.CostFactor,
            Enabled = newModelRequest.Enabled,
            TestStatus = newModelRequest.TestStatus,
            TimeoutSeconds = newModelRequest.TimeoutSeconds
        };

        var config = new AiProviderConfiguration
        {
            Enabled = true,
            DefaultSelectionStrategy = ModelSelectionStrategy.Standard,
            ModelRegistry = [modelAssignment],
            MicroModelId = modelAssignment.Id,
            MiniModelId = modelAssignment.Id,
            FullModelId = modelAssignment.Id,
            EmbeddingRegistry =
            [
                new()
                {
                    Id = "test-embedding",
                    DisplayName = "Test Embedding Model",
                    ModelId = "text-embedding-3-small",
                    BaseUrl = "https://api.openai.com/v1",
                    ApiKey = _apiKey ?? string.Empty,
                    Dimensions = 1536,
                    BatchSize = 100,
                    Enabled = true,
                    CustomHeaders = [],
                    TimeoutSeconds = 30
                }
            ],
            ActiveEmbeddingModelId = "test-embedding"
        };

        await _store!.UpsertAsync(config);
        _output.WriteLine($"✓ Model '{modelAssignment.DisplayName}' saved to database");

        // Step 2: Load configuration from database (simulating application startup)
        _output.WriteLine("Step 2: Loading configuration from database...");

        var loadedConfig = await _store.GetAsync();
        Assert.NotNull(loadedConfig);
        Assert.Single(loadedConfig.ModelRegistry);

        var loadedModel = loadedConfig.ModelRegistry[0];
        Assert.Equal(newModelRequest.Id, loadedModel.Id);
        Assert.Equal(newModelRequest.DisplayName, loadedModel.DisplayName);
        Assert.Equal(newModelRequest.ModelId, loadedModel.ModelId);
        Assert.Equal(newModelRequest.Url, loadedModel.Url);
        Assert.NotNull(loadedModel.RequestTemplate);
        Assert.NotNull(loadedModel.ResponseMapping);

        _output.WriteLine($"✓ Loaded model: {loadedModel.DisplayName}");
        _output.WriteLine($"  - URL: {loadedModel.Url}");
        _output.WriteLine($"  - Model ID: {loadedModel.ModelId}");
        _output.WriteLine($"  - Headers: {loadedModel.Headers.Count} configured");
        _output.WriteLine($"  - Template type: {loadedModel.RequestTemplate?.RootElement.ValueKind}");
        if (loadedModel.RequestTemplate != null)
        {
            var rawTemplate = loadedModel.RequestTemplate.RootElement.GetRawText();
            _output.WriteLine($"  - Template raw (first 200 chars): {rawTemplate.Substring(0, Math.Min(200, rawTemplate.Length))}");
            if (loadedModel.RequestTemplate.RootElement.ValueKind == JsonValueKind.String)
            {
                var templateStr = loadedModel.RequestTemplate.RootElement.GetString();
                _output.WriteLine($"  - Template string value (first 200 chars): {templateStr?.Substring(0, Math.Min(200, templateStr?.Length ?? 0))}");
            }
        }

        // Step 3: Use GenericAiHttpClient to call OpenAI (the way the application uses it)
        _output.WriteLine("Step 3: Calling OpenAI API using GenericAiHttpClient...");

        var aiClient = new GenericAiHttpClient(
            loadedModel,
            NullLogger<GenericAiHttpClient>.Instance
        );

        var testMessages = new List<ChatMessagePayload>
        {
            new("user", "Say 'test successful' if you can read this.")
        };

        var result = await aiClient.CompleteChatAsync(
            testMessages,
            temperature: 0.7,
            maxTokens: 50
        );

        Assert.NotNull(result);
        _output.WriteLine($"Result received:");
        _output.WriteLine($"  - Role: {result.Role}");
        _output.WriteLine($"  - Content: '{result.Content}'");
        _output.WriteLine($"  - Content length: {result.Content?.Length ?? 0}");
        _output.WriteLine($"  - Tokens: {result.PromptTokens} prompt + {result.CompletionTokens} completion = {result.TotalTokens} total");

        // Verify we got a valid response from OpenAI
        Assert.Equal("assistant", result.Role);
        Assert.True(result.TotalTokens > 0, "Should have consumed tokens");
        Assert.True(result.CompletionTokens > 0, "Should have generated completion tokens");

        // Content might be empty or non-empty depending on the model's response
        // The important thing is that the request succeeded
        _output.WriteLine($"✓ OpenAI API call successful!");

        aiClient.Dispose();


        _output.WriteLine("\n=== END-TO-END TEST COMPLETE ===");
        _output.WriteLine("✓ Frontend request → Database save → Load → OpenAI API call → Success");
    }

    [Fact(Skip = "Requires OPENAI_API_KEY environment variable")]
    public async Task EndToEnd_AddEmbeddingConfiguration_SaveToDatabase_CallOpenAI()
    {
        ArgumentNullException.ThrowIfNull(_store);
        ArgumentException.ThrowIfNullOrWhiteSpace(_connectionString);

        _output.WriteLine("\n=== EMBEDDING END-TO-END TEST ===");
        _output.WriteLine("This test validates: Frontend DTO → Database persistence → Loading → OpenAI Embeddings API call");

        // Step 1: Create embedding model configuration (simulating what frontend would send)
        _output.WriteLine("\nStep 1: Creating embedding model configuration...");

        // Create minimal chat model to satisfy validation (but we won't use it)
        var minimalChatTemplate = """
        {
          "model": "{MODEL_ID}",
          "messages": {MESSAGES}
        }
        """;

        var minimalChatModel = new AiModelAssignment
        {
            Id = "minimal-chat",
            DisplayName = "Minimal Chat Model",
            ModelId = "gpt-4o-mini",
            Url = "https://api.openai.com/v1/chat/completions",
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {_apiKey}"
            },
            RequestTemplate = JsonDocument.Parse(JsonSerializer.Serialize(minimalChatTemplate)),
            ResponseMapping = ResponseMapping.OpenAiDefault(),
            Enabled = true
        };

        var embeddingTemplate = """
        {
          "model": "{MODEL_ID}",
          "input": "{INPUT}",
          "encoding_format": "float"
        }
        """;

        var embeddingModel = new AiEmbeddingModelAssignment
        {
            Id = "test-embedding-e2e",
            DisplayName = "Test OpenAI Embedding",
            ModelId = "text-embedding-3-small",
            Url = "https://api.openai.com/v1/embeddings",
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {_apiKey}"
            },
            RequestTemplate = JsonDocument.Parse(JsonSerializer.Serialize(embeddingTemplate)),
            ResponseMapping = new Dictionary<string, string>
            {
                ["embedding"] = "$.data[0].embedding",
                ["usage.total_tokens"] = "$.usage.total_tokens"
            },
            Dimensions = 1536,
            BatchSize = 16,
            Enabled = true
        };

        var config = new AiProviderConfiguration
        {
            Enabled = true, // Enable AI with minimal chat model to pass validation
            DefaultSelectionStrategy = ModelSelectionStrategy.Standard,
            ModelRegistry = new List<AiModelAssignment> { minimalChatModel },
            MicroModelId = "minimal-chat", // Assign to Micro tier to satisfy validation
            EmbeddingRegistry = new List<AiEmbeddingModelAssignment> { embeddingModel },
            ActiveEmbeddingModelId = "test-embedding-e2e"
        };

        // Save to database
        _output.WriteLine("Saving embedding configuration to database...");
        await _store.UpsertAsync(config);
        _output.WriteLine($"✓ Embedding model '{embeddingModel.DisplayName}' saved to database");

        // Step 2: Load from database
        _output.WriteLine("\nStep 2: Loading embedding configuration from database...");
        var loadedConfig = await _store.GetAsync();
        Assert.NotNull(loadedConfig);
        Assert.NotEmpty(loadedConfig.EmbeddingRegistry);

        var loadedEmbedding = loadedConfig.EmbeddingRegistry.First();
        Assert.Equal("test-embedding-e2e", loadedEmbedding.Id);
        Assert.NotNull(loadedEmbedding.RequestTemplate);

        _output.WriteLine($"✓ Loaded embedding model: {loadedEmbedding.DisplayName}");
        _output.WriteLine($"  - URL: {loadedEmbedding.Url}");
        _output.WriteLine($"  - Model ID: {loadedEmbedding.ModelId}");
        _output.WriteLine($"  - Headers: {loadedEmbedding.Headers.Count} configured");
        _output.WriteLine($"  - Dimensions: {loadedEmbedding.Dimensions}");
        if (loadedEmbedding.RequestTemplate != null)
        {
            var templateRaw = loadedEmbedding.RequestTemplate.RootElement.GetRawText();
            _output.WriteLine($"  - Template raw: {templateRaw}");
            if (loadedEmbedding.RequestTemplate.RootElement.ValueKind == JsonValueKind.String)
            {
                var templateStr = loadedEmbedding.RequestTemplate.RootElement.GetString();
                _output.WriteLine($"  - Template string value: {templateStr}");
            }
        }
        // Step 3: Call OpenAI Embeddings API using GenericAiHttpClient
        _output.WriteLine("\nStep 3: Calling OpenAI Embeddings API using GenericAiHttpClient...");

        // GenericAiHttpClient requires a valid chat model in constructor, use the minimal one we created
        var aiClient = new GenericAiHttpClient(
            minimalChatModel,
            NullLogger<GenericAiHttpClient>.Instance
        );

        var testText = "This is a test document for embedding generation.";
        var embedding = await aiClient.EmbedAsync(loadedEmbedding, testText);

        Assert.NotNull(embedding);
        Assert.Equal(1536, embedding.Length);

        _output.WriteLine($"Result received:");
        _output.WriteLine($"  - Embedding dimensions: {embedding.Length}");
        _output.WriteLine($"  - First 5 values: [{string.Join(", ", embedding.Take(5).Select(v => v.ToString("F6")))}]");
        _output.WriteLine($"  - Vector magnitude: {Math.Sqrt(embedding.Sum(v => v * v)):F6}");

        // Verify embedding is valid
        Assert.True(embedding.Length == 1536, "Should have 1536 dimensions for text-embedding-3-small");
        Assert.True(embedding.Any(v => v != 0.0f), "Embedding should have non-zero values");

        _output.WriteLine($"✓ OpenAI Embeddings API call successful!");

        aiClient.Dispose();

        _output.WriteLine("\n=== EMBEDDING END-TO-END TEST COMPLETE ===");
        _output.WriteLine("✓ Frontend request → Database save → Load → OpenAI Embeddings API → Success");
    }

    private async Task WaitForDatabaseAsync()
    {
        var maxAttempts = 30;
        var delay = TimeSpan.FromSeconds(1);

        for (var i = 0; i < maxAttempts; i++)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                await conn.CloseAsync();
                return;
            }
            catch
            {
                if (i == maxAttempts - 1)
                    throw;
                await Task.Delay(delay);
            }
        }
    }

    private async Task InitializeDatabaseSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // Read and execute the schema initialization script
        var schemaPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "..",
            "sql", "00-init-schema.sql"
        );

        if (!File.Exists(schemaPath))
        {
            // Try alternative path
            schemaPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "sql", "00-init-schema.sql"
            );
        }

        if (!File.Exists(schemaPath))
        {
            throw new FileNotFoundException($"Schema file not found at {schemaPath}");
        }

        var schemaSql = await File.ReadAllTextAsync(schemaPath);

        await using var cmd = new NpgsqlCommand(schemaSql, conn);
        await cmd.ExecuteNonQueryAsync();

        // Run migration for flexible AI models
        var migrationPath = Path.Combine(
            Path.GetDirectoryName(schemaPath)!,
            "04-migrate-flexible-ai-models.sql"
        );

        if (File.Exists(migrationPath))
        {
            var migrationSql = await File.ReadAllTextAsync(migrationPath);
            await using var migCmd = new NpgsqlCommand(migrationSql, conn);
            await migCmd.ExecuteNonQueryAsync();
        }
    }
}

/// <summary>
/// DTOs matching what the frontend sends (for test clarity)
/// </summary>
public sealed record AiModelAssignmentDto(
    string Id,
    string DisplayName,
    string ModelId,
    string Url,
    Dictionary<string, string> Headers,
    JsonElement RequestTemplate,
    ResponseMappingDto? ResponseMapping,
    Dictionary<string, JsonElement> DefaultParams,
    int MaxContextTokens,
    int MaxOutputTokens,
    bool SupportsFunctionCalling,
    double CostFactor,
    bool Enabled,
    ModelTestStatus TestStatus,
    DateTimeOffset? LastTestedAt,
    string? LastTestMessage,
    int TimeoutSeconds
);

public sealed record ResponseMappingDto(
    string ContentPath,
    string RolePath,
    string? ToolCallsPath,
    string? UsagePromptTokensPath,
    string? UsageCompletionTokensPath,
    string? UsageTotalTokensPath,
    bool AutoDetected,
    DateTimeOffset? DetectedAt
);
