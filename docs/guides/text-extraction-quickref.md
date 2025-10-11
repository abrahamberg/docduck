# Text Extraction - Quick Reference

## Supported Formats

| Format | Extensions | Status |
|--------|-----------|--------|
| **Word** | `.docx` | ✅ Built-in |
| **Plain Text** | `.txt`, `.md`, `.csv`, `.log`, `.json`, `.xml`, `.yaml`, `.sql`, `.sh`, etc. | ✅ Built-in |
| **PDF** | `.pdf` | ⚠️ Stub (add PdfPig or Docnet.Core) |

---

## Quick Add: PDF Support

### Option 1: PdfPig (Recommended)

```bash
cd Indexer
dotnet add package PdfPig
```

**Update `PdfTextExtractor.cs`:**
```csharp
using UglyToad.PdfPig;

public Task<string> ExtractTextAsync(Stream stream, string filename, CancellationToken ct)
{
    return Task.Run(() =>
    {
        using var pdf = PdfDocument.Open(stream);
        var sb = new StringBuilder();
        
        foreach (var page in pdf.GetPages())
        {
            sb.AppendLine(page.Text);
        }
        
        return sb.ToString();
    }, ct);
}
```

**Already registered in `Program.cs` - just works!**

---

### Option 2: Docnet.Core (Faster)

```bash
dotnet add package Docnet.Core
```

```csharp
using Docnet.Core;
using Docnet.Core.Models;

public Task<string> ExtractTextAsync(Stream stream, string filename, CancellationToken ct)
{
    return Task.Run(() =>
    {
        using var library = DocLib.Instance;
        using var docReader = library.GetDocReader(stream, new PageDimensions(1080, 1920));
        var sb = new StringBuilder();
        
        for (int i = 0; i < docReader.GetPageCount(); i++)
        {
            using var pageReader = docReader.GetPageReader(i);
            var text = pageReader.GetText();
            sb.AppendLine(text);
        }
        
        return sb.ToString();
    }, ct);
}
```

---

## Quick Add: Excel Support

```bash
dotnet add package ClosedXML
```

**Create `ExcelTextExtractor.cs`:**
```csharp
using ClosedXML.Excel;

public class ExcelTextExtractor : ITextExtractor
{
    private static readonly HashSet<string> _supportedExtensions = new() { ".xlsx", ".xls" };
    public IReadOnlySet<string> SupportedExtensions => _supportedExtensions;
    
    private readonly ILogger<ExcelTextExtractor> _logger;
    
    public ExcelTextExtractor(ILogger<ExcelTextExtractor> logger) => _logger = logger;

    public Task<string> ExtractTextAsync(Stream stream, string filename, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var workbook = new XLWorkbook(stream);
            var sb = new StringBuilder();
            
            foreach (var sheet in workbook.Worksheets)
            {
                sb.AppendLine($"Sheet: {sheet.Name}");
                
                foreach (var row in sheet.RowsUsed())
                {
                    var values = row.Cells().Select(c => c.GetValue<string>());
                    sb.AppendLine(string.Join("\t", values));
                }
            }
            
            return sb.ToString();
        }, ct);
    }
}
```

**Register in `Program.cs`:**
```csharp
builder.Services.AddSingleton<ITextExtractor, ExcelTextExtractor>();
```

---

## Quick Add: PowerPoint Support

```bash
# Already installed: DocumentFormat.OpenXml
```

**Create `PptxTextExtractor.cs`:**
```csharp
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;

public class PptxTextExtractor : ITextExtractor
{
    private static readonly HashSet<string> _supportedExtensions = new() { ".pptx" };
    public IReadOnlySet<string> SupportedExtensions => _supportedExtensions;
    
    private readonly ILogger<PptxTextExtractor> _logger;
    
    public PptxTextExtractor(ILogger<PptxTextExtractor> logger) => _logger = logger;

    public Task<string> ExtractTextAsync(Stream stream, string filename, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var presentation = PresentationDocument.Open(stream, false);
            var sb = new StringBuilder();
            
            foreach (var slide in presentation.PresentationPart.SlideParts)
            {
                var shapes = slide.Slide.Descendants<Shape>();
                foreach (var shape in shapes)
                {
                    var text = shape.TextBody?.InnerText;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        sb.AppendLine(text);
                    }
                }
            }
            
            return sb.ToString();
        }, ct);
    }
}
```

**Register:**
```csharp
builder.Services.AddSingleton<ITextExtractor, PptxTextExtractor>();
```

---

## How It Works

### Automatic Format Selection

```csharp
// In MultiProviderIndexerService.cs

// 1. Check if supported
if (!_textExtractor.IsSupported(doc.Filename))
{
    _logger.LogWarning("Unsupported: {Filename}", doc.Filename);
    continue;
}

// 2. Extract (automatic format detection)
var text = await _textExtractor.ExtractTextAsync(stream, doc.Filename, ct);
```

### Extension Mapping

**Built-in mappings:**
- `.docx` → `DocxTextExtractor`
- `.txt`, `.md`, `.csv`, `.json`, `.yaml`, `.sql` → `PlainTextExtractor`
- `.pdf` → `PdfTextExtractor` (stub)

**Add new:**
```csharp
builder.Services.AddSingleton<ITextExtractor, YourExtractor>();
// Automatically maps based on SupportedExtensions property
```

---

## Configuration

### Enable File Types in Providers

```yaml
Providers:
  Local:
    FileExtensions:
      - ".docx"    # ✅ Supported
      - ".txt"     # ✅ Supported
      - ".md"      # ✅ Supported
      - ".pdf"     # ⚠️ Needs library
      - ".xlsx"    # ⚠️ Add ClosedXML
      - ".pptx"    # ⚠️ Add implementation
```

---

## Testing

```csharp
[Fact]
public async Task ExtractText_Docx_ReturnsContent()
{
    // Arrange
    var extractor = new DocxTextExtractor(logger);
    using var stream = File.OpenRead("sample.docx");
    
    // Act
    var text = await extractor.ExtractTextAsync(stream, "sample.docx", ct);
    
    // Assert
    Assert.Contains("expected text", text);
}
```

---

## Troubleshooting

### "File type not supported" warning

**Solution:** Add extractor for that file type or remove from `FileExtensions` config

### PDF extraction returns empty

**Solution:** Implement `PdfTextExtractor` with a PDF library (PdfPig, Docnet.Core)

### Excel/PowerPoint not working

**Solution:** Install library and implement extractor

---

## Summary

✅ **Modular:** Each format has dedicated extractor  
✅ **Extensible:** Implement `ITextExtractor` interface  
✅ **Automatic:** Format detection by file extension  
✅ **Simple:** Register in DI, works everywhere  
✅ **Safe:** Graceful handling of unsupported formats  

**Add a new format in 3 steps:**
1. Install NuGet package (if needed)
2. Implement `ITextExtractor`
3. Register in `Program.cs`

See [Text Extraction Guide](text-extraction.md) for detailed examples.
