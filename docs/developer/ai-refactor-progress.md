# AI Configuration Refactor - Progress Report

## ✅ Completed Components

### 1. System Prompts (`SystemPrompts.cs`)
- ✅ Created static class with hardcoded prompts (Refine, Chat, Evaluation)
- ✅ Removed from database/settings - now managed in code
- **Location**: `src/Providers.Shared/Ai/SystemPrompts.cs`

### 2. Flexible Model Configuration (`FlexibleAiModel.cs`)
- ✅ New model structure supporting any OpenAI-compatible API
- ✅ Includes: URL, Headers, RequestTemplate, ResponseMapping, DefaultParams
- ✅ Per-model configuration (temperature now in `DefaultParams`)
- **Location**: `src/Providers.Shared/Ai/FlexibleAiModel.cs`

### 3. Template Substitution Service (`TemplateSubstitutionService.cs`)
- ✅ Static service for variable replacement in request templates
- ✅ Supports: `{MODEL_ID}`, `{MESSAGES}`, `{TEMPERATURE}`, `{MAX_TOKENS}`, `{TOOLS}`, etc.
- ✅ Uses `TemplateContext` record to avoid excessive parameters
- **Location**: `src/Providers.Shared/Ai/TemplateSubstitutionService.cs`

### 4. Response Structure Auto-Detection (`ResponseMappingDetector.cs`)
- ✅ Analyzes API responses to detect data structure
- ✅ Creates JSONPath mappings for content, role, usage, tool_calls
- ✅ Supports OpenAI, Anthropic, and generic formats
- **Location**: `src/Providers.Shared/Ai/ResponseMappingDetector.cs`

### 5. cURL Import Service (`CurlImportService.cs`)
- ✅ Parses cURL commands to extract URL, headers, body
- ✅ Converts body to template format with variable placeholders
- ✅ Extracts default parameters (temperature, top_p, etc.)
- **Location**: `src/Providers.Shared/Ai/CurlImportService.cs`

### 6. Database Schema Updates
- ✅ Migration script: `sql/04-migrate-flexible-ai-models.sql`
- ✅ Updated init schema: `sql/00-init-schema.sql`
- ✅ New columns in `ai_provider_settings`:
  - `url` - Full API endpoint URL
  - `headers` - JSON object for HTTP headers
  - `request_template` - Request body template with placeholders
  - `response_mapping` - JSONPath expressions for parsing responses
  - `default_params` - Model-specific defaults (temperature, etc.)
- ✅ Indexes for efficient queries on new JSONB columns

---

## 🚧 Remaining Work

### 7. Update `AiProviderConfiguration` Class
**File**: `src/Providers.Shared/Ai/AiProviderConfiguration.cs`

**Changes needed**:
- ❌ Remove `DefaultTemperature` property (moved to per-model `DefaultParams`)
- ❌ Remove `RefineSystemPrompt` property (now in `SystemPrompts.Refine`)
- ❌ Update `AiModelAssignment` to use/reference `FlexibleAiModel` structure
- ❌ Update `AiEmbeddingModelAssignment` similarly
- ❌ Remove validation logic for removed properties

**Impact**: This class is used extensively - careful refactoring required.

---

### 8. Update `AiModelAssignment` Class
**File**: `src/Providers.Shared/Ai/AiModelAssignment.cs`

**Changes needed**:
- ❌ Merge with or replace by `FlexibleAiModel`
- ❌ Add: `Url`, `Headers`, `RequestTemplate`, `ResponseMapping`, `DefaultParams`
- ❌ Remove: Hardcoded `BaseUrl` + endpoint assumption
- ❌ Update all consumers to use new structure

**Approach**: Consider deprecating `AiModelAssignment` in favor of `FlexibleAiModel` or merging them.

---

### 9. Refactor `GenericAiHttpClient`
**File**: `src/Providers.Shared/Ai/GenericAiHttpClient.cs`

**Changes needed**:
- ❌ Accept `FlexibleAiModel` instead of `AiModelAssignment`
- ❌ Use `TemplateSubstitutionService` to build requests
- ❌ Use `ResponseMappingDetector` to parse responses
- ❌ Remove hardcoded OpenAI format assumptions
- ❌ Support auto-detection of response structure on first call

