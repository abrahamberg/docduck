using Indexer;
using DocDuck.Providers.Ai;
using DocDuck.Providers.Configuration;
using DocDuck.Providers.Providers;
using Microsoft.Extensions.Options;
using Indexer.Options;
using Indexer.Services;
using Indexer.Services.TextExtraction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using NetEscapades.Configuration.Yaml;
using Npgsql;
using Pgvector;

var builder = Host.CreateApplicationBuilder(args);

// Add configuration sources: YAML and environment variables
builder.Configuration.Sources.Clear();
builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddYamlFile("appsettings.yaml", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables();

// Expand simple ${VAR} placeholders in the Database:ConnectionString value so
// appsettings.yaml can use the ${ENV_VAR} style without Npgsql receiving
// unresolved placeholders (which causes a cryptic parse error).
static string ExpandEnvPlaceholders(string? input)
{
    if (string.IsNullOrEmpty(input)) return string.Empty;

    return Regex.Replace(input, @"\$\{([^}]+)\}", m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? string.Empty);
}

var rawConn = builder.Configuration["Database:ConnectionString"];
if (!string.IsNullOrEmpty(rawConn) && rawConn.Contains("${"))
{
    var expanded = ExpandEnvPlaceholders(rawConn);
    // overwrite the configuration value with the expanded string so bindings get the real value
    builder.Configuration["Database:ConnectionString"] = expanded;
}

builder.Services.Configure<Indexer.Options.DbOptions>(
    builder.Configuration.GetSection("Database"));
builder.Services.Configure<ChunkingOptions>(
    builder.Configuration.GetSection("Chunking"));

// Register provider configuration infrastructure
builder.Services.AddSingleton(sp =>
{
    var dbOptions = sp.GetRequiredService<IOptions<Indexer.Options.DbOptions>>().Value;
    return new ProviderSchemaInitializer(dbOptions.ConnectionString, sp.GetRequiredService<ILogger<ProviderSchemaInitializer>>());
});

builder.Services.AddSingleton(sp =>
{
    var dbOptions = sp.GetRequiredService<IOptions<Indexer.Options.DbOptions>>().Value;
    return new ProviderSettingsStore(dbOptions.ConnectionString);
});

builder.Services.AddSingleton<ProviderFactory>();
builder.Services.AddSingleton<ProviderConfigurationService>();
builder.Services.AddSingleton<ProviderSettingsSeeder>();

builder.Services.AddSingleton(sp => new ProviderCatalog(sp.GetRequiredService<ProviderConfigurationService>()));

builder.Services.AddSingleton(sp =>
{
    var dbOptions = sp.GetRequiredService<IOptions<Indexer.Options.DbOptions>>().Value;
    return new AiProviderSettingsStore(dbOptions.ConnectionString);
});
builder.Services.AddSingleton<AiConfigurationService>();
builder.Services.AddSingleton<OpenAiSettingsSeeder>();
builder.Services.AddSingleton<IOptions<OpenAiOptions>, OpenAiOptionsProvider>();

// Register SDK-based OpenAI embeddings client
builder.Services.AddSingleton<OpenAiEmbeddingsClient>();

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

// Validate connection string early to provide a helpful error instead of Npgsql's cryptic parser error.
var dbOptions = host.Services.GetRequiredService<IOptions<Indexer.Options.DbOptions>>().Value;
if (string.IsNullOrWhiteSpace(dbOptions.ConnectionString))
{
    var tempLogger = host.Services.GetRequiredService<ILogger<Program>>();
    tempLogger.LogCritical("Database connection string is empty. Set the environment variable referenced by appsettings.yaml (DB_CONNECTION_STRING) or configure Database:ConnectionString in appsettings.yaml.");
    Environment.Exit(2);
}

var schemaInitializer = host.Services.GetRequiredService<ProviderSchemaInitializer>();
try
{
    await schemaInitializer.EnsureSchemaAsync();
}
catch (Exception ex)
{
    var tempLogger = host.Services.GetRequiredService<ILogger<Program>>();
    tempLogger.LogCritical(ex, "Failed to ensure provider schema. Connection string: {ConnPreview}", dbOptions.ConnectionString?.Length > 64 ? dbOptions.ConnectionString[..64] + "..." : dbOptions.ConnectionString);
    throw;
}

var seeder = host.Services.GetRequiredService<ProviderSettingsSeeder>();
await seeder.SeedFromEnvironmentAsync();

var providerConfigService = host.Services.GetRequiredService<ProviderConfigurationService>();
await providerConfigService.ReloadAsync();

var openAiSeeder = host.Services.GetRequiredService<OpenAiSettingsSeeder>();
await openAiSeeder.SeedFromEnvironmentAsync();

var aiConfigService = host.Services.GetRequiredService<AiConfigurationService>();
await aiConfigService.ReloadAsync();

// Log configuration status
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var providers = await host.Services.GetRequiredService<ProviderCatalog>().GetProvidersAsync();

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
