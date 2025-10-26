using Microsoft.Extensions.Logging;

namespace DocDuck.Providers.Ai;

/// <summary>
/// Interface for model-agnostic AI operations.
/// Manages model selection, client creation, and intelligent fallback across multiple providers.
/// </summary>
public interface IModelAgnosticAiService : IAsyncDisposable
{
    /// <summary>
    /// Timestamp when configuration was last loaded.
    /// </summary>
    DateTimeOffset LoadedAt { get; }

    /// <summary>
    /// Reload configuration from database.
    /// </summary>
    Task ReloadAsync(CancellationToken ct = default);

    /// <summary>
    /// Generate embeddings for a single text.
    /// </summary>
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Generate embeddings for multiple texts.
    /// </summary>
    Task<float[][]> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct = default);

    /// <summary>
    /// Generate a chat completion using intelligent model selection.
    /// </summary>
    Task<ChatCompletionResult> CompleteChatAsync(
        List<ChatMessagePayload> messages,
        TaskComplexity complexity,
        ModelSelectionStrategy? strategy = null,
        ChatCompletionOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get current configuration (for admin display).
    /// </summary>
    Task<AiProviderConfiguration?> GetConfigurationAsync(CancellationToken ct = default);
}
