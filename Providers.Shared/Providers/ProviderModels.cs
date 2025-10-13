namespace DocDuck.Providers.Providers;

/// <summary>
/// Represents a document from any provider with normalized metadata.
/// </summary>
public sealed record ProviderDocument(
    string DocumentId,
    string Filename,
    string ProviderType,
    string ProviderName,
    string? ETag,
    DateTimeOffset? LastModified,
    long? SizeBytes,
    string? MimeType,
    string? RelativePath
);

/// <summary>
/// Metadata about a provider instance.
/// </summary>
public sealed record ProviderMetadata(
    string ProviderType,
    string ProviderName,
    bool IsEnabled,
    DateTimeOffset RegisteredAt,
    IReadOnlyDictionary<string, string>? AdditionalInfo = null
);

/// <summary>
/// Probe request options controlling how much work a provider should perform during connectivity tests.
/// </summary>
public sealed record ProviderProbeRequest(int MaxDocuments = 3, int MaxPreviewBytes = 256)
{
    public static ProviderProbeRequest Default { get; } = new();
}

/// <summary>
/// Result of a provider probe test.
/// </summary>
public sealed record ProviderProbeResult(
    bool Success,
    string Message,
    IReadOnlyList<ProviderProbeDocument> Documents
)
{
    public static ProviderProbeResult Failure(string message) => new(false, message, Array.Empty<ProviderProbeDocument>());
    public static ProviderProbeResult SuccessResult(string message, IReadOnlyList<ProviderProbeDocument> documents) => new(true, message, documents);
}

/// <summary>
/// Probe information for a single sampled document.
/// </summary>
public sealed record ProviderProbeDocument(
    string DocumentId,
    string Filename,
    long? SizeBytes,
    string? MimeType,
    int BytesRead
);
