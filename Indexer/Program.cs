using Indexer;
using Indexer.Options;
using Indexer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Configure options from environment variables
builder.Services.Configure<GraphOptions>(options =>
{
    options.AuthMode = Environment.GetEnvironmentVariable("GRAPH_AUTH_MODE") ?? "ClientSecret";
    options.AccountType = Environment.GetEnvironmentVariable("GRAPH_ACCOUNT_TYPE") ?? "business";
    
    // Client Secret auth (Business OneDrive - app-only)
    options.TenantId = Environment.GetEnvironmentVariable("GRAPH_TENANT_ID") ?? string.Empty;
    options.ClientId = Environment.GetEnvironmentVariable("GRAPH_CLIENT_ID") ?? string.Empty;
    options.ClientSecret = Environment.GetEnvironmentVariable("GRAPH_CLIENT_SECRET") ?? string.Empty;
    
    // Username/Password auth (Personal OneDrive or delegated)
    options.Username = Environment.GetEnvironmentVariable("GRAPH_USERNAME");
    options.Password = Environment.GetEnvironmentVariable("GRAPH_PASSWORD");
    
    // OneDrive location
    options.SiteId = Environment.GetEnvironmentVariable("GRAPH_SITE_ID");
    options.DriveId = Environment.GetEnvironmentVariable("GRAPH_DRIVE_ID");
    options.FolderPath = Environment.GetEnvironmentVariable("GRAPH_FOLDER_PATH") ?? "/Shared Documents/Docs";
});

builder.Services.Configure<OpenAiOptions>(options =>
{
    options.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
    options.BaseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "https://api.openai.com/v1";
    options.EmbedModel = Environment.GetEnvironmentVariable("OPENAI_EMBED_MODEL") ?? "text-embedding-3-small";
    
    if (int.TryParse(Environment.GetEnvironmentVariable("BATCH_SIZE"), out var batchSize))
    {
        options.BatchSize = batchSize;
    }
});

builder.Services.Configure<DbOptions>(options =>
{
    options.ConnectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") ?? string.Empty;
});

builder.Services.Configure<ChunkingOptions>(options =>
{
    if (int.TryParse(Environment.GetEnvironmentVariable("CHUNK_SIZE"), out var chunkSize))
    {
        options.ChunkSize = chunkSize;
    }
    
    if (int.TryParse(Environment.GetEnvironmentVariable("CHUNK_OVERLAP"), out var overlap))
    {
        options.ChunkOverlap = overlap;
    }
    
    if (int.TryParse(Environment.GetEnvironmentVariable("MAX_FILES"), out var maxFiles))
    {
        options.MaxFiles = maxFiles;
    }
});

// Register HttpClient for OpenAI with timeout
builder.Services.AddHttpClient<OpenAiEmbeddingsClient>()
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    });

// Register services
builder.Services.AddSingleton<GraphClient>();
builder.Services.AddSingleton<DocxExtractor>();
builder.Services.AddSingleton<TextChunker>();
builder.Services.AddSingleton<VectorRepository>();
builder.Services.AddSingleton<IndexerService>();

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

var host = builder.Build();

// Log configuration status (without exposing secrets)
var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Configuration loaded:");
logger.LogInformation("  Graph Tenant ID: {Status}", 
    string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GRAPH_TENANT_ID")) ? "Missing" : "Present");
logger.LogInformation("  OpenAI API Key: {Status}", 
    string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY")) ? "Missing" : "Present");
logger.LogInformation("  DB Connection: {Status}", 
    string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")) ? "Missing" : "Present");

// Run the indexer service
var indexer = host.Services.GetRequiredService<IndexerService>();
var cts = new CancellationTokenSource();

// Handle SIGTERM for K8s graceful shutdown
Console.CancelKeyPress += (s, e) =>
{
    logger.LogInformation("Cancellation requested (SIGTERM/SIGINT)");
    cts.Cancel();
    e.Cancel = true;
};

var exitCode = await indexer.ExecuteAsync(cts.Token);

logger.LogInformation("Indexer exiting with code {ExitCode}", exitCode);
Environment.Exit(exitCode);
