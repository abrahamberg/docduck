namespace Api.Models;

/// <summary>
/// Request model for query endpoint.
/// </summary>
public record QueryRequest(
    string Question,
    int? TopK = null,
    string? ProviderType = null,
    string? ProviderName = null
);

/// <summary>
/// Response model for query endpoint.
/// </summary>
public record QueryResponse(
    string Answer,
    List<Source> Sources,
    int TokensUsed
);

/// <summary>
/// Represents a source document chunk with citation information.
/// </summary>
public record Source(
    string DocId,
    string Filename,
    int ChunkNum,
    string Text,
    double Distance,
    string Citation,
    string? ProviderType = null,
    string? ProviderName = null
);

/// <summary>
/// Information about an active document provider.
/// </summary>
public record ProviderInfo(
    string ProviderType,
    string ProviderName,
    bool IsEnabled,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? LastSyncAt,
    Dictionary<string, string>? Metadata
);

/// <summary>
/// Request model for chat endpoint (with history).
/// </summary>
public record ChatRequest(
    string Message,
    List<ChatMessage>? History = null,
    int? TopK = null,
    string? ProviderType = null,
    string? ProviderName = null
);

/// <summary>
/// Chat message in conversation history.
/// </summary>
public record ChatMessage(
    string Role,  // "user" or "assistant"
    string Content
);

/// <summary>
/// Response model for chat endpoint.
/// </summary>
public record ChatResponse(
    string Answer,
    List<Source> Sources,
    int TokensUsed,
    List<ChatMessage> History
);

/// <summary>
/// Internal model for database chunk results.
/// </summary>
internal record ChunkResult(
    string DocId,
    string Filename,
    int ChunkNum,
    string Text,
    string? Metadata,
    double Distance
);
