using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Api.Options;
using Microsoft.Extensions.Options;

namespace Api.Services;

/// <summary>
/// Client for OpenAI API (embeddings and chat completion).
/// </summary>
public class OpenAiClient
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;
    private readonly ILogger<OpenAiClient> _logger;

    public OpenAiClient(
        HttpClient httpClient,
        IOptions<OpenAiOptions> options,
        ILogger<OpenAiClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        // Configure HttpClient
        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }

    /// <summary>
    /// Generate embedding vector for a single text input.
    /// </summary>
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var payload = new
        {
            model = _options.EmbedModel,
            input = text
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/embeddings")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json")
        };

        _logger.LogDebug("Generating embedding for text (length: {Length})", text.Length);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<EmbeddingResponse>(responseBody);

        if (result?.Data == null || result.Data.Count == 0)
        {
            throw new InvalidOperationException("OpenAI API returned no embeddings");
        }

        return result.Data[0].Embedding;
    }

    /// <summary>
    /// Generate chat completion with context from retrieved chunks.
    /// </summary>
    public async Task<(string Answer, int TokensUsed)> GenerateAnswerAsync(
        string question,
        List<string> contextChunks,
        List<(string Role, string Content)>? history = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(contextChunks);

        // Build context from chunks
        var contextText = string.Join("\n\n", contextChunks.Select((chunk, i) => 
            $"[{i + 1}] {chunk}"));

        // Build messages
        var messages = new List<object>();

        // System prompt
        messages.Add(new
        {
            role = "system",
            content = """
                You are a helpful assistant that answers questions based on the provided document excerpts.
                
                Guidelines:
                - Answer questions using ONLY the information from the provided excerpts
                - If the excerpts don't contain enough information, say so clearly
                - Cite sources using [1], [2], etc. notation when referencing specific excerpts
                - Be concise but thorough
                - If you're uncertain, acknowledge it
                """
        });

        // Add conversation history if provided
        if (history != null)
        {
            foreach (var msg in history)
            {
                messages.Add(new { role = msg.Role, content = msg.Content });
            }
        }

        // Add current question with context
        messages.Add(new
        {
            role = "user",
            content = $"""
                Context from documents:
                {contextText}
                
                Question: {question}
                """
        });

        var payload = new
        {
            model = _options.ChatModel,
            messages,
            max_tokens = _options.MaxTokens,
            temperature = _options.Temperature
        };

        _logger.LogDebug("Generating answer for question: {Question}", question);

        var request = new HttpRequestMessage(HttpMethod.Post, "/chat/completions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json")
        };

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<ChatCompletionResponse>(responseBody);

        if (result?.Choices == null || result.Choices.Count == 0)
        {
            throw new InvalidOperationException("OpenAI API returned no completions");
        }

        var answer = result.Choices[0].Message.Content;
        var tokensUsed = result.Usage?.TotalTokens ?? 0;

        _logger.LogInformation("Generated answer ({Tokens} tokens)", tokensUsed);

        return (answer, tokensUsed);
    }

    #region Response Models

    private record EmbeddingResponse(List<EmbeddingData> Data, EmbeddingUsage? Usage);
    private record EmbeddingData(float[] Embedding, int Index);
    private record EmbeddingUsage(int PromptTokens, int TotalTokens);

    private record ChatCompletionResponse(
        List<ChatChoice> Choices,
        ChatUsage? Usage);

    private record ChatChoice(ChatMessage Message, int Index, string? FinishReason);
    private record ChatMessage(string Role, string Content);
    private record ChatUsage(int PromptTokens, int CompletionTokens, int TotalTokens);

    #endregion
}