**Example**:
```csharp
public async Task<ChatCompletionResult> CompleteChatAsync(
    FlexibleAiModel model,
    List<ChatMessagePayload> messages,
    double? temperatureOverride = null,
    // ... other params
)
{
    // Build request using template substitution
    var context = new TemplateContext(
        ModelId: model.ModelId,
        Messages: messages,
        Temperature: temperatureOverride ?? GetDefaultTemperature(model),
        // ...
    );
    
    var requestBody = TemplateSubstitutionService.Substitute(
        model.RequestTemplate.RootElement.GetRawText(),
        context
    );
    
    // Send request
    var response = await SendRequestAsync(model.Url, model.Headers, requestBody);
    
    // Auto-detect response structure if not yet mapped
    if (model.ResponseMapping == null || model.ResponseMapping.AutoDetected)
    {
        var detector = new ResponseMappingDetector();
        model.ResponseMapping = detector.DetectMapping(response);
        // TODO: Persist updated mapping to DB
    }
    
    // Parse response using mapping
    return ParseResponse(response, model.ResponseMapping);
}
```

---

### 10. Update `AiConfigurationSeeder`
**File**: `src/Providers.Shared/Ai/AiConfigurationSeeder.cs`

**Changes needed**:
- ❌ Remove `RefineSystemPrompt` seeding
- ❌ Remove `DefaultTemperature` from global config
- ❌ Create models with:
  - Full URL (not BaseUrl + endpoint)
  - Headers with Authorization
  - Request template (default OpenAI format)
  - Response mapping (default OpenAI paths)
  - Default params with temperature per-model

**Example**:
```csharp
var microModel = new FlexibleAiModel
{
    Id = "openai-micro",
    DisplayName = "OpenAI GPT-5 Nano",
    ModelId = microModel,
    Url = $"{baseUrl}/chat/completions",
    Headers = new Dictionary<string, string>
    {
        ["Content-Type"] = "application/json",
        ["Authorization"] = $"Bearer {apiKey}"
    },
    RequestTemplate = JsonDocument.Parse(DefaultRequestTemplates.OpenAiChat),
    ResponseMapping = DefaultRequestTemplates.OpenAiResponseMapping,
    DefaultParams = new Dictionary<string, JsonElement>
    {
        ["temperature"] = JsonDocument.Parse("0.7").RootElement
    },
    // ... other properties
};
```

---

### 11. Update Service Consumers
**Files**:
- `src/Api/Services/ChatService.cs`
- `src/Api/Services/OpenAiSdkService.cs`
- `src/Providers.Shared/Ai/ModelAgnosticAiService.cs`

**Changes needed**:
- ❌ Replace `config.RefineSystemPrompt` with `SystemPrompts.Refine`
- ❌ Replace `config.DefaultTemperature` with per-model `DefaultParams["temperature"]`
- ❌ Update model selection logic if needed

**Find/Replace**:
```csharp
// OLD
var systemPrompt = config?.RefineSystemPrompt ?? "default...";

// NEW
var systemPrompt = SystemPrompts.Refine;
```

---

### 12. Update DTOs and API Contracts
**File**: `src/Api/Admin/AiConfigurationDtos.cs`

**Changes needed**:
- ❌ Remove `DefaultTemperature` from `AiConfigurationDto`
- ❌ Remove `RefineSystemPrompt` from `AiConfigurationDto`
- ❌ Add DTOs for `FlexibleAiModel` structure
- ❌ Add DTOs for cURL import request/response

**New DTOs needed**:
```csharp
public sealed record FlexibleAiModelDto(
    string Id,
    string DisplayName,
    string ModelId,
    string Url,
    Dictionary<string, string> Headers,
    JsonDocument RequestTemplate,
    ResponseMappingDto? ResponseMapping,
    Dictionary<string, JsonElement> DefaultParams,
    // ... existing model properties
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

public sealed record ImportCurlRequest(
    string CurlCommand,
    string ModelId,
    string DisplayName
);

public sealed record ProbeModelRequest(
    string ModelId,
    string? TestMessage = "ping"
);

public sealed record ProbeModelResponse(
    bool Success,
    string Message,
    ResponseMappingDto? DetectedMapping,
    string? RawResponse
);
```

