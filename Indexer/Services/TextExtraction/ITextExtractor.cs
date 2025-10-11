namespace Indexer.Services.TextExtraction;

/// <summary>
/// Interface for extracting plain text from various file formats.
/// </summary>
public interface ITextExtractor
{
    /// <summary>
    /// Gets the file extensions this extractor supports (e.g., ".docx", ".pdf").
    /// </summary>
    IReadOnlySet<string> SupportedExtensions { get; }
    
    /// <summary>
    /// Extracts plain text from a file stream.
    /// </summary>
    /// <param name="stream">File content stream</param>
    /// <param name="filename">Original filename (for format detection hints)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Extracted plain text</returns>
    Task<string> ExtractTextAsync(Stream stream, string filename, CancellationToken ct = default);
}
