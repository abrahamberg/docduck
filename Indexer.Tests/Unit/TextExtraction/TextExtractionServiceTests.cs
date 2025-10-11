using FluentAssertions;
using Indexer.Services.TextExtraction;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Indexer.Tests.Unit.TextExtraction;

[Trait("Category", "Unit")]
public class TextExtractionServiceTests
{
    [Fact]
    public void Constructor_RegistersExtractorsCorrectly()
    {
        // Arrange
        var plainTextExtractor = new PlainTextExtractor(NullLogger<PlainTextExtractor>.Instance);
        var docxExtractor = new DocxTextExtractor(NullLogger<DocxTextExtractor>.Instance);
        var pdfExtractor = new PdfTextExtractor(NullLogger<PdfTextExtractor>.Instance);
        
        var extractors = new ITextExtractor[] { plainTextExtractor, docxExtractor, pdfExtractor };

        // Act
        var service = new TextExtractionService(extractors, NullLogger<TextExtractionService>.Instance);

        // Assert
        service.IsSupported("test.txt").Should().BeTrue();
        service.IsSupported("test.docx").Should().BeTrue();
        service.IsSupported("test.pdf").Should().BeTrue();
    }

    [Theory]
    [InlineData("document.txt", true)]
    [InlineData("document.md", true)]
    [InlineData("document.docx", true)]
    [InlineData("document.pdf", true)]
    [InlineData("document.json", true)]
    [InlineData("document.csv", true)]
    public void IsSupported_RegisteredExtensions_ReturnsTrue(string filename, bool expected)
    {
        // Arrange
        var plainTextExtractor = new PlainTextExtractor(NullLogger<PlainTextExtractor>.Instance);
        var docxExtractor = new DocxTextExtractor(NullLogger<DocxTextExtractor>.Instance);
        var pdfExtractor = new PdfTextExtractor(NullLogger<PdfTextExtractor>.Instance);
        
        var extractors = new ITextExtractor[] { plainTextExtractor, docxExtractor, pdfExtractor };
        var service = new TextExtractionService(extractors, NullLogger<TextExtractionService>.Instance);

        // Act
        var result = service.IsSupported(filename);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("document.xyz")]
    [InlineData("document.unknown")]
    [InlineData("document.bin")]
    [InlineData("document")]
    public void IsSupported_UnregisteredExtensions_ReturnsFalse(string filename)
    {
        // Arrange
        var plainTextExtractor = new PlainTextExtractor(NullLogger<PlainTextExtractor>.Instance);
        var extractors = new ITextExtractor[] { plainTextExtractor };
        var service = new TextExtractionService(extractors, NullLogger<TextExtractionService>.Instance);

        // Act
        var result = service.IsSupported(filename);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("test.TXT")]
    [InlineData("test.Txt")]
    [InlineData("test.PDF")]
    [InlineData("test.DocX")]
    public void IsSupported_CaseInsensitive_ReturnsTrue(string filename)
    {
        // Arrange
        var plainTextExtractor = new PlainTextExtractor(NullLogger<PlainTextExtractor>.Instance);
        var docxExtractor = new DocxTextExtractor(NullLogger<DocxTextExtractor>.Instance);
        var pdfExtractor = new PdfTextExtractor(NullLogger<PdfTextExtractor>.Instance);
        
        var extractors = new ITextExtractor[] { plainTextExtractor, docxExtractor, pdfExtractor };
        var service = new TextExtractionService(extractors, NullLogger<TextExtractionService>.Instance);

        // Act
        var result = service.IsSupported(filename);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExtractTextAsync_TxtFile_DelegatesToPlainTextExtractor()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.txt");
        using var stream = File.OpenRead(testFilePath);
        
        var plainTextExtractor = new PlainTextExtractor(NullLogger<PlainTextExtractor>.Instance);
        var extractors = new ITextExtractor[] { plainTextExtractor };
        var service = new TextExtractionService(extractors, NullLogger<TextExtractionService>.Instance);

        // Act
        var result = await service.ExtractTextAsync(stream, "sample.txt");

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("sample plain text");
    }

