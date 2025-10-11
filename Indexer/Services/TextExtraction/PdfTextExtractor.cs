using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using System.Text;

namespace Indexer.Services.TextExtraction;

/// <summary>
/// Extracts text from PDF files using PdfPig library.
/// </summary>
public class PdfTextExtractor : ITextExtractor
{
    private readonly ILogger<PdfTextExtractor> _logger;
    
    private static readonly HashSet<string> _supportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf"
    };

    public IReadOnlySet<string> SupportedExtensions => _supportedExtensions;

    public PdfTextExtractor(ILogger<PdfTextExtractor> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public Task<string> ExtractTextAsync(Stream stream, string filename, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(filename);

        return Task.Run(() =>
        {
            try
            {
                using var pdf = PdfDocument.Open(stream);
                var sb = new StringBuilder();
                
                _logger.LogDebug("Extracting text from PDF: {Filename} with {PageCount} pages", 
                    filename, pdf.NumberOfPages);

                foreach (var page in pdf.GetPages())
                {
                    if (ct.IsCancellationRequested)
                    {
                        _logger.LogWarning("PDF extraction cancelled for {Filename} at page {PageNumber}", 
                            filename, page.Number);
                        break;
                    }

                    var pageText = page.Text;
                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        sb.AppendLine(pageText);
                        sb.AppendLine(); // Add blank line between pages
                    }
                }

                var extractedText = sb.ToString().Trim();
                _logger.LogDebug("Extracted {CharCount} characters from PDF: {Filename}", 
                    extractedText.Length, filename);

                return extractedText;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract text from PDF: {Filename}", filename);
                throw new InvalidOperationException($"Failed to extract text from PDF file: {filename}", ex);
            }
        }, ct);
    }
}
