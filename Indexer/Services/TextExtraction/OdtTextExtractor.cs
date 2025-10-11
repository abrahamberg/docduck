using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Xml.Linq;

namespace Indexer.Services.TextExtraction;

/// <summary>
/// Extracts text from OpenDocument Text (.odt) files.
/// ODT files are ZIP archives containing content.xml with the document text.
/// </summary>
public class OdtTextExtractor : ITextExtractor
{
    private readonly ILogger<OdtTextExtractor> _logger;
    private static readonly XNamespace TextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private static readonly XNamespace OfficeNs = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";

    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string> { ".odt" };

    public OdtTextExtractor(ILogger<OdtTextExtractor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> ExtractTextAsync(Stream stream, string filename, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(filename);

        _logger.LogDebug("Extracting text from ODT file: {Filename}", filename);

        try
        {
            ct.ThrowIfCancellationRequested();

            using var zipFile = new ZipFile(stream);
            var contentEntry = zipFile.GetEntry("content.xml");
            
            if (contentEntry == null)
            {
                throw new InvalidOperationException("ODT file does not contain content.xml");
            }

            using var contentStream = zipFile.GetInputStream(contentEntry);
            var doc = await XDocument.LoadAsync(contentStream, LoadOptions.None, ct);

            var sb = new StringBuilder();

            // Extract text from all paragraphs
            var body = doc.Descendants(OfficeNs + "text").FirstOrDefault();
            if (body != null)
            {
                foreach (var paragraph in body.Descendants(TextNs + "p"))
                {
                    var paragraphText = ExtractTextFromElement(paragraph);
                    if (!string.IsNullOrWhiteSpace(paragraphText))
                    {
                        sb.AppendLine(paragraphText);
                    }
                }
            }

            var result = sb.ToString();
            _logger.LogDebug("Extracted {Length} characters from ODT file: {Filename}", result.Length, filename);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text from ODT file: {Filename}", filename);
            throw new InvalidOperationException($"Failed to extract text from ODT file: {filename}", ex);
        }
    }

    private static string ExtractTextFromElement(XElement element)
    {
        var sb = new StringBuilder();

        foreach (var node in element.Nodes())
        {
            if (node is XText textNode)
            {
                sb.Append(textNode.Value);
            }
            else if (node is XElement childElement)
            {
                // Recursively extract text from child elements
                sb.Append(ExtractTextFromElement(childElement));
                
                // Add space after certain elements
                if (childElement.Name == TextNs + "s")
                {
                    // Space element
                    var count = (int?)childElement.Attribute(TextNs + "c") ?? 1;
                    sb.Append(' ', count);
                }
                else if (childElement.Name == TextNs + "tab")
                {
                    sb.Append('\t');
                }
                else if (childElement.Name == TextNs + "line-break")
                {
                    sb.Append('\n');
                }
            }
        }

        return sb.ToString();
    }
}