    [Fact]
    public async Task ExtractTextAsync_DocxFile_DelegatesToDocxExtractor()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.docx");
        using var stream = File.OpenRead(testFilePath);
        
        var docxExtractor = new DocxTextExtractor(NullLogger<DocxTextExtractor>.Instance);
        var extractors = new ITextExtractor[] { docxExtractor };
        var service = new TextExtractionService(extractors, NullLogger<TextExtractionService>.Instance);

        // Act
        var result = await service.ExtractTextAsync(stream, "sample.docx");

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExtractTextAsync_PdfFile_DelegatesToPdfExtractor()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.pdf");
        using var stream = File.OpenRead(testFilePath);
        
        var pdfExtractor = new PdfTextExtractor(NullLogger<PdfTextExtractor>.Instance);
        var extractors = new ITextExtractor[] { pdfExtractor };
        var service = new TextExtractionService(extractors, NullLogger<TextExtractionService>.Instance);

        // Act
        var result = await service.ExtractTextAsync(stream, "sample.pdf");

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExtractTextAsync_UnsupportedExtension_ThrowsNotSupportedException()
    {
        // Arrange
        using var stream = new MemoryStream();
        var plainTextExtractor = new PlainTextExtractor(NullLogger<PlainTextExtractor>.Instance);
        var extractors = new ITextExtractor[] { plainTextExtractor };
        var service = new TextExtractionService(extractors, NullLogger<TextExtractionService>.Instance);

        // Act
        Func<Task> act = async () => await service.ExtractTextAsync(stream, "document.unsupported");

        // Assert
        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*Unsupported file type*");
    }

    [Fact]
    public async Task ExtractTextAsync_NullStream_ThrowsArgumentNullException()
    {
        // Arrange
        var plainTextExtractor = new PlainTextExtractor(NullLogger<PlainTextExtractor>.Instance);
        var extractors = new ITextExtractor[] { plainTextExtractor };
        var service = new TextExtractionService(extractors, NullLogger<TextExtractionService>.Instance);

        // Act
        Func<Task> act = async () => await service.ExtractTextAsync(null!, "test.txt");

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExtractTextAsync_NullFilename_ThrowsArgumentNullException()
    {
        // Arrange
        using var stream = new MemoryStream();
        var plainTextExtractor = new PlainTextExtractor(NullLogger<PlainTextExtractor>.Instance);
        var extractors = new ITextExtractor[] { plainTextExtractor };
        var service = new TextExtractionService(extractors, NullLogger<TextExtractionService>.Instance);

        // Act
        Func<Task> act = async () => await service.ExtractTextAsync(stream, null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Constructor_MultipleExtractorsSameExtension_FirstWins()
    {
        // Arrange - create two mock extractors for .txt
        var mock1 = new Mock<ITextExtractor>();
        mock1.Setup(x => x.SupportedExtensions).Returns(new HashSet<string> { ".txt" });
        mock1.Setup(x => x.ExtractTextAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Extractor 1");

        var mock2 = new Mock<ITextExtractor>();
        mock2.Setup(x => x.SupportedExtensions).Returns(new HashSet<string> { ".txt" });
        mock2.Setup(x => x.ExtractTextAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Extractor 2");

        var extractors = new ITextExtractor[] { mock1.Object, mock2.Object };
        var service = new TextExtractionService(extractors, NullLogger<TextExtractionService>.Instance);

        // Act
        using var stream = new MemoryStream();
        var result = await service.ExtractTextAsync(stream, "test.txt");

        // Assert - first extractor should be used
        result.Should().Be("Extractor 1");
    }

    [Fact]
    public void Constructor_NoExtractors_CreatesEmptyService()
    {
        // Arrange
        var extractors = Array.Empty<ITextExtractor>();

        // Act
        var service = new TextExtractionService(extractors, NullLogger<TextExtractionService>.Instance);

        // Assert
        service.IsSupported("any.txt").Should().BeFalse();
    }
}
