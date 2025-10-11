using System.Text;
using Microsoft.Extensions.Logging;
using RtfPipe;

namespace Indexer.Services.TextExtraction;

/// <summary>
/// Extracts text from Rich Text Format (.rtf) files.
/// </summary>
public class RtfTextExtractor : ITextExtractor
{
    private readonly ILogger<RtfTextExtractor> _logger;

    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string> { ".rtf" };

    static RtfTextExtractor()
    {
        // Register encoding provider for Windows-1252 and other codepages needed by RtfPipe
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public RtfTextExtractor(ILogger<RtfTextExtractor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> ExtractTextAsync(Stream stream, string filename, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(filename);

        _logger.LogDebug("Extracting text from RTF file: {Filename}", filename);

        try
        {
            ct.ThrowIfCancellationRequested();

            // Read RTF content
            using var reader = new StreamReader(stream);
            var rtfContent = await reader.ReadToEndAsync(ct);

            ct.ThrowIfCancellationRequested();

            // Convert RTF to HTML, then extract plain text
            var html = Rtf.ToHtml(rtfContent);
            
            // Simple HTML tag removal (RtfPipe produces clean HTML)
            var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", string.Empty);
            
            // Decode HTML entities
            text = System.Net.WebUtility.HtmlDecode(text);
            
            // Clean up extra whitespace
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"(\r?\n\s*){3,}", "\n\n");
            
            var result = text.Trim();
            _logger.LogDebug("Extracted {Length} characters from RTF file: {Filename}", result.Length, filename);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text from RTF file: {Filename}", filename);
            throw new InvalidOperationException($"Failed to extract text from RTF file: {filename}", ex);
        }
    }
}
