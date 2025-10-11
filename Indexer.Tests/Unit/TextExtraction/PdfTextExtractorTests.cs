using FluentAssertions;
using Indexer.Services.TextExtraction;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Indexer.Tests.Unit.TextExtraction;

[Trait("Category", "Unit")]
public class PdfTextExtractorTests
{
    private readonly PdfTextExtractor _extractor;

    public PdfTextExtractorTests()
    {
        _extractor = new PdfTextExtractor(NullLogger<PdfTextExtractor>.Instance);
    }

    [Fact]
    public void SupportedExtensions_ContainsPdf()
    {
        // Assert
        _extractor.SupportedExtensions.Should().Contain(".pdf");
        _extractor.SupportedExtensions.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExtractTextAsync_ValidPdf_ExtractsText()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.pdf");
        using var stream = File.OpenRead(testFilePath);

        // Act
        var result = await _extractor.ExtractTextAsync(stream, "sample.pdf");

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Length.Should().BeGreaterThan(100);
        
        // Verify content from page 1
        result.Should().Contain("Test Document");
        result.Should().Contain("This is the first paragraph with some text");
        result.Should().Contain("Section 2");
        result.Should().Contain("Another paragraph in section 2");
    }

    [Fact]
    public async Task ExtractTextAsync_MultiPagePdf_ExtractsAllPages()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.pdf");
        using var stream = File.OpenRead(testFilePath);

        // Act
        var result = await _extractor.ExtractTextAsync(stream, "sample.pdf");

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Length.Should().BeGreaterThan(100);
        
        // Verify content from page 1
        result.Should().Contain("Test Document");
        result.Should().Contain("Section 2");
        
        // Verify content from page 2
        result.Should().Contain("New Page");
        result.Should().Contain("Here is content of the new page");
    }

    [Fact]
    public async Task ExtractTextAsync_MultiPagePdf_SeparatesPages()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.pdf");
        using var stream = File.OpenRead(testFilePath);

        // Act
        var result = await _extractor.ExtractTextAsync(stream, "sample.pdf");

        // Assert
        // Pages should be separated by blank lines
        result.Should().Contain("\n\n");
    }

    [Fact]
    public async Task ExtractTextAsync_NullStream_ThrowsArgumentNullException()
    {
        // Act
        Func<Task> act = async () => await _extractor.ExtractTextAsync(null!, "test.pdf");

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
    public async Task ExtractTextAsync_CorruptedPdf_ThrowsException()
    {
        // Arrange - create a stream with invalid PDF content
        var invalidContent = "This is not a valid PDF file"u8.ToArray();
        using var stream = new MemoryStream(invalidContent);

        // Act
        Func<Task> act = async () => await _extractor.ExtractTextAsync(stream, "corrupted.pdf");

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ExtractTextAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.pdf");
        using var stream = File.OpenRead(testFilePath);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = async () => await _extractor.ExtractTextAsync(stream, "sample.pdf", cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExtractTextAsync_PdfWithTable_ExtractsTableContent()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.pdf");
        using var stream = File.OpenRead(testFilePath);

        // Act
        var result = await _extractor.ExtractTextAsync(stream, "sample.pdf");

        // Assert
        result.Should().NotBeNullOrEmpty();
        
        // Verify table header and content is extracted
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
        var testFilePath = Path.Combine("TestData", "sample.pdf");
        
        // Act
        string result1, result2;
        using (var stream = File.OpenRead(testFilePath))
        {
            result1 = await _extractor.ExtractTextAsync(stream, "sample.pdf");
        }
        using (var stream = File.OpenRead(testFilePath))
        {
            result2 = await _extractor.ExtractTextAsync(stream, "sample.pdf");
        }

        // Assert - should be deterministic
        result1.Should().Be(result2);
    }

    [Fact]
    public async Task ExtractTextAsync_PdfWithImages_ExtractsTextOnlyNotImages()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.pdf");
        using var stream = File.OpenRead(testFilePath);

        // Act
        var result = await _extractor.ExtractTextAsync(stream, "sample.pdf");

        // Assert
        // Should extract text, images are ignored
        result.Should().NotBeNullOrEmpty();
        result.Should().NotContain("data:image");
        result.Should().NotContain("base64");
    }
}
