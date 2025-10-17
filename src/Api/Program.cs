using Api.Admin;
using Api.Models;
using Api.Options;
using Api.Services;
using DocDuck.Providers.Ai;
using DocDuck.Providers.Configuration;
using System.Text;
using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
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
});

builder.Services.AddSingleton(sp => new ProviderSchemaInitializer(dbConnectionString, sp.GetRequiredService<ILogger<ProviderSchemaInitializer>>()));
builder.Services.AddSingleton(new ProviderSettingsStore(dbConnectionString));
builder.Services.AddSingleton<ProviderFactory>();
builder.Services.AddSingleton<ProviderConfigurationService>();
builder.Services.AddSingleton<ProviderSettingsSeeder>();

builder.Services.AddSingleton(new AiProviderSettingsStore(dbConnectionString));
builder.Services.AddSingleton<AiConfigurationService>();
builder.Services.AddSingleton<OpenAiSettingsSeeder>();

builder.Services.AddSingleton(sp => new AdminUserStore(dbConnectionString, sp.GetRequiredService<ILogger<AdminUserStore>>()));
builder.Services.AddSingleton<AdminAuthService>();
builder.Services.AddScoped<AdminAuthFilter>();

builder.Services.AddSingleton<OpenAiSdkService>();

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

    var openAiSeeder = services.GetRequiredService<OpenAiSettingsSeeder>();
    await openAiSeeder.SeedFromEnvironmentAsync();

    var aiConfig = services.GetRequiredService<AiConfigurationService>();
    await aiConfig.ReloadAsync();
    var openAiSettings = await aiConfig.GetOpenAiAsync();

    var adminUserStore = services.GetRequiredService<AdminUserStore>();
    await adminUserStore.EnsureDefaultAdminAsync(CancellationToken.None);

    var bootstrapLogger = services.GetRequiredService<ILogger<Program>>();
    bootstrapLogger.LogInformation("Provider configurations loaded: {Count}", snapshot.Settings.Count);
    bootstrapLogger.LogInformation("OpenAI settings present: {Configured}", openAiSettings is not null);
    bootstrapLogger.LogInformation("Admin default user ensured.");
}

// Enable CORS
app.UseCors();

app.MapAdminEndpoints();

// Grab logger from app so middleware can use it
var logger = app.Logger;
var aiConfiguration = app.Services.GetRequiredService<AiConfigurationService>();
var openAiConfigured = (await aiConfiguration.GetOpenAiAsync())?.Enabled == true;

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
logger.LogInformation("OpenAI provider configured: {Status}", openAiConfigured ? "Enabled" : "Disabled/Missing");
logger.LogInformation("DB Connection configured: {Configured}", !string.IsNullOrWhiteSpace(dbConnectionString));

