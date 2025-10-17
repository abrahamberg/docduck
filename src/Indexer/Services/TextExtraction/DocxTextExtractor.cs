using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Indexer.Services.TextExtraction;

/// <summary>
/// Extracts plain text from .docx files using OpenXML SDK.
/// </summary>
public class DocxTextExtractor : ITextExtractor
{
    private readonly ILogger<DocxTextExtractor> _logger;
    
    private static readonly HashSet<string> _supportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx"
    };

    public IReadOnlySet<string> SupportedExtensions => _supportedExtensions;

    public DocxTextExtractor(ILogger<DocxTextExtractor> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async Task<string> ExtractTextAsync(Stream stream, string filename, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(filename);

        return await Task.Run(() =>
        {
            try
            {
                using var doc = WordprocessingDocument.Open(stream, false);
                var body = doc.MainDocumentPart?.Document?.Body;

                if (body == null)
                {
                    _logger.LogWarning("Document body is empty or invalid for {Filename}", filename);
                    return string.Empty;
                }

                var sb = new StringBuilder();
                var paragraphs = body.Descendants<Paragraph>()
                    .Select(p => p.InnerText)
                    .Where(text => !string.IsNullOrWhiteSpace(text));

                foreach (var paraText in paragraphs)
                {
                    ct.ThrowIfCancellationRequested();
                    sb.AppendLine(paraText);
                }

                var text = sb.ToString();
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
                throw new InvalidOperationException($"Failed to extract text from DOCX file: {filename}", ex);
            }
        }, ct);
    }
}
