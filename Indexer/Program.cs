using Indexer;
using Indexer.Options;
using Indexer.Providers;
using Indexer.Services;
using Indexer.Services.TextExtraction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetEscapades.Configuration.Yaml;

var builder = Host.CreateApplicationBuilder(args);

// Add configuration sources: YAML and environment variables
builder.Configuration.Sources.Clear();
builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddYamlFile("appsettings.yaml", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables();

// Bind configuration sections
builder.Services.Configure<ProvidersConfiguration>(
    builder.Configuration.GetSection(ProvidersConfiguration.SectionName));
builder.Services.Configure<OpenAiOptions>(
    builder.Configuration.GetSection("OpenAi"));
builder.Services.Configure<DbOptions>(
    builder.Configuration.GetSection("Database"));
builder.Services.Configure<ChunkingOptions>(
    builder.Configuration.GetSection("Chunking"));

// Register document providers based on configuration
var providersConfig = builder.Configuration
    .GetSection(ProvidersConfiguration.SectionName)
    .Get<ProvidersConfiguration>();

if (providersConfig != null)
{
    // OneDrive Provider
    if (providersConfig.OneDrive?.Enabled == true)
    {
        builder.Services.AddSingleton<IDocumentProvider>(sp =>
            new OneDriveProvider(
                providersConfig.OneDrive,
                sp.GetRequiredService<ILogger<OneDriveProvider>>()));
    }

    // Local Provider
    if (providersConfig.Local?.Enabled == true)
    {
        builder.Services.AddSingleton<IDocumentProvider>(sp =>
            new LocalProvider(
                providersConfig.Local,
                sp.GetRequiredService<ILogger<LocalProvider>>()));
    }

    // S3 Provider
    if (providersConfig.S3?.Enabled == true)
    {
        builder.Services.AddSingleton<IDocumentProvider>(sp =>
            new S3Provider(
                providersConfig.S3,
                sp.GetRequiredService<ILogger<S3Provider>>()));
    }
}

// Register HttpClient for OpenAI with timeout
builder.Services.AddHttpClient<OpenAiEmbeddingsClient>()
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    });

// Register text extraction services
builder.Services.AddSingleton<ITextExtractor, DocxTextExtractor>();
builder.Services.AddSingleton<ITextExtractor, PlainTextExtractor>();
builder.Services.AddSingleton<ITextExtractor, PdfTextExtractor>();
builder.Services.AddSingleton<TextExtractionService>();

// Register core services
builder.Services.AddSingleton<TextChunker>();
builder.Services.AddSingleton<VectorRepository>();
builder.Services.AddSingleton<MultiProviderIndexerService>();

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

var host = builder.Build();

// Log configuration status
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var providers = host.Services.GetServices<IDocumentProvider>().ToList();

logger.LogInformation("DocDuck Multi-Provider Indexer");
logger.LogInformation("Providers registered: {Count}", providers.Count);

foreach (var provider in providers)
{
    logger.LogInformation("  → {Type}/{Name} (Enabled: {Enabled})",
        provider.ProviderType, provider.ProviderName, provider.IsEnabled);
}

// Run the indexer service
var indexer = host.Services.GetRequiredService<MultiProviderIndexerService>();

using var cts = new CancellationTokenSource();

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
