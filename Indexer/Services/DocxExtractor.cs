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
            catch (System.IO.InvalidDataException ex)
            {
                // Zip/OpenXml format errors (corrupted/truncated file)
                _logger.LogWarning(ex, "Corrupted or invalid .docx file encountered. Skipping.");
                return string.Empty;
            }
            // Note: specific OpenXmlPackageException type is not available in this dependency; rely on
            // InvalidDataException and a general catch to handle corrupted or invalid packages.
            catch (Exception ex)
            {
                // Other IO or unexpected exceptions - log and return empty so indexer can continue
                _logger.LogWarning(ex, "Unhandled error extracting .docx. Skipping file.");
                return string.Empty;
            }
        }, ct);
    }
}
