# Text Extraction

File format–specific logic handled by implementations of `ITextExtractor`, orchestrated by `TextExtractionService`.

## Interface
```csharp
public interface ITextExtractor {
  IReadOnlyCollection<string> SupportedExtensions { get; }
  Task<string> ExtractTextAsync(Stream stream, string filename, CancellationToken ct);
}
```

## Dispatch Flow
1. Determine extension
2. Lookup extractor in extension map
3. Call extractor
4. Return plain UTF-8 text

## Built-In Extractors (Conceptual)
| Extractor | Extensions | Notes |
|-----------|-----------|-------|
| PlainTextExtractor | .txt .md .csv .json | Simple read / minimal cleanup |
| DocxTextExtractor | .docx | OpenXML-based extraction |
| PdfTextExtractor | .pdf | Optional dependency (stream parsing) |
| OdtTextExtractor | .odt | Zip/XML parse |
| RtfTextExtractor | .rtf | RTF to plain text |

## Adding an Extractor
```csharp
public sealed class HtmlTextExtractor : ITextExtractor {
  public IReadOnlyCollection<string> SupportedExtensions => new[] { ".html", ".htm" };
  public async Task<string> ExtractTextAsync(Stream s, string filename, CancellationToken ct) { /* parse & strip */ }
}
```
Register in DI; `TextExtractionService` auto-picks it.

## Error Handling
- Unsupported extension ⇒ `NotSupportedException`
- Empty or whitespace output triggers skip logic upstream

## Performance Tips
- Avoid full DOM loads for large HTML; stream parse
- Consider early size checks to skip huge binaries

## Future Enhancements
- Configurable max file size
- Structured metadata extraction (title, headings)

## Next
- Embeddings: [Embeddings & AI](ai-layer.md)
