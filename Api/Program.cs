using Api.Models;
using Api.Options;
using Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure options from environment variables
builder.Services.Configure<DbOptions>(options =>
{
    options.ConnectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") 
        ?? string.Empty;
});

builder.Services.Configure<OpenAiOptions>(options =>
{
    options.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") 
        ?? string.Empty;
    // Ensure BaseUrl ends with a trailing slash so relative paths like "/chat/completions"
    // combine to https://api.openai.com/v1/chat/completions correctly.
    options.BaseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") 
        ?? "https://api.openai.com/v1/";
    options.EmbedModel = Environment.GetEnvironmentVariable("OPENAI_EMBED_MODEL") 
        ?? "text-embedding-3-small";
    options.ChatModel = Environment.GetEnvironmentVariable("OPENAI_CHAT_MODEL") 
        ?? "gpt-4o-mini";
    
    if (int.TryParse(Environment.GetEnvironmentVariable("OPENAI_MAX_TOKENS"), out var maxTokens))
    {
        options.MaxTokens = maxTokens;
    }
    
    if (double.TryParse(Environment.GetEnvironmentVariable("OPENAI_TEMPERATURE"), out var temp))
    {
        options.Temperature = temp;
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

// Register HttpClient for OpenAI with timeout
// Register OpenAI SDK-based service. The wrapper will read OPENAI_API_KEY at runtime.
builder.Services.AddSingleton<Api.Services.OpenAiSdkService>();

// Register services
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

// Enable CORS
app.UseCors();

// Log configuration status
var logger = app.Logger;
logger.LogInformation("DocDuck Query API starting...");
logger.LogInformation("OpenAI API Key: {Status}", 
    string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY")) ? "Missing" : "Present");
logger.LogInformation("DB Connection: {Status}", 
    string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")) ? "Missing" : "Present");

// Health check endpoint
app.MapGet("/health", async (VectorSearchService searchService) =>
{
    try
    {
        var chunkCount = await searchService.GetChunkCountAsync();
        var docCount = await searchService.GetDocumentCountAsync();

        var openAiKeyPresent = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        var dbConnectionPresent = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DB_CONNECTION_STRING"));

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
    Api.Services.OpenAiSdkService openAiClient,
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
    Api.Services.OpenAiSdkService openAiClient,
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
app.MapPost("/chat", async (
    ChatRequest request,
    ChatService chatService,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "Message is required" });
    }

    try
    {
        logger.LogInformation("Processing chat message (iterative): {Message}", request.Message);
        var response = await chatService.ProcessAsync(request, ct);
        logger.LogInformation("Chat completed ({Tokens} tokens)", response.TokensUsed);
        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error processing chat message");
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