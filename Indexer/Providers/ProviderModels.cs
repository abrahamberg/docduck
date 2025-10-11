namespace Indexer.Providers;

/// <summary>
/// Represents a document from any provider with normalized metadata.
/// </summary>
public record ProviderDocument(
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
public record ProviderMetadata(
    string ProviderType,
    string ProviderName,
    bool IsEnabled,
    DateTimeOffset RegisteredAt,
    Dictionary<string, string>? AdditionalInfo = null
);
