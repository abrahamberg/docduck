using Api.Admin;
using Api.Models;
using Api.Options;
using Api.Services;
using DocDuck.Providers.Ai;
using DocDuck.Providers.Configuration;
using System.Text;
using System.IO;
using System.Text.Json;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
var builder = WebApplication.CreateBuilder(args);

var envConnectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
var configuredConnectionString = builder.Configuration["Database:ConnectionString"];
var dbConnectionString = !string.IsNullOrWhiteSpace(envConnectionString)
    ? envConnectionString
    : configuredConnectionString ?? string.Empty;

if (string.IsNullOrWhiteSpace(dbConnectionString))
{
    throw new InvalidOperationException("Database connection string is required. Set DB_CONNECTION_STRING or configure Database:ConnectionString in appsettings.");
}

builder.Services.Configure<DbOptions>(options =>
{
    options.ConnectionString = dbConnectionString;
});

var adminSecret = Environment.GetEnvironmentVariable("ADMIN_AUTH_SECRET") ?? builder.Configuration["Admin:Secret"];
if (string.IsNullOrWhiteSpace(adminSecret))
{
    throw new InvalidOperationException("Admin authentication secret is required. Set ADMIN_AUTH_SECRET or Admin:Secret in configuration.");
}

builder.Services.Configure<AdminAuthOptions>(options =>
{
    options.Secret = adminSecret;

    if (int.TryParse(Environment.GetEnvironmentVariable("ADMIN_TOKEN_LIFETIME_MINUTES"), out var envLifetime) && envLifetime > 0)
    {
        options.TokenLifetimeMinutes = envLifetime;
    }
    else if (int.TryParse(builder.Configuration["Admin:TokenLifetimeMinutes"], out var configLifetime) && configLifetime > 0)
    {
        options.TokenLifetimeMinutes = configLifetime;
    }
});

builder.Services.Configure<SearchOptions>(options =>
{
    if (int.TryParse(Environment.GetEnvironmentVariable("DEFAULT_TOP_K"), out var topK))
    {
        options.DefaultTopK = topK;
    }

    if (int.TryParse(Environment.GetEnvironmentVariable("MAX_TOP_K"), out var maxTopK))
    {
        options.MaxTopK = maxTopK;
    }

    if (int.TryParse(Environment.GetEnvironmentVariable("MAX_SEARCH_DEPTH"), out var maxDepth))
    {
        options.MaxSearchDepth = Math.Max(1, maxDepth);
    }

    if (int.TryParse(Environment.GetEnvironmentVariable("DEFAULT_SEARCH_DEPTH"), out var defaultDepth))
    {
        options.DefaultSearchDepth = Math.Clamp(defaultDepth, 1, options.MaxSearchDepth);
    }

    if (bool.TryParse(Environment.GetEnvironmentVariable("ENABLE_LEXICAL_SEARCH"), out var enableLexical))
    {
        options.EnableLexicalSearch = enableLexical;
    }

    if (double.TryParse(Environment.GetEnvironmentVariable("LEXICAL_SCORE_WEIGHT"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lexicalWeight))
    {
        options.LexicalScoreWeight = Math.Clamp(lexicalWeight, 0d, 1d);
    }

    if (int.TryParse(Environment.GetEnvironmentVariable("MAX_LEXICAL_RESULTS"), out var maxLexical))
    {
        options.MaxLexicalResults = Math.Max(1, maxLexical);
    }

    var lexicalConfig = Environment.GetEnvironmentVariable("LEXICAL_CONFIGURATION");
    if (!string.IsNullOrWhiteSpace(lexicalConfig))
    {
        options.LexicalConfiguration = lexicalConfig;
    }
});

builder.Services.AddSingleton(sp => new ProviderSchemaInitializer(dbConnectionString, sp.GetRequiredService<ILogger<ProviderSchemaInitializer>>()));
builder.Services.AddSingleton(new ProviderSettingsStore(dbConnectionString));
builder.Services.AddSingleton<ProviderFactory>();
builder.Services.AddSingleton<ProviderConfigurationService>();
builder.Services.AddSingleton<ProviderSettingsSeeder>();

builder.Services.AddSingleton(new AiProviderConfigurationStore(dbConnectionString));
builder.Services.AddSingleton<ModelAgnosticAiService>();
builder.Services.AddSingleton<AiConfigurationSeeder>();

builder.Services.AddSingleton(sp => new AdminUserStore(dbConnectionString, sp.GetRequiredService<ILogger<AdminUserStore>>()));
builder.Services.AddSingleton<AdminAuthService>();
builder.Services.AddScoped<AdminAuthFilter>();

builder.Services.AddSingleton<VectorSearchService>();
builder.Services.AddSingleton<ChatService>();

// Add CORS for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    var schemaInitializer = services.GetRequiredService<ProviderSchemaInitializer>();
    await schemaInitializer.EnsureSchemaAsync();

    var providerSeeder = services.GetRequiredService<ProviderSettingsSeeder>();
    await providerSeeder.SeedFromEnvironmentAsync();

    var providerConfig = services.GetRequiredService<ProviderConfigurationService>();
    await providerConfig.ReloadAsync();
    var snapshot = await providerConfig.GetSnapshotAsync();

    var aiSeeder = services.GetRequiredService<AiConfigurationSeeder>();
    await aiSeeder.SeedFromEnvironmentAsync();

    var bootstrapAiService = services.GetRequiredService<ModelAgnosticAiService>();
    await bootstrapAiService.ReloadAsync();
    var bootstrapAiConfig = await bootstrapAiService.GetConfigurationAsync();

    var adminUserStore = services.GetRequiredService<AdminUserStore>();
    await adminUserStore.EnsureDefaultAdminAsync(CancellationToken.None);

    var bootstrapLogger = services.GetRequiredService<ILogger<Program>>();
    bootstrapLogger.LogInformation("Provider configurations loaded: {Count}", snapshot.Settings.Count);
    bootstrapLogger.LogInformation("AI provider configured: {Configured}", bootstrapAiConfig is { Enabled: true });
}

