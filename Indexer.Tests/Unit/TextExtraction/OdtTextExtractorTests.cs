using FluentAssertions;
using Indexer.Services.TextExtraction;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Indexer.Tests.Unit.TextExtraction;

[Trait("Category", "Unit")]
public class OdtTextExtractorTests : IDisposable
{
    private readonly OdtTextExtractor _extractor;

    public OdtTextExtractorTests()
    {
        _extractor = new OdtTextExtractor(NullLogger<OdtTextExtractor>.Instance);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Constructor_ValidLogger_Initializes()
    {
        // Assert
        _extractor.SupportedExtensions.Should().Contain(".odt");
    }

    [Fact]
    public async Task ExtractTextAsync_ValidOdt_ExtractsText()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.odt");
        using var stream = File.OpenRead(testFilePath);

        // Act
        var result = await _extractor.ExtractTextAsync(stream, "sample.odt");

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Length.Should().BeGreaterThan(100);
        
        // Verify content from page 1 (same as PDF/DOCX)
        result.Should().Contain("Test Document");
        result.Should().Contain("This is the first paragraph with some text");
        result.Should().Contain("Section 2");
        result.Should().Contain("Another paragraph in section 2");
    }

    [Fact]
    public async Task ExtractTextAsync_Odt_PreservesContent()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.odt");
        using var stream = File.OpenRead(testFilePath);

        // Act
        var result = await _extractor.ExtractTextAsync(stream, "sample.odt");

        // Assert
        // Should have content from both pages
        result.Should().Contain("Test Document");
        result.Should().Contain("New Page");
    }

    [Fact]
    public async Task ExtractTextAsync_OdtWithTable_ExtractsTableContent()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.odt");
        using var stream = File.OpenRead(testFilePath);

        // Act
        var result = await _extractor.ExtractTextAsync(stream, "sample.odt");

        // Assert
        result.Should().NotBeNullOrEmpty();
        
        // Verify table content is extracted (same as PDF/DOCX)
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
    public async Task ExtractTextAsync_NullStream_ThrowsArgumentNullException()
    {
        // Act
        Func<Task> act = async () => await _extractor.ExtractTextAsync(null!, "test.odt");

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
    public async Task ExtractTextAsync_CorruptedOdt_ThrowsException()
    {
        // Arrange - create a stream with invalid ODT content
        var invalidContent = "This is not a valid ODT file"u8.ToArray();
        using var stream = new MemoryStream(invalidContent);

        // Act
        Func<Task> act = async () => await _extractor.ExtractTextAsync(stream, "corrupted.odt");

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ExtractTextAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.odt");
        using var stream = File.OpenRead(testFilePath);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = async () => await _extractor.ExtractTextAsync(stream, "sample.odt", cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExtractTextAsync_MultipleExtractions_ProducesSameResult()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.odt");
        
        // Act
        string result1, result2;
        using (var stream = File.OpenRead(testFilePath))
        {
            result1 = await _extractor.ExtractTextAsync(stream, "sample.odt");
        }
        using (var stream = File.OpenRead(testFilePath))
        {
            result2 = await _extractor.ExtractTextAsync(stream, "sample.odt");
        }

        // Assert - should be deterministic
        result1.Should().Be(result2);
    }

    [Fact]
    public async Task ExtractTextAsync_OdtWithFormatting_ExtractsTextWithoutFormatting()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.odt");
        using var stream = File.OpenRead(testFilePath);

        // Act
        var result = await _extractor.ExtractTextAsync(stream, "sample.odt");

        // Assert
        result.Should().NotBeNullOrEmpty();
        
        // Text should be extracted without formatting markers
        result.Should().NotContain("<text:");
        result.Should().NotContain("<office:");
        
        // But should contain the actual text content (same as PDF/DOCX)
        result.Should().Contain("Formatted text");
    }

    [Theory]
    [InlineData(".odt")]
    [InlineData(".ODT")]
    public void SupportedExtensions_IncludesOdtExtension(string extension)
    {
        // Assert
        _extractor.SupportedExtensions.Should().Contain(extension.ToLowerInvariant());
    }
}
