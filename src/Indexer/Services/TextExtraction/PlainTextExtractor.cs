using Microsoft.Extensions.Logging;

namespace Indexer.Services.TextExtraction;

/// <summary>
/// Extracts plain text from text-based files (.txt, .md, .csv, etc.).
/// </summary>
public class PlainTextExtractor(ILogger<PlainTextExtractor> logger) : ITextExtractor
{
    private readonly ILogger<PlainTextExtractor> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private static readonly HashSet<string> _supportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt",
        ".md",
        ".markdown",
        ".csv",
        ".log",
        ".json",
        ".xml",
        ".yaml",
        ".yml",
        ".html",
        ".htm",
        ".css",
        ".js",
        ".ts",
        ".sql",
        ".sh",
        ".bat",
        ".ps1"
    };

    public IReadOnlySet<string> SupportedExtensions => _supportedExtensions;

    public async Task<string> ExtractTextAsync(Stream stream, string filename, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(filename);

        try
        {
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            var text = await reader.ReadToEndAsync(ct);

            _logger.LogDebug("Extracted {Length} characters from {Filename}", text.Length, filename);
            return text;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract text from {Filename}", filename);
            throw new InvalidOperationException($"Failed to extract text from file: {filename}", ex);
        }
    }
}