---

### 13. Create Admin API Endpoints
**File**: `src/Api/Admin/AdminEndpointExtensions.cs`

**New endpoints needed**:

```csharp
// POST /admin/ai/import-curl
app.MapPost("/admin/ai/import-curl", async (
    ImportCurlRequest request,
    AiProviderConfigurationStore store) =>
{
    var model = CurlImportService.ParseCurl(
        request.CurlCommand,
        request.ModelId,
        request.DisplayName
    );
    
    // Add to configuration
    // ... save to store
    
    return Results.Ok(model);
})
.RequireAuthorization()
.WithTags("AI Configuration");

// POST /admin/ai/models/{id}/probe
app.MapPost("/admin/ai/models/{id}/probe", async (
    string id,
    ProbeModelRequest request,
    AiProviderConfigurationStore store,
    ResponseMappingDetector detector) =>
{
    // Get model config
    var config = await store.GetAsync();
    var model = config.ModelRegistry.FirstOrDefault(m => m.Id == id);
    
    if (model == null)
    {
        return Results.NotFound($"Model {id} not found");
    }
    
    // Send test request
    var client = new GenericAiHttpClient(model);
    var messages = new List<ChatMessagePayload>
    {
        new("user", request.TestMessage ?? "ping")
    };
    
    try
    {
        var result = await client.CompleteChatAsync(messages);
        
        // Auto-detect response structure
        var mapping = detector.DetectMapping(/* raw response */);
        
        // Update model's response mapping
        model.ResponseMapping = mapping;
        await store.UpsertAsync(config);
        
        return Results.Ok(new ProbeModelResponse(
            Success: true,
            Message: "Model tested successfully",
            DetectedMapping: /* convert to DTO */,
            RawResponse: /* raw JSON */
        ));
    }
    catch (Exception ex)
    {
        return Results.Ok(new ProbeModelResponse(
            Success: false,
            Message: ex.Message,
            DetectedMapping: null,
            RawResponse: null
        ));
    }
})
.RequireAuthorization()
.WithTags("AI Configuration");
```

---

### 14. Update `AiProviderConfigurationStore`
**File**: `src/Providers.Shared/Ai/AiProviderConfigurationStore.cs`

**Changes needed**:
- ❌ Update serialization/deserialization to handle new columns
- ❌ Map `FlexibleAiModel` properties to DB columns (`url`, `headers`, `request_template`, `response_mapping`, `default_params`)
- ❌ Remove handling of `DefaultTemperature` and `RefineSystemPrompt` from global settings JSONB

**SQL Update Example**:
```csharp
private const string UpsertSql = """
    INSERT INTO ai_provider_settings (
        provider_id, provider_type, settings,
        url, headers, request_template, response_mapping, default_params,
        test_status, updated_at
    )
    VALUES (
        @ProviderId, @ProviderType, @Settings::jsonb,
        @Url, @Headers::jsonb, @RequestTemplate::jsonb, @ResponseMapping::jsonb, @DefaultParams::jsonb,
        @TestStatus, NOW()
    )
    ON CONFLICT (provider_id)
    DO UPDATE SET
        settings = EXCLUDED.settings,
        url = EXCLUDED.url,
        headers = EXCLUDED.headers,
        request_template = EXCLUDED.request_template,
        response_mapping = EXCLUDED.response_mapping,
        default_params = EXCLUDED.default_params,
        test_status = EXCLUDED.test_status,
        updated_at = NOW()
    """;
```

---

## 📋 Testing Checklist

After completing remaining work:

- [ ] Run database migration: `psql -f sql/04-migrate-flexible-ai-models.sql`
- [ ] Test seeder creates valid default configuration
- [ ] Test cURL import with real OpenAI curl command
- [ ] Test model probe/auto-detection with different APIs (OpenAI, local model)
- [ ] Verify system prompts work (refine query, chat, evaluation)
- [ ] Test temperature override (per-request vs per-model default)
- [ ] Verify backward compatibility or document breaking changes
- [ ] Update API documentation for new endpoints

