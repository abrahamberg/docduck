using Api.Models;
using Api.Options;
using Api.Services;
using Api.Services.Agents;
using Api.Services.Agents.Interfaces;
using Api.Services.Interfaces;
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
    private static readonly JsonSerializerOptions StreamJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IModelAgnosticAiService _aiService;
    private readonly IVectorSearchService _searchService;
    private readonly IChatService _chatService;
    private readonly ISearchOrchestrationService _orchestrationService;
    private readonly SearchOptions _searchOptions;
    private readonly ILogger<QueryHandler> _logger;

    public QueryHandler(
        IModelAgnosticAiService aiService,
        IVectorSearchService searchService,
        IChatService chatService,
        ISearchOrchestrationService orchestrationService,
        IOptions<SearchOptions> searchOptions,
        ILogger<QueryHandler> logger)
    {
        _aiService = aiService;
        _searchService = searchService;
        _chatService = chatService;
        _orchestrationService = orchestrationService;
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
                Sources: [],
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
        var searchRequest = new MultiStepSearchRequest(
            Query: request.Question,
            MaxSteps: depth,
            TopK: request.TopK,
            ProviderType: request.ProviderType,
            ProviderName: request.ProviderName
        );

        if (request.StreamSteps)
        {
            return await HandleStreamingOrchestrationAsync(httpContext, searchRequest, request.History, ct);
        }

        return await HandleNonStreamingOrchestrationAsync(searchRequest, request.History, ct);
    }

    private async Task<IResult> HandleStreamingOrchestrationAsync(
        HttpContext httpContext,
        MultiStepSearchRequest searchRequest,
        List<ChatMessage>? history,
        CancellationToken ct)
    {
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers["X-Accel-Buffering"] = "no";

        // Execute search with streaming thinking steps
        var response = await _orchestrationService.ExecuteSearchAsync(
            searchRequest,
            async step =>
            {
                var update = new ChatStreamUpdate(
                    Type: "step",
                    Message: step,
                    Files: null,
                    Final: null
                );
                var payload = JsonSerializer.Serialize(update, StreamJsonOptions);
                await httpContext.Response.WriteAsync($"data: {payload}\n\n", ct);
                await httpContext.Response.Body.FlushAsync(ct);
            },
            ct);

        // Convert to ChatResponse format for compatibility
        // Each SearchFinding represents ONE document with aggregated chunks
        // Return one Source per document, using the representative chunk with best distance
        // Sort by document strength (includes vector, keywords, filename, context, etc.)
        var sources = response.FinalFindings
            .OrderByDescending(f => f.Strength) // Sort by strength first (best match)
            .ThenBy(f => f.Chunks.Min(c => c.Distance)) // Then by best distance
            .Select(f =>
            {
                // Get the chunk with the best (lowest) distance as representative
                var bestChunk = f.Chunks.MinBy(c => c.Distance) ?? f.Chunks.First();

                // Combine all chunk texts with separators for full context
                var combinedText = string.Join("\n\n[...]\n\n", f.Chunks.Select(c => c.Text));

                return new Source(
                    DocId: f.DocId,
                    Filename: f.Filename,
                    ChunkNum: bestChunk.ChunkNum, // Representative chunk number
                    Text: combinedText, // All chunks combined
                    Distance: bestChunk.Distance, // Best distance
                    Citation: $"[{f.ProviderType}/{f.ProviderName}:{f.Filename}#chunk{bestChunk.ChunkNum}]",
                    ProviderType: f.ProviderType,
                    ProviderName: f.ProviderName
                );
            })
            .ToList();

        var answer = GenerateAnswerFromFindings(response.FinalFindings);

        var chatResponse = new ChatResponse(
            Answer: answer,
            Steps: response.ThinkingSteps,
            Files: [],
            Sources: sources,
            TokensUsed: 0,
            History: history ?? [],
            ModelUsage: null
        );

        var finalUpdate = new ChatStreamUpdate(
            Type: "final",
            Message: null,
            Files: null,
            Final: chatResponse
        );

        var finalPayload = JsonSerializer.Serialize(finalUpdate, StreamJsonOptions);
        await httpContext.Response.WriteAsync($"data: {finalPayload}\n\n", ct);
        await httpContext.Response.Body.FlushAsync(ct);

        return Results.Empty;
    }

    private async Task<IResult> HandleNonStreamingOrchestrationAsync(
        MultiStepSearchRequest searchRequest,
        List<ChatMessage>? history,
        CancellationToken ct)
    {
        var response = await _orchestrationService.ExecuteSearchAsync(searchRequest, ct);

        // Convert to ChatResponse format for compatibility
        // Each SearchFinding represents ONE document with aggregated chunks
        // Sort by document strength (includes all scoring factors)
        var sources = response.FinalFindings
            .OrderByDescending(f => f.Strength)
            .ThenBy(f => f.Chunks.Min(c => c.Distance))
            .Select(f =>
            {
                var bestChunk = f.Chunks.MinBy(c => c.Distance) ?? f.Chunks.First();
                var combinedText = string.Join("\n\n[...]\n\n", f.Chunks.Select(c => c.Text));

                return new Source(
                    DocId: f.DocId,
                    Filename: f.Filename,
                    ChunkNum: bestChunk.ChunkNum,
                    Text: combinedText,
                    Distance: bestChunk.Distance,
                    Citation: $"[{f.ProviderType}/{f.ProviderName}:{f.Filename}#chunk{bestChunk.ChunkNum}]",
                    ProviderType: f.ProviderType,
                    ProviderName: f.ProviderName
                );
            })
            .ToList();

        var answer = GenerateAnswerFromFindings(response.FinalFindings);

        var chatResponse = new ChatResponse(
            Answer: answer,
            Steps: response.ThinkingSteps,
            Files: [],
            Sources: sources,
            TokensUsed: 0,
            History: history ?? [],
            ModelUsage: null
        );

        var queryResponse = QueryResponse.FromChatResponse(chatResponse);

        _logger.LogInformation("Smart query completed (0 tokens - multi-agent search)");

        return Results.Ok(queryResponse);
    }

    private string GenerateAnswerFromFindings(List<SearchFinding> findings)
    {
        if (findings.Count == 0)
        {
            return "I couldn't find any relevant information in the indexed documents.";
        }

        var totalChunks = findings.Sum(f => f.ChunkCount);
        var lines = new List<string>
        {
            $"Found {findings.Count} relevant document{(findings.Count != 1 ? "s" : "")} with {totalChunks} matching section{(totalChunks != 1 ? "s" : "")}:"
        };

        var displayCount = Math.Min(3, findings.Count);
        for (var i = 0; i < displayCount; i++)
        {
            var f = findings[i];
            lines.Add($"• {f.Filename} (strength: {f.Strength}, {f.ChunkCount} section{(f.ChunkCount != 1 ? "s" : "")})");
        }

        if (findings.Count > displayCount)
        {
            lines.Add($"...and {findings.Count - displayCount} more document{(findings.Count - displayCount != 1 ? "s" : "")}.");
        }

        return string.Join("\n", lines);
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

    private static IResult WriteStreamingError(HttpContext httpContext, CancellationToken ct)
    {
        var errorUpdate = new ChatStreamUpdate(
            Type: "error",
            Message: "An error occurred processing your query.",
            Files: null,
            Final: null);

        var payload = JsonSerializer.Serialize(errorUpdate, StreamJsonOptions);
        httpContext.Response.WriteAsync($"data: {payload}\n\n", ct).Wait(ct);
        httpContext.Response.Body.FlushAsync(ct).Wait(ct);

        return Results.Empty;
    }
}