// Enable CORS
app.UseCors();

app.MapAdminEndpoints();

// Grab logger from app so middleware can use it
var logger = app.Logger;
var aiService = app.Services.GetRequiredService<ModelAgnosticAiService>();
var aiConfig = await aiService.GetConfigurationAsync();
var aiConfigured = aiConfig is { Enabled: true };

// Global exception logging middleware: captures unhandled exceptions and logs request details
app.Use(async (context, next) =>
{
    try
    {
        // Allow downstream to read the request body multiple times
        context.Request.EnableBuffering();
        await next();
    }
    catch (Exception ex)
    {
        // Try to read request body for debugging (reset position afterwards)
        string body = string.Empty;
        try
        {
            context.Request.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            body = await reader.ReadToEndAsync();
            context.Request.Body.Seek(0, SeekOrigin.Begin);
        }
        catch (Exception readEx)
        {
            logger.LogDebug(readEx, "Failed to read request body for error logging");
        }

        logger.LogError(ex, "Unhandled exception processing {Method} {Path}. Request body: {Body}", context.Request.Method, context.Request.Path, body);
        throw;
    }
});

// Log configuration status
logger.LogInformation("DocDuck Query API starting...");
logger.LogInformation("AI provider configured: {Status}", aiConfigured ? "Enabled" : "Disabled/Missing");
logger.LogInformation("DB Connection configured: {Configured}", !string.IsNullOrWhiteSpace(dbConnectionString));

// Health check endpoint
app.MapGet("/health", async (VectorSearchService searchService, ModelAgnosticAiService aiSvc, CancellationToken ct) =>
{
    try
    {
        var chunkCount = await searchService.GetChunkCountAsync(ct);
        var docCount = await searchService.GetDocumentCountAsync(ct);

        var config = await aiSvc.GetConfigurationAsync(ct);
        var aiKeyPresent = config is { Enabled: true, EmbeddingModel: not null } && !string.IsNullOrWhiteSpace(config.EmbeddingModel.ApiKey);
        var dbConnectionPresent = !string.IsNullOrWhiteSpace(dbConnectionString);

        return Results.Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            chunks = chunkCount,
            documents = docCount,
            aiKeyPresent,
            dbConnectionPresent
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Health check failed");
        return Results.Problem("Service unhealthy");
    }
});