// Health check endpoint
app.MapGet("/health", async (VectorSearchService searchService, AiConfigurationService aiService, CancellationToken ct) =>
{
    try
    {
        var chunkCount = await searchService.GetChunkCountAsync(ct);
        var docCount = await searchService.GetDocumentCountAsync(ct);

        var aiSettings = await aiService.GetOpenAiAsync(ct);
        var openAiKeyPresent = aiSettings is { Enabled: true } && !string.IsNullOrWhiteSpace(aiSettings.ApiKey);
        var dbConnectionPresent = !string.IsNullOrWhiteSpace(dbConnectionString);

        return Results.Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            chunks = chunkCount,
            documents = docCount,
            openAiKeyPresent,
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

// Query endpoint - simple question answering with optional provider filtering
app.MapPost("/query", async (
    QueryRequest request,
    OpenAiSdkService openAiClient,
    VectorSearchService searchService,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest(new { error = "Question is required" });
    }

    try
    {
        logger.LogInformation("Processing query: {Question} (Provider: {Type}/{Name})", 
            request.Question, request.ProviderType ?? "all", request.ProviderName ?? "all");

        // 1. Generate embedding for the question
    var questionEmbedding = await openAiClient.EmbedAsync(request.Question, ct);

        // 2. Search for similar chunks with optional provider filter
        var sources = await searchService.SearchAsync(
            questionEmbedding, 
            request.TopK, 
            request.ProviderType, 
            request.ProviderName, 
            ct);

        if (sources.Count == 0)
        {
            return Results.Ok(new QueryResponse(
                Answer: "I couldn't find any relevant information in the indexed documents.",
                Sources: new List<Source>(),
                TokensUsed: 0
            ));
        }

        // 3. Generate answer using retrieved context
        var contextChunks = sources.Select(s => s.Text).ToList();
        var (answer, tokensUsed) = await openAiClient.GenerateAnswerAsync(
            request.Question,
            contextChunks,
            null,
            ct);

        // 4. Return response with citations
        var response = new QueryResponse(
            Answer: answer,
            Sources: sources,
            TokensUsed: tokensUsed
        );

        logger.LogInformation("Query completed successfully ({Tokens} tokens)", tokensUsed);
        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error processing query");
        return Results.Problem("An error occurred processing your query");
    }
});

// Lightweight document search endpoint: return up to 5 most relevant documents (grouped by doc_id)
app.MapPost("/docsearch", async (
    QueryRequest request,
    OpenAiSdkService openAiClient,
    VectorSearchService searchService,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest(new { error = "Question/query is required" });
    }

    try
    {
        // Create embedding for the query
        var qEmbedding = await openAiClient.EmbedAsync(request.Question, ct);

        // Fetch chunks (limit a bit higher to allow grouping) - respect TopK if provided but cap to 100
        var fetchTopK = Math.Min(request.TopK ?? 20, 100);
        var chunks = await searchService.SearchAsync(qEmbedding, fetchTopK, request.ProviderType, request.ProviderName, ct);

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

// Chat endpoint - conversation with history and optional provider filtering
var streamJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

app.MapPost("/chat", async (
    HttpContext httpContext,
    ChatRequest request,
    ChatService chatService,
    ILogger<Program> endpointLogger,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "Message is required" });
    }

    try
    {
        if (request.StreamSteps)
        {
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers["X-Accel-Buffering"] = "no";

            async Task WriteUpdateAsync(ChatStreamUpdate update)
            {
                var payload = JsonSerializer.Serialize(update, streamJsonOptions);
                await httpContext.Response.WriteAsync($"data: {payload}\n\n", ct);
                await httpContext.Response.Body.FlushAsync(ct);
            }

            logger.LogInformation("Processing chat message (streaming): {Message}", request.Message);
            await chatService.ProcessAsync(request, WriteUpdateAsync, ct);
            return Results.Empty;
        }

        logger.LogInformation("Processing chat message (batched): {Message}", request.Message);
        var response = await chatService.ProcessAsync(request, null, ct);
        logger.LogInformation("Chat completed ({Tokens} tokens)", response.TokensUsed);
        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        endpointLogger.LogError(ex, "Error processing chat message");

        if (request.StreamSteps)
        {
            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
                httpContext.Response.ContentType = "text/event-stream";
                httpContext.Response.Headers.CacheControl = "no-cache";
                httpContext.Response.Headers["X-Accel-Buffering"] = "no";
            }

            var errorUpdate = new ChatStreamUpdate(
                Type: "error",
                Message: "An error occurred processing your message.",
                Files: null,
                Final: null);
            var payload = JsonSerializer.Serialize(errorUpdate, streamJsonOptions);
            await httpContext.Response.WriteAsync($"data: {payload}\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);
            return Results.Empty;
        }

        return Results.Problem("An error occurred processing your message");
    }
});

// Root endpoint - API info
app.MapGet("/", () => Results.Ok(new
{
    name = "DocDuck Query API",
    version = "2.0.0",
    endpoints = new[]
    {
        "GET /health - Health check",
        "GET /providers - List active document providers",
        "POST /query - Question answering with optional provider filtering",
        "POST /chat - Conversational chat with optional provider filtering"
    }
}));

await app.RunAsync();