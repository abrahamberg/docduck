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
    options.BaseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") 
        ?? "https://api.openai.com/v1";
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
builder.Services.AddHttpClient<OpenAiClient>()
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(60);
    });

// Register services
builder.Services.AddSingleton<VectorSearchService>();

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
        
        return Results.Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            chunks = chunkCount,
            documents = docCount
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Health check failed");
        return Results.Problem("Service unhealthy");
    }
});

// Query endpoint - simple question answering
app.MapPost("/query", async (
    QueryRequest request,
    OpenAiClient openAiClient,
    VectorSearchService searchService,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest(new { error = "Question is required" });
    }

    try
    {
        logger.LogInformation("Processing query: {Question}", request.Question);

        // 1. Generate embedding for the question
        var questionEmbedding = await openAiClient.EmbedAsync(request.Question, ct);

        // 2. Search for similar chunks
        var sources = await searchService.SearchAsync(questionEmbedding, request.TopK, ct);

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

// Chat endpoint - conversation with history
app.MapPost("/chat", async (
    ChatRequest request,
    OpenAiClient openAiClient,
    VectorSearchService searchService,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "Message is required" });
    }

    try
    {
        logger.LogInformation("Processing chat message: {Message}", request.Message);

        // 1. Generate embedding for the current message
        var messageEmbedding = await openAiClient.EmbedAsync(request.Message, ct);

        // 2. Search for similar chunks
        var sources = await searchService.SearchAsync(messageEmbedding, request.TopK, ct);

        if (sources.Count == 0)
        {
            return Results.Ok(new ChatResponse(
                Answer: "I couldn't find any relevant information in the indexed documents.",
                Sources: new List<Source>(),
                TokensUsed: 0,
                History: request.History ?? new List<ChatMessage>()
            ));
        }

        // 3. Convert history to tuples
        var historyTuples = request.History?
            .Select(h => (h.Role, h.Content))
            .ToList();

        // 4. Generate answer with conversation history
        var contextChunks = sources.Select(s => s.Text).ToList();
        var (answer, tokensUsed) = await openAiClient.GenerateAnswerAsync(
            request.Message,
            contextChunks,
            historyTuples,
            ct);

        // 5. Build updated history
        var updatedHistory = new List<ChatMessage>(request.History ?? new List<ChatMessage>())
        {
            new ChatMessage("user", request.Message),
            new ChatMessage("assistant", answer)
        };

        // 6. Return response
        var response = new ChatResponse(
            Answer: answer,
            Sources: sources,
            TokensUsed: tokensUsed,
            History: updatedHistory
        );

        logger.LogInformation("Chat completed successfully ({Tokens} tokens)", tokensUsed);
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
    version = "1.0.0",
    endpoints = new[]
    {
        "GET /health - Health check",
        "POST /query - Simple question answering",
        "POST /chat - Conversational chat with history"
    }
}));

app.Run();

