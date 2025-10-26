using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using DocDuck.Providers.Ai;
using DocDuck.Providers.Configuration;
using DocDuck.Providers.Providers;
using DocDuck.Providers.Providers.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Api.Admin;

public static class AdminEndpointExtensions
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S3776:Cognitive Complexity of methods should not be too high")]
    public static RouteGroupBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").WithTags("Admin");

        var auth = admin.MapGroup("/auth");
        auth.MapPost("/login", async (
            AdminLoginRequest request,
            AdminUserStore userStore,
            AdminAuthService authService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new { error = "Username and password are required." });
            }

            var user = await userStore.ValidateCredentialsAsync(request.Username, request.Password, ct);
            if (user is null || !user.IsAdmin)
            {
                return Results.Unauthorized();
            }

            var token = authService.IssueToken(user.Id);
            return Results.Ok(new AdminLoginResponse(token, ToDto(user)));
        });

        auth.MapGet("/profile", (HttpContext context) => Results.Ok(GetAuthenticatedUser(context)))
            .AddEndpointFilter<AdminAuthFilter>();

        var secure = admin.MapGroup(string.Empty).AddEndpointFilter<AdminAuthFilter>();

        secure.MapGet("/providers", async (ProviderSettingsStore store, CancellationToken ct) =>
        {
            var records = await store.GetAllAsync(ct);
            var list = new List<ProviderSettingsDto>(records.Count);

            foreach (var record in records)
            {
                var payload = record.Payload;
                var clone = payload.RootElement.Clone();
                var enabled = clone.TryGetProperty("enabled", out var enabledProp) && enabledProp.ValueKind == JsonValueKind.True;
                list.Add(new ProviderSettingsDto(record.ProviderType, record.ProviderName, enabled, record.UpdatedAt, clone));
                payload.Dispose();
            }

            return Results.Ok(new { providers = list, count = list.Count });
        });

        secure.MapGet("/providers/{providerType}/{providerName}", async (string providerType, string providerName, ProviderSettingsStore store, CancellationToken ct) =>
        {
            var record = await store.GetAsync(providerType, providerName, ct);
            if (record is null)
            {
                return Results.NotFound();
            }

            var payload = record.Payload;
            var clone = payload.RootElement.Clone();
            var enabled = clone.TryGetProperty("enabled", out var enabledProp) && enabledProp.ValueKind == JsonValueKind.True;
            payload.Dispose();

            return Results.Ok(new ProviderSettingsDto(record.ProviderType, record.ProviderName, enabled, record.UpdatedAt, clone));
        });

        secure.MapPut("/providers/{providerType}/{providerName}", async (
            string providerType,
            string providerName,
            ProviderSettingsRequest request,
            ProviderSettingsStore store,
            ProviderFactory factory,
            ProviderConfigurationService configuration,
            CancellationToken ct) =>
        {
            if (request.Settings.ValueKind == JsonValueKind.Undefined || request.Settings.ValueKind == JsonValueKind.Null)
            {
                return Results.BadRequest(new { error = "Settings payload is required." });
            }

            using var doc = JsonDocument.Parse(request.Settings.GetRawText());
            var record = new ProviderSettingsRecord
            {
                ProviderType = providerType,
                ProviderName = providerName,
                Payload = doc,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            IProviderSettings settings;
            try
            {
                if (!factory.TryCreateSettings(record, out settings))
                {
                    return Results.BadRequest(new { error = $"Unsupported provider type '{providerType}'." });
                }
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            if (!string.Equals(settings.Name, providerName, StringComparison.Ordinal))
            {
                return Results.BadRequest(new { error = "Provider name in settings must match request path." });
            }

            await store.UpsertAsync(settings, ct);
            await configuration.ReloadAsync(ct);

            return Results.Ok(new { providerType, providerName });
        });

        secure.MapDelete("/providers/{providerType}/{providerName}", async (
            string providerType,
            string providerName,
            ProviderSettingsStore store,
            ProviderConfigurationService configuration,
            CancellationToken ct) =>
        {
            await store.DeleteAsync(providerType, providerName, ct);
            await configuration.ReloadAsync(ct);
            return Results.NoContent();
        });

        secure.MapPost("/providers/probe", async (
            ProviderProbeRequestDto request,
            ProviderFactory factory,
            CancellationToken ct) =>
        {
            if (request.Settings.ValueKind == JsonValueKind.Undefined || request.Settings.ValueKind == JsonValueKind.Null)
            {
                return Results.BadRequest(new { error = "Settings payload is required." });
            }

            using var doc = JsonDocument.Parse(request.Settings.GetRawText());
            var record = new ProviderSettingsRecord
            {
                ProviderType = request.ProviderType,
                ProviderName = doc.RootElement.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                    ? nameProp.GetString() ?? string.Empty
                    : string.Empty,
                Payload = doc,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            IProviderSettings settings;
            try
            {
                if (!factory.TryCreateSettings(record, out settings))
                {
                    return Results.BadRequest(new { error = $"Unsupported provider type '{request.ProviderType}'." });
                }
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var provider = factory.CreateProvider(settings);
            var probeRequest = new ProviderProbeRequest(
                request.MaxDocuments ?? ProviderProbeRequest.Default.MaxDocuments,
                request.MaxPreviewBytes ?? ProviderProbeRequest.Default.MaxPreviewBytes);

            try
            {
                var result = await provider.ProbeAsync(probeRequest, ct);
                return Results.Ok(new ProviderProbeResponse(result.Success, result.Message, result.Documents));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Ok(new ProviderProbeResponse(false, ex.Message, Array.Empty<ProviderProbeDocument>()));
            }
            finally
            {
                switch (provider)
                {
                    case IAsyncDisposable asyncDisposable:
                        await asyncDisposable.DisposeAsync();
                        break;
                    case IDisposable disposable:
                        disposable.Dispose();
                        break;
                }
            }
        });

        // AI Configuration (Model-Agnostic Multi-Tier System)
        var ai = secure.MapGroup("/ai");

        ai.MapGet("/config", async (
            AiProviderConfigurationStore store,
            ModelAgnosticAiService aiService,
            CancellationToken ct) =>
        {
            var config = await store.GetAsync(ct);
            if (config == null)
            {
                return Results.NotFound(new { error = "AI configuration not found. Create one first." });
            }

            return Results.Ok(AiConfigurationMapper.ToDto(config, aiService.LoadedAt));
        });

        ai.MapPut("/config", async (
            AiConfigurationRequest request,
            AiProviderConfigurationStore store,
            ModelAgnosticAiService aiService,
            CancellationToken ct) =>
        {
            try
            {
                var config = AiConfigurationMapper.FromDto(request);
                config.Validate();

                await store.UpsertAsync(config, ct);
                await aiService.ReloadAsync(ct);

                return Results.Ok(AiConfigurationMapper.ToDto(config, aiService.LoadedAt));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        ai.MapPost("/probe", async (
            AiProbeRequest request,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.BaseUrl) || string.IsNullOrWhiteSpace(request.ApiKey))
            {
                return Results.BadRequest(new { error = "BaseUrl and ApiKey are required for probe." });
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // Use longer timeout for probe (some models can take minutes)
                var timeout = TimeSpan.FromSeconds(request.TimeoutSeconds ?? 120);

                using var http = new HttpClient
                {
                    BaseAddress = new Uri(request.BaseUrl, UriKind.Absolute),
                    Timeout = timeout
                };

                http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", request.ApiKey);

                if (request.CustomHeaders != null)
                {
                    foreach (var header in request.CustomHeaders)
                    {
                        var parts = header.Split(':', 2, StringSplitOptions.TrimEntries);
                        if (parts.Length == 2)
                        {
                            http.DefaultRequestHeaders.TryAddWithoutValidation(parts[0], parts[1]);
                        }
                    }
                }

                // Test with actual chat completion
                var testPayload = new
                {
                    model = request.ModelId,
                    messages = new[]
                    {
                        new { role = "user", content = "Respond with just 'OK' to confirm you are working." }
                    }
                };

                var jsonContent = new StringContent(
                    JsonSerializer.Serialize(testPayload),
                    System.Text.Encoding.UTF8,
                    "application/json");

                logger.LogInformation("Probing AI model {Model} at {BaseUrl} (timeout: {Timeout}s)",
                    request.ModelId, request.BaseUrl, timeout.TotalSeconds);

                using var response = await http.PostAsync("chat/completions", jsonContent, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                sw.Stop();

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("AI probe failed for {Model}: HTTP {Status}",
                        request.ModelId, (int)response.StatusCode);

                    return Results.Ok(new AiProbeResponse(
                        Success: false,
                        Message: $"Model test failed with status {(int)response.StatusCode}. Check model ID and API key.",
                        Details: JsonSerializer.Deserialize<JsonElement>(body)
                    ));
                }

                var result = JsonSerializer.Deserialize<JsonElement>(body);
                var responseText = result.TryGetProperty("choices", out var choices) &&
                                   choices.GetArrayLength() > 0 &&
                                   choices[0].TryGetProperty("message", out var msg) &&
                                   msg.TryGetProperty("content", out var content)
                    ? content.GetString() ?? "No content"
                    : "No response";

                logger.LogInformation("AI probe succeeded for {Model} in {Elapsed}ms: {Response}",
                    request.ModelId, sw.ElapsedMilliseconds, responseText);

                return Results.Ok(new AiProbeResponse(
                    Success: true,
                    Message: $"Model responded successfully in {sw.ElapsedMilliseconds}ms: {responseText}",
                    Details: result
                ));
            }
            catch (TaskCanceledException ex)
            {
                sw.Stop();
                logger.LogWarning(ex, "AI probe timed out for {Model} after {Elapsed}ms",
                    request.ModelId, sw.ElapsedMilliseconds);

                return Results.Ok(new AiProbeResponse(
                    Success: false,
                    Message: $"Model test timed out after {sw.ElapsedMilliseconds}ms. The model may be slow or unavailable.",
                    Details: null
                ));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                sw.Stop();
                logger.LogError(ex, "AI probe failed for {Model} after {Elapsed}ms",
                    request.ModelId, sw.ElapsedMilliseconds);

                return Results.Ok(new AiProbeResponse(
                    Success: false,
                    Message: $"Connection failed: {ex.Message}",
                    Details: null
                ));
            }
        });

        // Test model endpoint - looks up saved model by ID and tests it
        ai.MapPost("/test-model", async (
            TestModelRequest request,
            AiProviderConfigurationStore store,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.ModelId))
            {
                return Results.BadRequest(new { error = "ModelId is required." });
            }

            var config = await store.GetAsync(ct);
            if (config == null)
            {
                return Results.BadRequest(new { error = "AI configuration not found." });
            }

            var model = config.ModelRegistry?.FirstOrDefault(m => m.Id == request.ModelId);
            if (model == null)
            {
                return Results.NotFound(new { error = $"Model '{request.ModelId}' not found in registry." });
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var timeout = TimeSpan.FromSeconds(120);

                // Use new flexible Url property
                if (string.IsNullOrWhiteSpace(model.Url))
                {
                    return Results.BadRequest(new { error = "Model URL is not configured." });
                }

                using var http = new HttpClient
                {
                    Timeout = timeout
                };

                // Add headers from Headers dictionary
                if (model.Headers != null)
                {
                    foreach (var (key, value) in model.Headers)
                    {
                        if (key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                        {
                            var parts = value.Split(' ', 2, StringSplitOptions.TrimEntries);
                            if (parts.Length == 2)
                            {
                                http.DefaultRequestHeaders.Authorization =
                                    new System.Net.Http.Headers.AuthenticationHeaderValue(parts[0], parts[1]);
                            }
                        }
                        else if (!key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                        {
                            http.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
                        }
                    }
                }

                // Test with function calling to verify model capabilities
                // Build request from template + DefaultParams
                var basePayload = new JsonObject
                {
                    ["model"] = model.ModelId,
                    ["messages"] = new JsonArray
                    {
                        new JsonObject { ["role"] = "system", ["content"] = "You are a helpful assistant with access to functions." },
                        new JsonObject { ["role"] = "user", ["content"] = "What is the weather in San Francisco?" }
                    },
                    ["tools"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "function",
                            ["function"] = new JsonObject
                            {
                                ["name"] = "get_weather",
                                ["description"] = "Get the current weather for a location",
                                ["parameters"] = new JsonObject
                                {
                                    ["type"] = "object",
                                    ["properties"] = new JsonObject
                                    {
                                        ["location"] = new JsonObject
                                        {
                                            ["type"] = "string",
                                            ["description"] = "The city and state, e.g. San Francisco, CA"
                                        }
                                    },
                                    ["required"] = new JsonArray { "location" }
                                }
                            }
                        }
                    }
                };

                // Merge DefaultParams from model config
                if (model.DefaultParams != null)
                {
                    foreach (var (key, value) in model.DefaultParams)
                    {
                        if (!basePayload.ContainsKey(key))
                        {
                            basePayload[key] = System.Text.Json.Nodes.JsonNode.Parse(value.GetRawText());
                        }
                    }
                }

                var testPayload = basePayload.ToJsonString();

                var jsonContent = new StringContent(
                    testPayload,
                    System.Text.Encoding.UTF8,
                    "application/json");

                logger.LogInformation("Testing saved model {ModelId} ({Model}) at {Url}",
                    model.Id, model.ModelId, model.Url);

                using var response = await http.PostAsync(model.Url, jsonContent, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                sw.Stop();

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("AI model test failed for {ModelId}: HTTP {Status}",
                        model.Id, (int)response.StatusCode);

                    JsonElement? errorDetails = null;
                    try
                    {
                        errorDetails = JsonSerializer.Deserialize<JsonElement>(body);
                    }
                    catch
                    {
                        // Not JSON
                    }

                    return Results.Ok(new AiProbeResponse(
                        Success: false,
                        Message: $"❌ HTTP {(int)response.StatusCode}: {response.ReasonPhrase}",
                        Details: errorDetails
                    ));
                }

                // First, validate we got a proper response structure
                JsonElement result;
                try
                {
                    result = JsonSerializer.Deserialize<JsonElement>(body);
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "AI model test failed for {ModelId}: response is not valid JSON", model.Id);
                    return Results.Ok(new AiProbeResponse(
                        Success: false,
                        Message: "❌ Model returned invalid JSON response",
                        Details: null
                    ));
                }

                // Check if we got the expected response structure
                if (!result.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                {
                    logger.LogWarning("AI model test failed for {ModelId}: no choices array in response", model.Id);
                    return Results.Ok(new AiProbeResponse(
                        Success: false,
                        Message: "❌ Model response missing 'choices' array",
                        Details: result
                    ));
                }

                if (!choices[0].TryGetProperty("message", out var msg))
                {
                    logger.LogWarning("AI model test failed for {ModelId}: no message in response", model.Id);
                    return Results.Ok(new AiProbeResponse(
                        Success: false,
                        Message: "❌ Model response missing message",
                        Details: result
                    ));
                }

                // Try content field first, then reasoning field (for reasoning models)
                var responseText = "";
                if (msg.TryGetProperty("content", out var content))
                {
                    responseText = content.GetString() ?? "";
                }

                // If content is empty, check reasoning field (some models use this)
                if (string.IsNullOrWhiteSpace(responseText) && msg.TryGetProperty("reasoning", out var reasoning))
                {
                    responseText = reasoning.GetString() ?? "";
                }

                // Check for function/tool calls (function calling capability test)
                var hasToolCalls = msg.TryGetProperty("tool_calls", out var toolCalls) &&
                                   toolCalls.ValueKind == JsonValueKind.Array &&
                                   toolCalls.GetArrayLength() > 0;

                var supportsFunctionCalling = false;
                var functionCallDetails = "";

                if (hasToolCalls)
                {
                    supportsFunctionCalling = true;
                    var firstCall = toolCalls[0];
                    if (firstCall.TryGetProperty("function", out var func))
                    {
                        var funcName = func.TryGetProperty("name", out var name) ? name.GetString() : "unknown";
                        var funcArgs = func.TryGetProperty("arguments", out var args) ? args.GetString() : "{}";
                        functionCallDetails = $"Function: {funcName}, Args: {funcArgs}";
                    }
                }

                // A valid response should have either content or tool calls
                if (string.IsNullOrWhiteSpace(responseText) && !hasToolCalls)
                {
                    logger.LogWarning("AI model test failed for {ModelId}: empty response content and no tool calls", model.Id);
                    return Results.Ok(new AiProbeResponse(
                        Success: false,
                        Message: "❌ Model returned empty content (no 'content', 'reasoning', or 'tool_calls')",
                        Details: result
                    ));
                }

                logger.LogInformation("AI model test succeeded for {ModelId} in {Elapsed}ms (function calling: {SupportsFunctionCalling})",
                    model.Id, sw.ElapsedMilliseconds, supportsFunctionCalling);

                var successMessage = supportsFunctionCalling
                    ? $"✓ Model responded in {sw.ElapsedMilliseconds}ms with function call - {functionCallDetails}"
                    : $"✓ Model responded in {sw.ElapsedMilliseconds}ms - \"{responseText.Substring(0, Math.Min(50, responseText.Length))}{(responseText.Length > 50 ? "..." : "")}\"";

                return Results.Ok(new AiProbeResponse(
                    Success: true,
                    Message: successMessage,
                    Details: result
                ));
            }
            catch (TaskCanceledException ex)
            {
                sw.Stop();
                logger.LogWarning(ex, "AI model test timed out for {ModelId} after {Elapsed}ms",
                    model.Id, sw.ElapsedMilliseconds);

                return Results.Ok(new AiProbeResponse(
                    Success: false,
                    Message: $"⏱ Timeout after {sw.ElapsedMilliseconds}ms",
                    Details: null
                ));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                sw.Stop();
                logger.LogError(ex, "AI model test failed for {ModelId} after {Elapsed}ms",
                    model.Id, sw.ElapsedMilliseconds);

                return Results.Ok(new AiProbeResponse(
                    Success: false,
                    Message: $"❌ {ex.Message}",
                    Details: null
                ));
            }
        });

        // Test embedding endpoint - looks up saved embedding model by ID and tests it
        ai.MapPost("/test-embedding", async (
            TestModelRequest request,
            AiProviderConfigurationStore store,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.ModelId))
            {
                return Results.BadRequest(new { error = "ModelId is required." });
            }

            var config = await store.GetAsync(ct);
            if (config == null)
            {
                return Results.BadRequest(new { error = "AI configuration not found." });
            }

            var embedding = config.EmbeddingRegistry?.FirstOrDefault(e => e.Id == request.ModelId);
            if (embedding == null)
            {
                return Results.NotFound(new { error = $"Embedding model '{request.ModelId}' not found in registry." });
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var timeout = TimeSpan.FromSeconds(120);

                // Use new flexible Url property
                if (string.IsNullOrWhiteSpace(embedding.Url))
                {
                    return Results.BadRequest(new { error = "Embedding model URL is not configured." });
                }

                using var http = new HttpClient
                {
                    Timeout = timeout
                };

                // Add headers from Headers dictionary
                if (embedding.Headers != null)
                {
                    foreach (var (key, value) in embedding.Headers)
                    {
                        if (key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                        {
                            var parts = value.Split(' ', 2, StringSplitOptions.TrimEntries);
                            if (parts.Length == 2)
                            {
                                http.DefaultRequestHeaders.Authorization =
                                    new System.Net.Http.Headers.AuthenticationHeaderValue(parts[0], parts[1]);
                            }
                        }
                        else if (!key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                        {
                            http.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
                        }
                    }
                }

                // Test embedding with simple text
                var testPayload = new
                {
                    model = embedding.ModelId,
                    input = "test embedding",
                    dimensions = embedding.Dimensions
                };

                var jsonContent = new StringContent(
                    JsonSerializer.Serialize(testPayload),
                    System.Text.Encoding.UTF8,
                    "application/json");

                logger.LogInformation("Testing saved embedding model {ModelId} ({Model}) at {Url}",
                    embedding.Id, embedding.ModelId, embedding.Url);

                using var response = await http.PostAsync(embedding.Url, jsonContent, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                sw.Stop();

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Embedding model test failed for {ModelId}: HTTP {Status}",
                        embedding.Id, (int)response.StatusCode);

                    JsonElement? errorDetails = null;
                    try
                    {
                        errorDetails = JsonSerializer.Deserialize<JsonElement>(body);
                    }
                    catch
                    {
                        // Not JSON
                    }

                    return Results.Ok(new AiProbeResponse(
                        Success: false,
                        Message: $"❌ HTTP {(int)response.StatusCode}: {response.ReasonPhrase}",
                        Details: errorDetails
                    ));
                }

                // First, validate we got proper JSON response
                JsonElement result;
                try
                {
                    result = JsonSerializer.Deserialize<JsonElement>(body);
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "Embedding model test failed for {ModelId}: response is not valid JSON", embedding.Id);
                    return Results.Ok(new AiProbeResponse(
                        Success: false,
                        Message: "❌ Model returned invalid JSON response",
                        Details: null
                    ));
                }

                if (!result.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
                {
                    logger.LogWarning("Embedding model test for {ModelId} returned no data array",
                        embedding.Id);

                    return Results.Ok(new AiProbeResponse(
                        Success: false,
                        Message: "❌ No data array in response",
                        Details: result
                    ));
                }

                if (!data[0].TryGetProperty("embedding", out var embeddingVec) || embeddingVec.GetArrayLength() == 0)
                {
                    logger.LogWarning("Embedding model test for {ModelId} returned no embedding vector",
                        embedding.Id);

                    return Results.Ok(new AiProbeResponse(
                        Success: false,
                        Message: "❌ No embedding vector in response",
                        Details: result
                    ));
                }

                var actualDimensions = embeddingVec.GetArrayLength();
                var dimensionMatch = actualDimensions == embedding.Dimensions;

                logger.LogInformation("Embedding model test succeeded for {ModelId} in {Ms}ms (dimensions: {Actual}/{Expected})",
                    embedding.Id, sw.ElapsedMilliseconds, actualDimensions, embedding.Dimensions);

                var message = dimensionMatch
                    ? $"✅ Success in {sw.ElapsedMilliseconds}ms (dimensions: {actualDimensions})"
                    : $"⚠️ Success in {sw.ElapsedMilliseconds}ms but dimension mismatch (expected: {embedding.Dimensions}, got: {actualDimensions})";

                return Results.Ok(new AiProbeResponse(
                    Success: dimensionMatch,
                    Message: message,
                    Details: JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(new { dimensions = actualDimensions, expected = embedding.Dimensions }))
                ));
            }
            catch (TaskCanceledException ex)
            {
                sw.Stop();
                logger.LogWarning(ex, "Embedding model test timed out for {ModelId} after {Ms}ms",
                    embedding.Id, sw.ElapsedMilliseconds);

                return Results.Ok(new AiProbeResponse(
                    Success: false,
                    Message: $"❌ Request timed out after {sw.ElapsedMilliseconds}ms",
                    Details: null
                ));
            }
            catch (Exception ex)
            {
                sw.Stop();
                logger.LogError(ex, "Embedding model test failed for {ModelId} after {Ms}ms",
                    embedding.Id, sw.ElapsedMilliseconds);

                return Results.Ok(new AiProbeResponse(
                    Success: false,
                    Message: $"❌ {ex.Message}",
                    Details: null
                ));
            }
        });

        ai.MapPost("/check-embedding-change", async (
            AiConfigurationRequest request,
            AiProviderConfigurationStore store,
            IOptions<Api.Options.DbOptions> dbOptions,
            CancellationToken ct) =>
        {
            var current = await store.GetAsync(ct);

            // Get new active embedding model from request
            var newEmbeddingModel = request.EmbeddingRegistry?.FirstOrDefault(e => e.Id == request.ActiveEmbeddingModelId);

            // No current config or no current embedding
            if (current?.EmbeddingModel == null)
            {
                return Results.Ok(new EmbeddingChangeWarningResponse(
                    WillDropEmbeddings: false,
                    Warning: "No existing embedding model configured.",
                    CurrentDimensions: 0,
                    NewDimensions: newEmbeddingModel?.Dimensions ?? 0,
                    EstimatedAffectedChunks: 0
                ));
            }

            // No new embedding model specified
            if (newEmbeddingModel == null || string.IsNullOrWhiteSpace(request.ActiveEmbeddingModelId))
            {
                return Results.Ok(new EmbeddingChangeWarningResponse(
                    WillDropEmbeddings: true,
                    Warning: "⚠️ Removing embedding model will disable AI features until a new embedding model is configured.",
                    CurrentDimensions: current.EmbeddingModel.Dimensions,
                    NewDimensions: 0,
                    EstimatedAffectedChunks: 0
                ));
            }

            // Check if dimensions changed
            var dimensionsChanged = current.EmbeddingModel.Dimensions != newEmbeddingModel.Dimensions;
            var modelIdChanged = current.EmbeddingModel.ModelId != newEmbeddingModel.ModelId;

            if (!dimensionsChanged && !modelIdChanged)
            {
                return Results.Ok(new EmbeddingChangeWarningResponse(
                    WillDropEmbeddings: false,
                    Warning: "Embedding model unchanged.",
                    CurrentDimensions: current.EmbeddingModel.Dimensions,
                    NewDimensions: newEmbeddingModel.Dimensions,
                    EstimatedAffectedChunks: 0
                ));
            }

            // Count affected chunks
            long affectedChunks = 0;
            try
            {
                await using var conn = new Npgsql.NpgsqlConnection(dbOptions.Value.ConnectionString);
                await conn.OpenAsync(ct);
                await using var cmd = new Npgsql.NpgsqlCommand("SELECT COUNT(*) FROM chunks", conn);
                affectedChunks = (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to count affected chunks: {ex.Message}");
            }

            var warning = dimensionsChanged
                ? $"⚠️ WARNING: Embedding dimensions changed from {current.EmbeddingModel.Dimensions} to {newEmbeddingModel.Dimensions}. " +
                  $"ALL {affectedChunks:N0} existing chunk embeddings will be DROPPED and must be re-indexed. " +
                  $"This is irreversible. Ensure you have a backup or are prepared to re-run the indexer."
                : $"⚠️ Model ID changed from '{current.EmbeddingModel.ModelId}' to '{newEmbeddingModel.ModelId}'. " +
                  $"Consider re-indexing {affectedChunks:N0} chunks if the new model produces incompatible embeddings.";

            return Results.Ok(new EmbeddingChangeWarningResponse(
                WillDropEmbeddings: dimensionsChanged,
                Warning: warning,
                CurrentDimensions: current.EmbeddingModel.Dimensions,
                NewDimensions: newEmbeddingModel.Dimensions,
                EstimatedAffectedChunks: affectedChunks
            ));
        });

        // Import model from cURL command
        ai.MapPost("/import-curl", (ImportCurlRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.CurlCommand))
            {
                return Results.BadRequest(new { error = "cURL command is required." });
            }

            try
            {
                var model = CurlImportService.ParseCurl(
                    request.CurlCommand,
                    request.ModelId ?? "imported-model",
                    request.DisplayName ?? "Imported Model"
                );

                // Convert to DTO for response
                var dto = new AiModelAssignmentDto(
                    Id: model.Id,
                    DisplayName: model.DisplayName,
                    ModelId: model.ModelId,
                    Url: model.Url,
                    Headers: model.Headers ?? new Dictionary<string, string>(),
                    RequestTemplate: model.RequestTemplate?.RootElement.Clone() ?? default,
                    ResponseMapping: model.ResponseMapping != null ? new ResponseMappingDto(
                        ContentPath: model.ResponseMapping.ContentPath,
                        RolePath: model.ResponseMapping.RolePath,
                        ToolCallsPath: model.ResponseMapping.ToolCallsPath,
                        UsagePromptTokensPath: model.ResponseMapping.UsagePromptTokensPath,
                        UsageCompletionTokensPath: model.ResponseMapping.UsageCompletionTokensPath,
                        UsageTotalTokensPath: model.ResponseMapping.UsageTotalTokensPath,
                        AutoDetected: model.ResponseMapping.AutoDetected,
                        DetectedAt: model.ResponseMapping.DetectedAt
                    ) : null,
                    DefaultParams: model.DefaultParams ?? new Dictionary<string, JsonElement>(),
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

                return Results.Ok(new ImportCurlResponse(
                    Success: true,
                    Message: "Successfully parsed cURL command. Review the model configuration before saving.",
                    Model: dto
                ));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.Ok(new ImportCurlResponse(
                    Success: false,
                    Message: $"Failed to parse cURL command: {ex.Message}",
                    Model: null
                ));
            }
        });

        // Probe model endpoint and auto-detect response structure
        ai.MapPost("/models/probe", async (
            ProbeModelRequest request,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Url))
            {
                return Results.BadRequest(new { error = "URL is required." });
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                using var http = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(request.TimeoutSeconds ?? 120)
                };

                // Add headers
                if (request.Headers != null)
                {
                    foreach (var (key, value) in request.Headers)
                    {
                        if (key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                        {
                            var parts = value.Split(' ', 2, StringSplitOptions.TrimEntries);
                            if (parts.Length == 2)
                            {
                                http.DefaultRequestHeaders.Authorization =
                                    new System.Net.Http.Headers.AuthenticationHeaderValue(parts[0], parts[1]);
                            }
                        }
                        else if (!key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                        {
                            http.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
                        }
                    }
                }

                // Use provided request template or default test payload
                string requestJson;
                if (request.RequestTemplate != null)
                {
                    var context = new TemplateContext(
                        ModelId: request.ModelId ?? "test-model",
                        Messages: new List<ChatMessagePayload>
                        {
                            new("user", "Respond with just 'OK' to confirm you are working.")
                        },
                        Temperature: 0.0,
                        MaxTokens: 10
                    );
                    // Template is stored as a JSON string value, deserialize it first
                    var templateString = request.RequestTemplate.RootElement.GetString()
                        ?? request.RequestTemplate.RootElement.GetRawText();
                    requestJson = TemplateSubstitutionService.Substitute(
                        templateString,
                        context
                    );
                }
                else
                {
                    // Default OpenAI-compatible test payload
                    var testPayload = new
                    {
                        model = request.ModelId ?? "test-model",
                        messages = new[]
                        {
                            new { role = "user", content = "Respond with just 'OK' to confirm you are working." }
                        },
                        max_tokens = 10,
                        temperature = 0.0
                    };
                    requestJson = JsonSerializer.Serialize(testPayload);
                }

                logger.LogInformation("Probing model at {Url}", request.Url);

                using var content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");
                using var response = await http.PostAsync(request.Url, content, ct);
                var responseBody = await response.Content.ReadAsStringAsync(ct);
                sw.Stop();

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Model probe failed: HTTP {Status}", (int)response.StatusCode);
                    return Results.Ok(new ProbeModelResponse(
                        Success: false,
                        Message: $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}",
                        ResponseMapping: null,
                        ResponseSample: responseBody,
                        ElapsedMs: sw.ElapsedMilliseconds
                    ));
                }

                // Auto-detect response mapping
                var detector = new ResponseMappingDetector();
                var mapping = detector.DetectMapping(responseBody);

                var mappingDto = new ResponseMappingDto(
                    ContentPath: mapping.ContentPath,
                    RolePath: mapping.RolePath,
                    ToolCallsPath: mapping.ToolCallsPath,
                    UsagePromptTokensPath: mapping.UsagePromptTokensPath,
                    UsageCompletionTokensPath: mapping.UsageCompletionTokensPath,
                    UsageTotalTokensPath: mapping.UsageTotalTokensPath,
                    AutoDetected: mapping.AutoDetected,
                    DetectedAt: mapping.DetectedAt
                );

                logger.LogInformation("Model probe succeeded in {Ms}ms. Detected format: {Format}",
                    sw.ElapsedMilliseconds,
                    mapping.AutoDetected ? "auto-detected" : "default");

                return Results.Ok(new ProbeModelResponse(
                    Success: true,
                    Message: $"Successfully probed model in {sw.ElapsedMilliseconds}ms. Response mapping auto-detected.",
                    ResponseMapping: mappingDto,
                    ResponseSample: responseBody,
                    ElapsedMs: sw.ElapsedMilliseconds
                ));
            }
            catch (TaskCanceledException ex)
            {
                sw.Stop();
                logger.LogWarning(ex, "Model probe timed out after {Ms}ms", sw.ElapsedMilliseconds);
                return Results.Ok(new ProbeModelResponse(
                    Success: false,
                    Message: $"Request timed out after {sw.ElapsedMilliseconds}ms",
                    ResponseMapping: null,
                    ResponseSample: null,
                    ElapsedMs: sw.ElapsedMilliseconds
                ));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                sw.Stop();
                logger.LogError(ex, "Model probe failed after {Ms}ms", sw.ElapsedMilliseconds);
                return Results.Ok(new ProbeModelResponse(
                    Success: false,
                    Message: $"Error: {ex.Message}",
                    ResponseMapping: null,
                    ResponseSample: null,
                    ElapsedMs: sw.ElapsedMilliseconds
                ));
            }
        });

        var users = secure.MapGroup("/users");

        users.MapGet(string.Empty, async (AdminUserStore userStore, CancellationToken ct) =>
        {
            var admins = await userStore.GetUsersAsync(ct);
            var dtos = admins.Select(ToDto).ToList();
            return Results.Ok(new { users = dtos });
        });

        users.MapPost(string.Empty, async (
            AdminCreateUserRequest request,
            AdminUserStore userStore,
            CancellationToken ct) =>
        {
            var username = request.Username?.Trim();
            if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
            {
                return Results.BadRequest(new { error = "Username must be at least 3 characters." });
            }

            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            {
                return Results.BadRequest(new { error = "Password must be at least 8 characters." });
            }

            try
            {
                var created = await userStore.CreateUserAsync(username, request.Password, request.IsAdmin, ct);
                return Results.Ok(ToDto(created));
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return Results.Conflict(new { error = "Username already exists." });
            }
        });

        users.MapPost("/{userId:guid}/password", async (
            Guid userId,
            AdminChangePasswordRequest request,
            AdminUserStore userStore,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            {
                return Results.BadRequest(new { error = "Password must be at least 8 characters." });
            }

            var updated = await userStore.TrySetPasswordAsync(userId, request.Password, ct);
            if (!updated)
            {
                return Results.NotFound();
            }

            return Results.NoContent();
        });

        users.MapPost("/{userId:guid}/admin", async (
            Guid userId,
            AdminSetAdminRequest request,
            AdminUserStore userStore,
            CancellationToken ct) =>
        {
            var user = await userStore.GetByIdAsync(userId, ct);
            if (user is null)
            {
                return Results.NotFound();
            }

            if (!request.IsAdmin && user.IsAdmin)
            {
                var adminCount = await userStore.CountAdminsAsync(ct);
                if (adminCount <= 1)
                {
                    return Results.BadRequest(new { error = "Cannot remove admin rights from the last admin user." });
                }
            }

            var updated = await userStore.TrySetAdminAsync(userId, request.IsAdmin, ct);
            if (!updated)
            {
                return Results.NotFound();
            }

            return Results.NoContent();
        });

        return admin;
    }

    private static AdminUserDto GetAuthenticatedUser(HttpContext context)
    {
        if (context.Items.TryGetValue(AdminAuthFilter.ContextItemKey, out var value) && value is AdminUser user)
        {
            return ToDto(user);
        }

        throw new InvalidOperationException("Admin user context missing.");
    }

    private static AdminUserDto ToDto(AdminUser user) => new(user.Id, user.Username, user.IsAdmin, user.CreatedAt, user.UpdatedAt);
}
