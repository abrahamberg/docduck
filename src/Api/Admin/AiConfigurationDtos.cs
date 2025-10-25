using System.Text.Json;
using DocDuck.Providers.Ai;

namespace Api.Admin;

public sealed record AiConfigurationDto(
    bool Enabled,
    ModelSelectionStrategy DefaultSelectionStrategy,
    List<AiModelAssignmentDto> ModelRegistry,
    string? MicroModelId,
    string? MiniModelId,
    string? FullModelId,
    List<AiEmbeddingModelAssignmentDto> EmbeddingRegistry,
    string? ActiveEmbeddingModelId,
    DateTimeOffset LoadedAt
);

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

public sealed record AiEmbeddingModelAssignmentDto(
    string Id,
    string DisplayName,
    string ModelId,
    string BaseUrl,
    string ApiKey, // Will be masked in GET responses
    int Dimensions,
    int BatchSize,
    bool Enabled,
    ModelTestStatus TestStatus,
    DateTimeOffset? LastTestedAt,
    string? LastTestMessage,
    List<string> CustomHeaders,
    int TimeoutSeconds
);

public sealed record AiConfigurationRequest(
    bool Enabled,
    ModelSelectionStrategy? DefaultSelectionStrategy,
    List<AiModelAssignmentDto>? ModelRegistry,
    string? MicroModelId,
    string? MiniModelId,
    string? FullModelId,
    List<AiEmbeddingModelAssignmentDto>? EmbeddingRegistry,
    string? ActiveEmbeddingModelId
);

public sealed record AiProbeRequest(
    string ModelId,
    string BaseUrl,
    string ApiKey,
    List<string>? CustomHeaders,
    int? TimeoutSeconds
);

public sealed record AiProbeResponse(
    bool Success,
    string Message,
    JsonElement? Details
);

public sealed record EmbeddingChangeWarningResponse(
    bool WillDropEmbeddings,
    string Warning,
    int CurrentDimensions,
    int NewDimensions,
    long EstimatedAffectedChunks
);

public static class AiConfigurationMapper
{
    public static AiConfigurationDto ToDto(AiProviderConfiguration config, DateTimeOffset loadedAt)
    {
        return new AiConfigurationDto(
            Enabled: config.Enabled,
            DefaultSelectionStrategy: config.DefaultSelectionStrategy,
            ModelRegistry: config.ModelRegistry.Select(m => ToDto(m, maskApiKey: true)).ToList(),
            MicroModelId: config.MicroModelId,
            MiniModelId: config.MiniModelId,
            FullModelId: config.FullModelId,
            EmbeddingRegistry: config.EmbeddingRegistry.Select(m => ToDto(m, maskApiKey: true)).ToList(),
            ActiveEmbeddingModelId: config.ActiveEmbeddingModelId,
            LoadedAt: loadedAt
        );
    }

    public static AiModelAssignmentDto ToDto(AiModelAssignment model, bool maskApiKey)
    {
        // Extract API key from Authorization header for masking
        var apiKey = string.Empty;
        if (model.Headers.TryGetValue("Authorization", out var authHeader))
        {
            apiKey = authHeader.Replace("Bearer ", "").Trim();
        }

        return new AiModelAssignmentDto(
            Id: model.Id,
            DisplayName: model.DisplayName,
            ModelId: model.ModelId,
            Url: model.Url,
            Headers: maskApiKey && !string.IsNullOrEmpty(apiKey)
                ? new Dictionary<string, string>(model.Headers) { ["Authorization"] = $"Bearer {MaskApiKey(apiKey)}" }
                : new Dictionary<string, string>(model.Headers),
            RequestTemplate: model.RequestTemplate.RootElement.Clone(),
            ResponseMapping: model.ResponseMapping == null ? null : new ResponseMappingDto(
                ContentPath: model.ResponseMapping.ContentPath,
                RolePath: model.ResponseMapping.RolePath,
                ToolCallsPath: model.ResponseMapping.ToolCallsPath,
                UsagePromptTokensPath: model.ResponseMapping.UsagePromptTokensPath,
                UsageCompletionTokensPath: model.ResponseMapping.UsageCompletionTokensPath,
                UsageTotalTokensPath: model.ResponseMapping.UsageTotalTokensPath,
                AutoDetected: model.ResponseMapping.AutoDetected,
                DetectedAt: model.ResponseMapping.DetectedAt
            ),
            DefaultParams: new Dictionary<string, JsonElement>(
                model.DefaultParams.Select(kvp => new KeyValuePair<string, JsonElement>(kvp.Key, kvp.Value.Clone()))
            ),
            MaxContextTokens: model.MaxContextTokens,
            MaxOutputTokens: model.MaxOutputTokens,
            SupportsFunctionCalling: model.SupportsFunctionCalling,
            CostFactor: model.CostFactor,
            Enabled: model.Enabled,
            TestStatus: model.TestStatus,
            LastTestedAt: model.LastTestedAt,
            LastTestMessage: model.LastTestMessage,
            TimeoutSeconds: model.TimeoutSeconds
        );
    }

