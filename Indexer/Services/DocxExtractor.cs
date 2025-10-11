using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Indexer.Services;

/// <summary>
/// Extracts plain text from .docx files using OpenXML SDK.
/// </summary>
public class DocxExtractor
{
    private readonly ILogger<DocxExtractor> _logger;

    public DocxExtractor(ILogger<DocxExtractor> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Extracts plain text from a .docx stream, preserving paragraph boundaries with newlines.
    /// </summary>
    public async Task<string> ExtractPlainTextAsync(Stream docxStream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(docxStream);

        return await Task.Run(() =>
        {
            try
            {
                using var doc = WordprocessingDocument.Open(docxStream, false);
                var body = doc.MainDocumentPart?.Document?.Body;

                if (body == null)
                {
                    _logger.LogWarning("Document body is empty or invalid");
                    return string.Empty;
                }

                var sb = new StringBuilder();

                foreach (var paragraph in body.Descendants<Paragraph>())
                {
                    ct.ThrowIfCancellationRequested();

                    var paraText = paragraph.InnerText;
                    if (!string.IsNullOrWhiteSpace(paraText))
                    {
                        sb.AppendLine(paraText);
                    }
                }

                var text = sb.ToString();
                _logger.LogDebug("Extracted {Length} characters from document", text.Length);
                return text;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract text from .docx");
                throw;
            }
        }, ct);
    }
}
