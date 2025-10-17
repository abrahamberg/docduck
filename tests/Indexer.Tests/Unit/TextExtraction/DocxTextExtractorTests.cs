using FluentAssertions;
using Indexer.Services.TextExtraction;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Indexer.Tests.Unit.TextExtraction;

[Trait("Category", "Unit")]
public class DocxTextExtractorTests
{
    private readonly DocxTextExtractor _extractor;

    public DocxTextExtractorTests()
    {
        _extractor = new DocxTextExtractor(NullLogger<DocxTextExtractor>.Instance);
    }

    [Fact]
    public void SupportedExtensions_ContainsDocx()
    {
        // Assert
        _extractor.SupportedExtensions.Should().Contain(".docx");
        _extractor.SupportedExtensions.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExtractTextAsync_ValidDocx_ExtractsAllParagraphs()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.docx");
        using var stream = File.OpenRead(testFilePath);

        // Act
        var result = await _extractor.ExtractTextAsync(stream, "sample.docx");

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Length.Should().BeGreaterThan(100);
        
        // Verify content from page 1 (same as PDF)
        result.Should().Contain("Test Document");
        result.Should().Contain("This is the first paragraph with some text");
        result.Should().Contain("Section 2");
        result.Should().Contain("Another paragraph in section 2");
    }

    [Fact]
    public async Task ExtractTextAsync_Docx_PreservesLineBreaks()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.docx");
        using var stream = File.OpenRead(testFilePath);

        // Act
        var result = await _extractor.ExtractTextAsync(stream, "sample.docx");

        // Assert
        // Multiple paragraphs should result in line breaks
        result.Should().Contain("\n");
        
        // Should have content from both pages
        result.Should().Contain("Test Document");
        result.Should().Contain("New Page");
    }

    [Fact]
    public async Task ExtractTextAsync_NullStream_ThrowsArgumentNullException()
    {
        // Act
        Func<Task> act = async () => await _extractor.ExtractTextAsync(null!, "test.docx");

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExtractTextAsync_NullFilename_ThrowsArgumentNullException()
    {
        // Arrange
        using var stream = new MemoryStream();

        // Act
        Func<Task> act = async () => await _extractor.ExtractTextAsync(stream, null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExtractTextAsync_CorruptedDocx_ThrowsException()
    {
        // Arrange - create a stream with invalid DOCX content
        var invalidContent = "This is not a valid DOCX file"u8.ToArray();
        using var stream = new MemoryStream(invalidContent);

        // Act
        Func<Task> act = async () => await _extractor.ExtractTextAsync(stream, "corrupted.docx");

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ExtractTextAsync_EmptyDocx_ReturnsEmptyOrWhitespace()
    {
        // Arrange - create a minimal valid DOCX
        using var memStream = new MemoryStream();
        using (var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(
            memStream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document();
            mainPart.Document.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Body());
            mainPart.Document.Save();
        }
        memStream.Position = 0;

        // Act
        var result = await _extractor.ExtractTextAsync(memStream, "empty.docx");

        // Assert
        result.Should().NotBeNull();
        result.Trim().Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractTextAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.docx");
        using var stream = File.OpenRead(testFilePath);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = async () => await _extractor.ExtractTextAsync(stream, "sample.docx", cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExtractTextAsync_DocxWithTable_ExtractsTableContent()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.docx");
        using var stream = File.OpenRead(testFilePath);

        // Act
        var result = await _extractor.ExtractTextAsync(stream, "sample.docx");

        // Assert
        result.Should().NotBeNullOrEmpty();
        
        // Verify table content is extracted (same as PDF)
        result.Should().Contain("Table");
        result.Should().Contain("Amount");
        result.Should().Contain("Description");
        result.Should().Contain("2021");
        result.Should().Contain("200");
        result.Should().Contain("This is text for 2021");
        result.Should().Contain("2020");
        result.Should().Contain("45.8");
    }

    [Fact]
    public async Task ExtractTextAsync_MultipleExtractions_ProducesSameResult()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.docx");
        
        // Act
        string result1, result2;
        using (var stream = File.OpenRead(testFilePath))
        {
            result1 = await _extractor.ExtractTextAsync(stream, "sample.docx");
        }
        using (var stream = File.OpenRead(testFilePath))
        {
            result2 = await _extractor.ExtractTextAsync(stream, "sample.docx");
        }

        // Assert - should be deterministic
        result1.Should().Be(result2);
    }

    [Fact]
    public async Task ExtractTextAsync_DocxWithFormatting_ExtractsTextWithoutFormatting()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.docx");
        using var stream = File.OpenRead(testFilePath);

        // Act
        var result = await _extractor.ExtractTextAsync(stream, "sample.docx");

        // Assert
        result.Should().NotBeNullOrEmpty();
        
        // Text should be extracted without formatting markers
        result.Should().NotContain("<b>");
        result.Should().NotContain("<i>");
        result.Should().NotContain("<");
        result.Should().NotContain(">");
        
        // But should contain the actual text content (same as PDF)
        result.Should().Contain("Formatted text");
    }
}