    public static AiEmbeddingModelAssignmentDto ToDto(AiEmbeddingModelAssignment model, bool maskApiKey)
    {
        return new AiEmbeddingModelAssignmentDto(
            Id: model.Id,
            DisplayName: model.DisplayName,
            ModelId: model.ModelId,
            BaseUrl: model.BaseUrl,
            ApiKey: maskApiKey ? MaskApiKey(model.ApiKey) : model.ApiKey,
            Dimensions: model.Dimensions,
            BatchSize: model.BatchSize,
            Enabled: model.Enabled,
            TestStatus: model.TestStatus,
            LastTestedAt: model.LastTestedAt,
            LastTestMessage: model.LastTestMessage,
            CustomHeaders: new List<string>(model.CustomHeaders),
            TimeoutSeconds: model.TimeoutSeconds
        );
    }

    public static AiProviderConfiguration FromDto(AiConfigurationRequest dto)
    {
        return new AiProviderConfiguration
        {
            Enabled = dto.Enabled,
            DefaultSelectionStrategy = dto.DefaultSelectionStrategy ?? ModelSelectionStrategy.Standard,
            ModelRegistry = dto.ModelRegistry?.Select(FromDto).ToList() ?? new List<AiModelAssignment>(),
            MicroModelId = dto.MicroModelId,
            MiniModelId = dto.MiniModelId,
            FullModelId = dto.FullModelId,
            EmbeddingRegistry = dto.EmbeddingRegistry?.Select(FromDto).ToList() ?? new List<AiEmbeddingModelAssignment>(),
            ActiveEmbeddingModelId = dto.ActiveEmbeddingModelId
        };
    }

    public static AiModelAssignment FromDto(AiModelAssignmentDto dto)
    {
        return new AiModelAssignment
        {
            Id = dto.Id,
            DisplayName = dto.DisplayName,
            ModelId = dto.ModelId,
            Url = dto.Url,
            Headers = new Dictionary<string, string>(dto.Headers),
            RequestTemplate = JsonDocument.Parse(dto.RequestTemplate.GetRawText()),
            ResponseMapping = dto.ResponseMapping == null ? null : new ResponseMapping
            {
                ContentPath = dto.ResponseMapping.ContentPath,
                RolePath = dto.ResponseMapping.RolePath,
                ToolCallsPath = dto.ResponseMapping.ToolCallsPath,
                UsagePromptTokensPath = dto.ResponseMapping.UsagePromptTokensPath,
                UsageCompletionTokensPath = dto.ResponseMapping.UsageCompletionTokensPath,
                UsageTotalTokensPath = dto.ResponseMapping.UsageTotalTokensPath,
                AutoDetected = dto.ResponseMapping.AutoDetected,
                DetectedAt = dto.ResponseMapping.DetectedAt
            },
            DefaultParams = new Dictionary<string, JsonElement>(
                dto.DefaultParams.Select(kvp => new KeyValuePair<string, JsonElement>(kvp.Key, kvp.Value.Clone()))
            ),
            MaxContextTokens = dto.MaxContextTokens,
            MaxOutputTokens = dto.MaxOutputTokens,
            SupportsFunctionCalling = dto.SupportsFunctionCalling,
            CostFactor = dto.CostFactor,
            Enabled = dto.Enabled,
            TestStatus = dto.TestStatus,
            LastTestedAt = dto.LastTestedAt,
            LastTestMessage = dto.LastTestMessage,
            TimeoutSeconds = dto.TimeoutSeconds
        };
    }

    public static AiEmbeddingModelAssignment FromDto(AiEmbeddingModelAssignmentDto dto)
    {
        return new AiEmbeddingModelAssignment
        {
            Id = dto.Id,
            DisplayName = dto.DisplayName,
            ModelId = dto.ModelId,
            BaseUrl = dto.BaseUrl,
            ApiKey = dto.ApiKey,
            Dimensions = dto.Dimensions,
            BatchSize = dto.BatchSize,
            Enabled = dto.Enabled,
            TestStatus = dto.TestStatus,
            LastTestedAt = dto.LastTestedAt,
            LastTestMessage = dto.LastTestMessage,
            CustomHeaders = new List<string>(dto.CustomHeaders),
            TimeoutSeconds = dto.TimeoutSeconds
        };
    }

    private static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length < 8)
        {
            return "***";
        }

        return $"{apiKey.Substring(0, 4)}...{apiKey.Substring(apiKey.Length - 4)}";
    }
}

// Request to test a saved model by ID
public sealed record TestModelRequest(string ModelId);

// Request to import a model from cURL command
public sealed record ImportCurlRequest(
    string CurlCommand,
    string? ModelId = null,
    string? DisplayName = null
);

// Response from cURL import
public sealed record ImportCurlResponse(
    bool Success,
    string Message,
    AiModelAssignmentDto? Model
);

// Request to probe/test a model and auto-detect response structure
public sealed record ProbeModelRequest(
    string Url,
    string? ModelId = null,
    Dictionary<string, string>? Headers = null,
    JsonDocument? RequestTemplate = null,
    int? TimeoutSeconds = null
);

// Response from model probe
public sealed record ProbeModelResponse(
    bool Success,
    string Message,
    ResponseMappingDto? ResponseMapping,
    string? ResponseSample,
    long ElapsedMs
);
