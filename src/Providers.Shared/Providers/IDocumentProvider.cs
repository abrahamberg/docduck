namespace DocDuck.Providers.Providers;

/// <summary>
/// Abstraction for document sources (OneDrive, S3, local files, etc.).
/// Shared between API and Indexer so both can manage providers consistently.
/// </summary>
public interface IDocumentProvider
{
    /// <summary>
    /// Unique identifier for this provider type (e.g., "onedrive", "s3", "local").
    /// </summary>
    string ProviderType { get; }

    /// <summary>
    /// Human-readable name for this provider instance.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Whether this provider is currently enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Lists all indexable documents from this provider.
    /// </summary>
    Task<IReadOnlyList<ProviderDocument>> ListDocumentsAsync(CancellationToken ct = default);

    /// <summary>
    /// Downloads a document as a stream for processing. Caller owns the returned stream.
    /// </summary>
    Task<Stream> DownloadDocumentAsync(string documentId, CancellationToken ct = default);

    /// <summary>
    /// Optional: Returns provider-specific metadata for tracking and display.
    /// </summary>
    Task<ProviderMetadata> GetMetadataAsync(CancellationToken ct = default);

    /// <summary>
    /// Quick connectivity test so UI and API can validate credentials without running the full indexer.
    /// </summary>
    Task<ProviderProbeResult> ProbeAsync(ProviderProbeRequest request, CancellationToken ct = default);
}
