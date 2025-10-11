using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Indexer.Tests.TestHelpers;

/// <summary>
/// Helper to generate test files for testing.
/// </summary>
public static class TestFileGenerator
{
    public static void CreateSampleDocx(string filePath)
    {
        using var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = mainPart.Document.AppendChild(new Body());

        // Add heading
        var headingPara = body.AppendChild(new Paragraph());
        var headingRun = headingPara.AppendChild(new Run());
        headingRun.AppendChild(new Text("Test Document"));
        
        // Add first paragraph
        var para1 = body.AppendChild(new Paragraph());
        var run1 = para1.AppendChild(new Run());
        run1.AppendChild(new Text("This is the first paragraph with some text."));
        
        // Add second paragraph
        var para2 = body.AppendChild(new Paragraph());
        var run2 = para2.AppendChild(new Run());
        run2.AppendChild(new Text("This is the second paragraph with more content."));
        
        // Add third paragraph
        var para3 = body.AppendChild(new Paragraph());
        var run3 = para3.AppendChild(new Run());
        run3.AppendChild(new Text("Final paragraph for testing."));

        mainPart.Document.Save();
    }

    public static void CreateEmptyDocx(string filePath)
    {
        using var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document();
        mainPart.Document.AppendChild(new Body());
        mainPart.Document.Save();
    }

    public static void CreateSamplePdf(string filePath)
    {
        // For now, create a placeholder file
        // In a real scenario, you'd use a PDF library like PdfSharpCore or iTextSharp
        File.WriteAllText(filePath + ".txt", "PDF generation requires additional library. Use real PDF for integration tests.");
    }
}
