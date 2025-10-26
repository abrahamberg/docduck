using Api.Admin;
using Api.Handlers;
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
builder.Services.AddSingleton<IModelAgnosticAiService>(sp => sp.GetRequiredService<ModelAgnosticAiService>());
builder.Services.AddSingleton<AiConfigurationSeeder>();

builder.Services.AddSingleton(sp => new AdminUserStore(dbConnectionString, sp.GetRequiredService<ILogger<AdminUserStore>>()));
builder.Services.AddSingleton<AdminAuthService>();
builder.Services.AddScoped<AdminAuthFilter>();

builder.Services.AddSingleton<VectorSearchService>();
builder.Services.AddSingleton<IVectorSearchService>(sp => sp.GetRequiredService<VectorSearchService>());
builder.Services.AddSingleton<ChatService>();
builder.Services.AddSingleton<IChatService>(sp => sp.GetRequiredService<ChatService>());
builder.Services.AddSingleton<QueryHandler>();

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

    var bootstrapAiService = services.GetRequiredService<IModelAgnosticAiService>();
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
var aiService = app.Services.GetRequiredService<IModelAgnosticAiService>();
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
app.MapGet("/health", async (IVectorSearchService searchService, IModelAgnosticAiService aiSvc, CancellationToken ct) =>
{
    return await GetHealthCheckAsync(searchService, aiSvc, dbConnectionString, logger, ct);
});

// Get active providers endpoint
app.MapGet("/providers", async (IVectorSearchService searchService, CancellationToken ct) =>
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
    QueryHandler queryHandler,
    CancellationToken ct) =>
{
    return await queryHandler.HandleQueryAsync(httpContext, request, ct);
});

// Lightweight document search endpoint: return up to 5 most relevant documents (grouped by doc_id)
app.MapPost("/docsearch", async (
    QueryRequest request,
    IModelAgnosticAiService aiSvc,
    IVectorSearchService searchService,
    IOptions<SearchOptions> searchOptions,
    CancellationToken ct) =>
{
    return await ExecuteDocSearchAsync(request, aiSvc, searchService, searchOptions.Value, logger, ct);
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

static async Task<IResult> GetHealthCheckAsync(
    IVectorSearchService searchService,
    IModelAgnosticAiService aiSvc,
    string dbConnectionString,
    ILogger logger,
    CancellationToken ct)
{
    try
    {
        var chunkCount = await searchService.GetChunkCountAsync(ct);
        var docCount = await searchService.GetDocumentCountAsync(ct);

        var config = await aiSvc.GetConfigurationAsync(ct);
        var aiKeyPresent = IsAiKeyPresent(config);
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
}

static bool IsAiKeyPresent(AiProviderConfiguration? config)
{
    return config is { Enabled: true, EmbeddingModel: not null }
        && config.EmbeddingModel.Headers.TryGetValue("Authorization", out var authHeader)
        && !string.IsNullOrWhiteSpace(authHeader);
}

static async Task<IResult> ExecuteDocSearchAsync(
    QueryRequest request,
    IModelAgnosticAiService aiSvc,
    IVectorSearchService searchService,
    SearchOptions searchOptions,
    ILogger logger,
    CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest(new { error = "Question/query is required" });
    }

    try
    {
        var depth = Math.Clamp(request.SearchDepth ?? searchOptions.DefaultSearchDepth, 1, searchOptions.MaxSearchDepth);
        var qEmbedding = await aiSvc.EmbedAsync(request.Question, ct);

        var fetchTopK = Math.Min(request.TopK ?? 20, 100);
        var chunks = await searchService.SearchAsync(qEmbedding, request.Question, fetchTopK, request.ProviderType, request.ProviderName, depth, ct);

        var docs = GroupChunksByDocument(chunks);

        return Results.Ok(new { query = request.Question, count = docs.Count, results = docs });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error processing docsearch");
        return Results.Problem("An error occurred processing your document search");
    }
}

static List<Api.Models.DocumentResult> GroupChunksByDocument(List<Source> chunks)
{
    return chunks
        .GroupBy(c => c.DocId)
        .Select(g => new
        {
            DocId = g.Key,
            Filename = g.First().Filename,
            ProviderType = g.First().ProviderType,
            ProviderName = g.First().ProviderName,
            BestDistance = g.Min(x => x.Distance)
        })
        .OrderBy(x => x.BestDistance)
        .Take(5)
        .Select(x =>
        {
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
}

