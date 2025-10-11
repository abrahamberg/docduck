# Text Extraction System

Modular, extensible text extraction supporting multiple file formats.

## Overview

The text extraction system uses a **provider pattern** similar to the document providers. Each file format has a dedicated extractor that implements `ITextExtractor`.

### Supported Formats (Out of the Box)

| Format | Extensions | Extractor | Status |
|--------|-----------|-----------|--------|
| **Word Documents** | `.docx` | `DocxTextExtractor` | ✅ Implemented |
| **Plain Text** | `.txt`, `.md`, `.csv`, `.log`, `.json`, `.xml`, `.yaml`, `.sql`, `.sh`, etc. | `PlainTextExtractor` | ✅ Implemented |
| **PDF** | `.pdf` | `PdfTextExtractor` | ⚠️ Stub (needs PDF library) |

---

## Architecture

### Core Interface: `ITextExtractor`

```csharp
public interface ITextExtractor
{
    IReadOnlySet<string> SupportedExtensions { get; }
    Task<string> ExtractTextAsync(Stream stream, string filename, CancellationToken ct);
}
```

### Service: `TextExtractionService`

Orchestrates extraction by:
1. Building a map of file extensions → extractors
2. Selecting the appropriate extractor based on filename
3. Delegating extraction to the selected extractor

---

## How It Works

### 1. Registration (Program.cs)

```csharp
// Register all text extractors
builder.Services.AddSingleton<ITextExtractor, DocxTextExtractor>();
builder.Services.AddSingleton<ITextExtractor, PlainTextExtractor>();
builder.Services.AddSingleton<ITextExtractor, PdfTextExtractor>();

// Register orchestrator service
builder.Services.AddSingleton<TextExtractionService>();
```

### 2. Automatic Selection (MultiProviderIndexerService.cs)

```csharp
// Check if file type is supported
if (!_textExtractor.IsSupported(doc.Filename))
{
    _logger.LogWarning("File type not supported: {Filename}. Skipping.", doc.Filename);
    continue;
}

// Extract text using appropriate extractor
var text = await _textExtractor.ExtractTextAsync(stream, doc.Filename, ct);
```

### 3. Format-Specific Extraction

**Example: DOCX**
```csharp
public class DocxTextExtractor : ITextExtractor
{
    public async Task<string> ExtractTextAsync(Stream stream, string filename, CancellationToken ct)
    {
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        
        var sb = new StringBuilder();
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            sb.AppendLine(paragraph.InnerText);
        }
        return sb.ToString();
    }
}
```

**Example: Plain Text**
```csharp
public class PlainTextExtractor : ITextExtractor
{
    public async Task<string> ExtractTextAsync(Stream stream, string filename, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(ct);
    }
}
```

---

## Adding New Extractors

### Example: Adding PDF Support

**Step 1: Install NuGet Package**

Choose one:
- **PdfPig** (recommended, Apache 2.0): `dotnet add package PdfPig`
- **Docnet.Core** (fast, MIT): `dotnet add package Docnet.Core`
- **iTextSharp** (AGPL/commercial): `dotnet add package itext7`

**Step 2: Implement PdfTextExtractor**

```csharp
using UglyToad.PdfPig;

public class PdfTextExtractor : ITextExtractor
{
    private static readonly HashSet<string> _supportedExtensions = new() { ".pdf" };
    public IReadOnlySet<string> SupportedExtensions => _supportedExtensions;

    public async Task<string> ExtractTextAsync(Stream stream, string filename, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            using var pdf = PdfDocument.Open(stream);
            var sb = new StringBuilder();
            
            foreach (var page in pdf.GetPages())
            {
                ct.ThrowIfCancellationRequested();
                sb.AppendLine(page.Text);
            }
            
            return sb.ToString();
        }, ct);
    }
}
```

**Step 3: Register in Program.cs**

```csharp
builder.Services.AddSingleton<ITextExtractor, PdfTextExtractor>();
```

That's it! PDF files are now supported automatically.

---

## Example: Adding Custom Extractors

### Excel Extractor (.xlsx)

```csharp
using ClosedXML.Excel;

public class ExcelTextExtractor : ITextExtractor
{
    private static readonly HashSet<string> _supportedExtensions = new() { ".xlsx", ".xls" };
    public IReadOnlySet<string> SupportedExtensions => _supportedExtensions;

    public async Task<string> ExtractTextAsync(Stream stream, string filename, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            using var workbook = new XLWorkbook(stream);
            var sb = new StringBuilder();
            
            foreach (var worksheet in workbook.Worksheets)
            {
                sb.AppendLine($"Sheet: {worksheet.Name}");
                
                foreach (var row in worksheet.RowsUsed())
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

**Register:**
```csharp
builder.Services.AddSingleton<ITextExtractor, ExcelTextExtractor>();
```

### HTML Extractor (.html)

```csharp
using HtmlAgilityPack;

