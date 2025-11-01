using Api.Models;

namespace Api.Services.Interfaces;

/// <summary>
/// Interface for multi-step chat interaction with LLM-driven refinement.
/// Orchestrates query refinement, vector search, context evaluation, and answer generation.
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Process a chat request with multi-step refinement and answer generation.
    /// </summary>
    /// <param name="request">Chat request with user message and options</param>
    /// <param name="progress">Optional callback for streaming progress updates</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Chat response with answer, steps, sources, and history</returns>
    Task<ChatResponse> ProcessAsync(
        ChatRequest request,
        Func<ChatStreamUpdate, Task>? progress = null,
        CancellationToken ct = default);
}
