using Microsoft.Extensions.Logging;

namespace Indexer.Services.TextExtraction;

/// <summary>
/// Orchestrates text extraction by delegating to format-specific extractors.
/// Automatically selects the appropriate extractor based on file extension.
/// </summary>
public class TextExtractionService
{
    private readonly ILogger<TextExtractionService> _logger;
    private readonly Dictionary<string, ITextExtractor> _extensionMap;

    public TextExtractionService(
        IEnumerable<ITextExtractor> extractors,
        ILogger<TextExtractionService> logger)
    {
        ArgumentNullException.ThrowIfNull(extractors);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;

        // Build extension -> extractor lookup map
        _extensionMap = new Dictionary<string, ITextExtractor>(StringComparer.OrdinalIgnoreCase);
        
        var extractorList = extractors.ToList();
        foreach (var extractor in extractorList)
        {
            foreach (var extension in extractor.SupportedExtensions)
            {
                if (_extensionMap.TryAdd(extension, extractor))
                {
                    _logger.LogDebug("Registered {ExtractorType} for {Extension}",
                        extractor.GetType().Name, extension);
                }
                else
                {
                    _logger.LogWarning(
                        "Extension {Extension} already registered to {ExistingExtractor}, ignoring {NewExtractor}",
                        extension, _extensionMap[extension].GetType().Name, extractor.GetType().Name);
                }
            }
        }

        _logger.LogInformation("Text extraction service initialized with {Count} extractor(s) supporting {Extensions} file types",
            extractorList.Count, _extensionMap.Count);
    }

    /// <summary>
    /// Extracts text from a file stream using the appropriate extractor.
    /// </summary>
    public async Task<string> ExtractTextAsync(Stream stream, string filename, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(filename);

        var extension = Path.GetExtension(filename);
        
        if (string.IsNullOrEmpty(extension))
        {
            _logger.LogWarning("File {Filename} has no extension, cannot determine extractor", filename);
            throw new NotSupportedException($"File has no extension: {filename}");
        }

        if (!_extensionMap.TryGetValue(extension, out var extractor))
        {
            _logger.LogWarning("No extractor found for extension {Extension} (file: {Filename})",
                extension, filename);
            throw new NotSupportedException($"Unsupported file type: {extension}");
        }

        _logger.LogDebug("Using {ExtractorType} for {Filename}",
            extractor.GetType().Name, filename);

        return await extractor.ExtractTextAsync(stream, filename, ct);
    }

    /// <summary>
    /// Checks if a file type is supported for text extraction.
    /// </summary>
    public bool IsSupported(string filename)
    {
        ArgumentNullException.ThrowIfNull(filename);
        
        var extension = Path.GetExtension(filename);
        return !string.IsNullOrEmpty(extension) && _extensionMap.ContainsKey(extension);
    }

    /// <summary>
    /// Gets all supported file extensions.
    /// </summary>
    public IEnumerable<string> GetSupportedExtensions()
    {
        return _extensionMap.Keys.OrderBy(ext => ext);
    }
}