public class HtmlTextExtractor : ITextExtractor
{
    private static readonly HashSet<string> _supportedExtensions = new() { ".html", ".htm" };
    public IReadOnlySet<string> SupportedExtensions => _supportedExtensions;

    public async Task<string> ExtractTextAsync(Stream stream, string filename, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var doc = new HtmlDocument();
            doc.Load(stream);
            
            // Extract text, remove scripts and styles
            var nodes = doc.DocumentNode.SelectNodes("//script|//style");
            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    node.Remove();
                }
            }
            
            return doc.DocumentNode.InnerText;
        }, ct);
    }
}
```

---

## Configuration

### Provider File Extensions

Update provider configurations to include supported formats:

```yaml
Providers:
  Local:
    Enabled: true
    FileExtensions:
      - ".docx"
      - ".txt"
      - ".md"
      - ".pdf"     # If PDF extractor implemented
      - ".xlsx"    # If Excel extractor added
```

### Unsupported File Handling

Files with unsupported extensions are automatically skipped:

```
WARN: File type not supported: document.pptx from LocalFiles. Skipping.
```

---

## Benefits

### ✅ Modular Design
- Each format has isolated, testable extractor
- Easy to add new formats without modifying existing code
- Clear separation of concerns

### ✅ Automatic Selection
- No manual format detection logic
- Extension-based routing
- Fails gracefully for unsupported types

### ✅ Extensible
- Implement `ITextExtractor` interface
- Register in DI container
- Works automatically across all providers

### ✅ Pluggable
- Add/remove extractors without touching core code
- Swap implementations (e.g., different PDF libraries)
- Conditional registration based on config

---

## Testing

### Unit Test Example

```csharp
[Fact]
public async Task DocxExtractor_ExtractsParagraphs()
{
    // Arrange
    var extractor = new DocxTextExtractor(NullLogger<DocxTextExtractor>.Instance);
    using var stream = File.OpenRead("sample.docx");
    
    // Act
    var text = await extractor.ExtractTextAsync(stream, "sample.docx", CancellationToken.None);
    
    // Assert
    Assert.Contains("expected text", text);
}
```

### Integration Test

```csharp
[Fact]
public async Task TextExtractionService_SelectsCorrectExtractor()
{
    // Arrange
    var extractors = new ITextExtractor[]
    {
        new DocxTextExtractor(logger),
        new PlainTextExtractor(logger)
    };
    var service = new TextExtractionService(extractors, logger);
    
    // Act
    var isDocxSupported = service.IsSupported("document.docx");
    var isPdfSupported = service.IsSupported("document.pdf");
    
    // Assert
    Assert.True(isDocxSupported);
    Assert.False(isPdfSupported);  // PDF not registered
}
```

---

## Recommended Libraries

| Format | Library | NuGet Package | License |
|--------|---------|---------------|---------|
| **PDF** | PdfPig | `PdfPig` | Apache 2.0 |
| **PDF** | Docnet.Core | `Docnet.Core` | MIT |
| **Excel** | ClosedXML | `ClosedXML` | MIT |
| **HTML** | HtmlAgilityPack | `HtmlAgilityPack` | MIT |
| **PowerPoint** | OpenXML SDK | `DocumentFormat.OpenXml` | MIT |
| **RTF** | RtfPipe | `RtfPipe` | MIT |
| **Markdown** | Markdig | `Markdig` | BSD-2 |

---

## Performance Considerations

### Streaming
- All extractors work with `Stream` (not file paths)
- Supports large files without loading entirely into memory
- Compatible with S3, OneDrive, and local providers

### Async Operations
- CPU-intensive extraction wrapped in `Task.Run()`
- Doesn't block thread pool during I/O
- Supports cancellation tokens

### Error Handling
- Extractor exceptions logged and propagated
- Unsupported files skipped gracefully
- Individual file failures don't stop batch processing

---

## Migration from DocxExtractor

**Before (old code):**
```csharp
var text = await _docxExtractor.ExtractPlainTextAsync(stream, ct);
```

**After (new code):**
```csharp
if (!_textExtractor.IsSupported(doc.Filename))
{
    // Skip unsupported files
    continue;
}

var text = await _textExtractor.ExtractTextAsync(stream, doc.Filename, ct);
```

**Benefits:**
- ✅ Supports multiple formats automatically
- ✅ Graceful handling of unsupported types
- ✅ Easy to extend with new formats
- ✅ No breaking changes to providers

---

## Summary

The modular text extraction system:

✅ **Supports multiple formats** (DOCX, TXT, MD, and more)  
✅ **Extensible** via `ITextExtractor` interface  
✅ **Automatic format detection** based on file extension  
✅ **Easy to add new formats** (implement interface, register in DI)  
✅ **Graceful error handling** for unsupported files  
✅ **Works with all document providers** (OneDrive, S3, Local)  
✅ **Production-ready** with logging, cancellation support  

Add new file formats in minutes without touching existing code! 🚀
