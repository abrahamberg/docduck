using Api.Models;
using Api.Options;
using Api.Services;
using DocDuck.Providers.Ai;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Api.Handlers;

/// <summary>
/// Handles the /query endpoint logic, reducing complexity in Program.cs.
/// Implements single responsibility: process user queries with appropriate depth and streaming.
/// </summary>
public sealed class QueryHandler
{
    private readonly ModelAgnosticAiService _aiService;
    private readonly VectorSearchService _searchService;
    private readonly ChatService _chatService;
    private readonly SearchOptions _searchOptions;
    private readonly ILogger<QueryHandler> _logger;

    public QueryHandler(
        ModelAgnosticAiService aiService,
        VectorSearchService searchService,
        ChatService chatService,
        IOptions<SearchOptions> searchOptions,
        ILogger<QueryHandler> logger)
    {
        _aiService = aiService;
        _searchService = searchService;
        _chatService = chatService;
        _searchOptions = searchOptions.Value;
        _logger = logger;
    }

    public async Task<IResult> HandleQueryAsync(
        HttpContext httpContext,
        QueryRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return Results.BadRequest(new { error = "Question is required" });
        }

        var depth = Math.Clamp(
            request.SearchDepth ?? _searchOptions.DefaultSearchDepth,
            1,
            _searchOptions.MaxSearchDepth);

        _logger.LogInformation(
            "Processing query: {Question} (Depth: {Depth}, Stream: {Stream}, Provider: {Type}/{Name})",
            request.Question, depth, request.StreamSteps, request.ProviderType ?? "all", request.ProviderName ?? "all");

        try
        {
            if (depth == 1)
            {
                return await HandleSimpleQueryAsync(request, ct);
            }

            return await HandleSmartQueryAsync(httpContext, request, depth, ct);
        }
        catch (Exception ex)
        {
            return HandleQueryError(httpContext, request, ex, ct);
        }
    }

    private async Task<IResult> HandleSimpleQueryAsync(QueryRequest request, CancellationToken ct)
    {
        var questionEmbedding = await _aiService.EmbedAsync(request.Question, ct);
        var sources = await _searchService.SearchAsync(
            questionEmbedding,
            request.Question,
            request.TopK,
            request.ProviderType,
            request.ProviderName,
            1, // depth = 1
            ct);

        if (sources.Count == 0)
        {
            return Results.Ok(new QueryResponse(
                Answer: "I couldn't find any relevant information in the indexed documents.",
                Sources: new List<Source>(),
                TokensUsed: 0
            ));
        }

        var result = await GenerateSimpleAnswerAsync(request, sources, ct);

        _logger.LogInformation("Simple query completed ({Tokens} tokens)", result.TotalTokens);

        return Results.Ok(new QueryResponse(
            Answer: result.Content,
            Sources: sources,
            TokensUsed: result.TotalTokens
        ));
    }

    private async Task<ChatCompletionResult> GenerateSimpleAnswerAsync(
        QueryRequest request,
        List<Source> sources,
        CancellationToken ct)
    {
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

        return await _aiService.CompleteChatAsync(
            messages,
            TaskComplexity.Simple,
            null, // default strategy
            null, // default options
            ct);
    }

    private async Task<IResult> HandleSmartQueryAsync(
        HttpContext httpContext,
        QueryRequest request,
        int depth,
        CancellationToken ct)
    {
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
            return await HandleStreamingQueryAsync(httpContext, chatRequest, ct);
        }

        return await HandleNonStreamingQueryAsync(chatRequest, ct);
    }

    private async Task<IResult> HandleStreamingQueryAsync(
        HttpContext httpContext,
        ChatRequest chatRequest,
        CancellationToken ct)
    {
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers["X-Accel-Buffering"] = "no";

        var streamJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        async Task WriteUpdateAsync(ChatStreamUpdate update)
        {
            var payload = JsonSerializer.Serialize(update, streamJsonOptions);
            _logger.LogDebug("Sending stream update: {Type}, payload length: {Length}", update.Type, payload.Length);

            if (update.Type == "final" && update.Final != null)
            {
                var preview = update.Final.Answer.Length > 100
                    ? update.Final.Answer.Substring(0, 100) + "..."
                    : update.Final.Answer;
                _logger.LogDebug("Final answer preview: {Answer}", preview);
            }

            await httpContext.Response.WriteAsync($"data: {payload}\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);
        }

        await _chatService.ProcessAsync(chatRequest, WriteUpdateAsync, ct);
        return Results.Empty;
    }

    private async Task<IResult> HandleNonStreamingQueryAsync(ChatRequest chatRequest, CancellationToken ct)
    {
        var chatResponse = await _chatService.ProcessAsync(chatRequest, null, ct);
        var queryResponse = QueryResponse.FromChatResponse(chatResponse);

        _logger.LogInformation("Smart query completed ({Tokens} tokens)", queryResponse.TokensUsed);

        return Results.Ok(queryResponse);
    }

    private IResult HandleQueryError(HttpContext httpContext, QueryRequest request, Exception ex, CancellationToken ct)
    {
        _logger.LogError(ex, "Error processing query");

        if (request.StreamSteps && httpContext.Response.HasStarted)
        {
            return WriteStreamingError(httpContext, ct);
        }

        return Results.Problem("An error occurred processing your query");
    }

    private IResult WriteStreamingError(HttpContext httpContext, CancellationToken ct)
    {
        var streamJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var errorUpdate = new ChatStreamUpdate(
            Type: "error",
            Message: "An error occurred processing your query.",
            Files: null,
            Final: null);

        var payload = JsonSerializer.Serialize(errorUpdate, streamJsonOptions);
        httpContext.Response.WriteAsync($"data: {payload}\n\n", ct).Wait();
        httpContext.Response.Body.FlushAsync(ct).Wait();

        return Results.Empty;
    }
}