// Get active providers endpoint
app.MapGet("/providers", async (VectorSearchService searchService, CancellationToken ct) =>
{
    try
    {
        var providers = await searchService.GetProvidersAsync(ct);
        
        return Results.Ok(new
        {
            providers,
            count = providers.Count,
            timestamp = DateTime.UtcNow
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to retrieve providers");
        return Results.Problem("An error occurred retrieving providers");
    }
});

// Unified Query endpoint - adaptive intelligence based on search depth
// - depth=1: Simple mode (single search + answer, no refinement)
// - depth=2-5: Smart mode (multi-attempt with query refinement and answerability checks)
// - streamSteps=true: Stream intermediate thinking via SSE
// - streamSteps=false: Return final result only
app.MapPost("/query", async (
    HttpContext httpContext,
    QueryRequest request,
    ModelAgnosticAiService aiSvc,
    VectorSearchService searchService,
    ChatService chatService,
    IOptions<SearchOptions> searchOptions,
    ILogger<Program> endpointLogger,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest(new { error = "Question is required" });
    }

    var depth = Math.Clamp(request.SearchDepth ?? searchOptions.Value.DefaultSearchDepth, 1, searchOptions.Value.MaxSearchDepth);
    
    logger.LogInformation("Processing query: {Question} (Depth: {Depth}, Stream: {Stream}, Provider: {Type}/{Name})", 
        request.Question, depth, request.StreamSteps, request.ProviderType ?? "all", request.ProviderName ?? "all");

    try
    {
        // Depth=1: Use simple single-shot flow (fast, no refinement)
        if (depth == 1)
        {
            var questionEmbedding = await aiSvc.EmbedAsync(request.Question, ct);
            var sources = await searchService.SearchAsync(
                questionEmbedding, 
                request.Question,
                request.TopK, 
                request.ProviderType, 
                request.ProviderName, 
                depth,
                ct);

            if (sources.Count == 0)
            {
                return Results.Ok(new QueryResponse(
                    Answer: "I couldn't find any relevant information in the indexed documents.",
                    Sources: new List<Source>(),
                    TokensUsed: 0
                ));
            }

            // Build simple answer using CompleteChatAsync
            var contextChunks = sources.Select(s => s.Text).ToList();
            var contextText = string.Join("\n\n", contextChunks.Select((chunk, i) => $"[{i + 1}] {chunk}"));
            
            var systemPrompt = "You are a helpful assistant. Answer the user's question based on the provided context. If the context doesn't contain relevant information, say so.";
            var userPrompt = $"Context:\n{contextText}\n\nQuestion: {request.Question}";
            
            var messages = new List<ChatMessagePayload> { new("system", systemPrompt) };
            if (request.History != null)
            {
                messages.AddRange(request.History.Select(h => new ChatMessagePayload(h.Role, h.Content)));
            }
            messages.Add(new ChatMessagePayload("user", userPrompt));
            
            var result = await aiSvc.CompleteChatAsync(
                messages,
                TaskComplexity.Simple,
                null, // default strategy
                null, // default options
                ct);

            var response = new QueryResponse(
                Answer: result.Content,
                Sources: sources,
                TokensUsed: result.TotalTokens
            );

            logger.LogInformation("Simple query completed ({Tokens} tokens)", result.TotalTokens);
            return Results.Ok(response);
        }

        // Depth > 1: Use intelligent multi-attempt flow via ChatService
        var chatRequest = new ChatRequest(
            Message: request.Question,
            History: request.History,
            TopK: request.TopK,
            ProviderType: request.ProviderType,
            ProviderName: request.ProviderName,
            StreamSteps: request.StreamSteps,
            SearchDepth: depth
        );

        if (request.StreamSteps)
        {
            // Streaming mode: Send SSE events
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers["X-Accel-Buffering"] = "no";

            var streamJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

            async Task WriteUpdateAsync(ChatStreamUpdate update)
            {
                var payload = JsonSerializer.Serialize(update, streamJsonOptions);
                logger.LogDebug("Sending stream update: {Type}, payload length: {Length}", update.Type, payload.Length);
                if (update.Type == "final" && update.Final != null)
                {
                    logger.LogDebug("Final answer preview: {Answer}", update.Final.Answer.Length > 100 ? update.Final.Answer.Substring(0, 100) + "..." : update.Final.Answer);
                }
                await httpContext.Response.WriteAsync($"data: {payload}\n\n", ct);
                await httpContext.Response.Body.FlushAsync(ct);
            }

            await chatService.ProcessAsync(chatRequest, WriteUpdateAsync, ct);
            return Results.Empty;
        }
        else
        {
            // Non-streaming mode: Return complete response
            var chatResponse = await chatService.ProcessAsync(chatRequest, null, ct);
            var queryResponse = QueryResponse.FromChatResponse(chatResponse);
            
            logger.LogInformation("Smart query completed ({Tokens} tokens)", queryResponse.TokensUsed);
            return Results.Ok(queryResponse);
        }
    }
    catch (Exception ex)
    {
        endpointLogger.LogError(ex, "Error processing query");

        if (request.StreamSteps && httpContext.Response.HasStarted)
        {
            var streamJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var errorUpdate = new ChatStreamUpdate(
                Type: "error",
                Message: "An error occurred processing your query.",
                Files: null,
                Final: null);
            var payload = JsonSerializer.Serialize(errorUpdate, streamJsonOptions);
            await httpContext.Response.WriteAsync($"data: {payload}\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);
            return Results.Empty;
        }

        return Results.Problem("An error occurred processing your query");
    }
});

// Lightweight document search endpoint: return up to 5 most relevant documents (grouped by doc_id)
app.MapPost("/docsearch", async (
    QueryRequest request,
    ModelAgnosticAiService aiSvc,
    VectorSearchService searchService,
    IOptions<SearchOptions> searchOptions,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest(new { error = "Question/query is required" });
    }

    try
    {
        var depth = Math.Clamp(request.SearchDepth ?? searchOptions.Value.DefaultSearchDepth, 1, searchOptions.Value.MaxSearchDepth);
        // Create embedding for the query
        var qEmbedding = await aiSvc.EmbedAsync(request.Question, ct);

        // Fetch chunks (limit a bit higher to allow grouping) - respect TopK if provided but cap to 100
        var fetchTopK = Math.Min(request.TopK ?? 20, 100);
        var chunks = await searchService.SearchAsync(qEmbedding, request.Question, fetchTopK, request.ProviderType, request.ProviderName, depth, ct);

        // Group by document and pick the best (smallest) distance per document
        var docs = chunks
            .GroupBy(c => c.DocId)
            .Select(g => new {
                DocId = g.Key,
                Filename = g.First().Filename,
                ProviderType = g.First().ProviderType,
                ProviderName = g.First().ProviderName,
                BestDistance = g.Min(x => x.Distance)
            })
            .OrderBy(x => x.BestDistance)
            .Take(5)
                .Select(x => {
                    // pick the first chunk text for the document to show as snippet
                    var chunkText = chunks.FirstOrDefault(c => c.DocId == x.DocId)?.Text ?? string.Empty;
                    return new Api.Models.DocumentResult(
                        DocId: x.DocId,
                        Filename: x.Filename,
                        Address: $"{x.ProviderType}/{x.ProviderName}:{x.Filename}",
                        Text: chunkText,
                        Distance: x.BestDistance,
                        ProviderType: x.ProviderType,
                        ProviderName: x.ProviderName
                    );
                })
            .ToList();

        return Results.Ok(new { query = request.Question, count = docs.Count, results = docs });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error processing docsearch");
        return Results.Problem("An error occurred processing your document search");
    }
});

// Root endpoint - API info
app.MapGet("/", () => Results.Ok(new
{
    name = "DocDuck Query API",
    version = "3.0.0",
    endpoints = new[]
    {
        "GET /health - Health check",
        "GET /providers - List active document providers",
        "POST /query - Intelligent Q&A with adaptive depth (1-5) and optional streaming",
        "POST /docsearch - Document-level search (returns top 5 matching documents)"
    }
}));

await app.RunAsync();