---

## 🎯 Design Decisions Summary

| Aspect | Decision | Rationale |
|--------|----------|-----------|
| **System Prompts** | Hardcoded in `SystemPrompts` class | Simplicity, no user configuration needed |
| **Temperature** | Per-model in `default_params` JSON | Different models need different defaults |
| **Response Structure** | Auto-detect JSONPath on first call | Support any API without manual configuration |
| **Request Template** | JSON with `{VARIABLE}` placeholders | Flexible, supports any API format |
| **cURL Import** | Parse and convert to template | Easy onboarding for users with existing API calls |
| **Backward Compatibility** | Breaking changes allowed | Pre-release, clean architecture over compatibility |

---

## 🔧 Quick Reference

### Getting Default Temperature for a Model
```csharp
// OLD
var temp = config.DefaultTemperature;

// NEW
var temp = model.DefaultParams.TryGetValue("temperature", out var t) 
    ? t.GetDouble() 
    : 0.7;
```

### Using System Prompts
```csharp
// OLD
var prompt = config.RefineSystemPrompt;

// NEW
var prompt = SystemPrompts.Refine;
```

### Creating a Model from cURL
```csharp
var curl = """
    curl https://api.openai.com/v1/chat/completions \
      -H "Authorization: Bearer sk-..." \
      -d '{"model": "gpt-4", "messages": [{"role": "user", "content": "Hello"}]}'
    """;

var model = CurlImportService.ParseCurl(curl, "my-model-id", "My Custom Model");
```

---

## 📦 Files Modified/Created

### Created
- ✅ `src/Providers.Shared/Ai/SystemPrompts.cs`
- ✅ `src/Providers.Shared/Ai/FlexibleAiModel.cs`
- ✅ `src/Providers.Shared/Ai/TemplateSubstitutionService.cs`
- ✅ `src/Providers.Shared/Ai/ResponseMappingDetector.cs`
- ✅ `src/Providers.Shared/Ai/CurlImportService.cs`
- ✅ `sql/04-migrate-flexible-ai-models.sql`
- ✅ `docs/developer/ai-refactor-progress.md` (this file)

### Modified
- ✅ `sql/00-init-schema.sql` (added new columns + indexes)

### To Be Modified
- ❌ `src/Providers.Shared/Ai/AiProviderConfiguration.cs`
- ❌ `src/Providers.Shared/Ai/AiModelAssignment.cs`
- ❌ `src/Providers.Shared/Ai/GenericAiHttpClient.cs`
- ❌ `src/Providers.Shared/Ai/AiConfigurationSeeder.cs`
- ❌ `src/Providers.Shared/Ai/AiProviderConfigurationStore.cs`
- ❌ `src/Api/Admin/AiConfigurationDtos.cs`
- ❌ `src/Api/Admin/AdminEndpointExtensions.cs`
- ❌ `src/Api/Services/ChatService.cs`
- ❌ `src/Api/Services/OpenAiSdkService.cs`
- ❌ `src/Providers.Shared/Ai/ModelAgnosticAiService.cs`

---

## 🚀 Next Steps

1. **Decide on AiModelAssignment vs FlexibleAiModel**
   - Merge them into one class, or keep separate?
   - If merging, rename `FlexibleAiModel` → `AiModelAssignment` to minimize changes?

2. **Update AiProviderConfiguration**
   - Remove deprecated properties
   - Update validation logic
   - Consider versioning the settings JSONB structure

3. **Refactor GenericAiHttpClient**
   - This is the critical integration point
   - Test with multiple API providers

4. **Create Admin Endpoints**
   - cURL import
   - Model probe/test
   - Response mapping override

5. **Update Documentation**
   - User guide for model configuration
   - Developer guide for adding new providers
   - Migration guide for existing deployments

---

**Status**: Foundation complete (7/14 tasks). Ready for integration phase.
