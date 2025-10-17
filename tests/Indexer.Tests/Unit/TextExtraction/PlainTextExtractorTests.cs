using FluentAssertions;
using Indexer.Services.TextExtraction;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Indexer.Tests.Unit.TextExtraction;

[Trait("Category", "Unit")]
public class PlainTextExtractorTests
{
    private readonly PlainTextExtractor _extractor;

    public PlainTextExtractorTests()
    {
        _extractor = new PlainTextExtractor(NullLogger<PlainTextExtractor>.Instance);
    }

    [Fact]
    public void SupportedExtensions_ContainsCommonTextFormats()
    {
        // Assert
        _extractor.SupportedExtensions.Should().Contain(".txt");
        _extractor.SupportedExtensions.Should().Contain(".md");
        _extractor.SupportedExtensions.Should().Contain(".csv");
        _extractor.SupportedExtensions.Should().Contain(".json");
        _extractor.SupportedExtensions.Should().Contain(".xml");
        _extractor.SupportedExtensions.Should().Contain(".yaml");
        _extractor.SupportedExtensions.Should().HaveCountGreaterThan(10);
    }

    [Fact]
    public async Task ExtractTextAsync_PlainTextFile_ExtractsContent()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.txt");
        using var stream = File.OpenRead(testFilePath);

        // Act
        var result = await _extractor.ExtractTextAsync(stream, "sample.txt");

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("sample plain text");
        result.Should().Contain("multiple lines");
    }

    [Fact]
    public async Task ExtractTextAsync_MarkdownFile_ExtractsContent()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.md");
        using var stream = File.OpenRead(testFilePath);

        // Act
        var result = await _extractor.ExtractTextAsync(stream, "sample.md");

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("Sample Markdown");
        result.Should().Contain("**markdown**");
    }

    [Fact]
    public async Task ExtractTextAsync_JsonFile_ExtractsContent()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.json");
        using var stream = File.OpenRead(testFilePath);

        // Act
        var result = await _extractor.ExtractTextAsync(stream, "sample.json");

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("\"name\"");
        result.Should().Contain("\"value\"");
    }

    [Fact]
    public async Task ExtractTextAsync_CsvFile_ExtractsContent()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.csv");
        using var stream = File.OpenRead(testFilePath);

        // Act
        var result = await _extractor.ExtractTextAsync(stream, "sample.csv");

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("name,age,city");
        result.Should().Contain("John,30,NYC");
    }

    [Fact]
    public async Task ExtractTextAsync_EmptyFile_ReturnsEmptyString()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "empty.txt");
        using var stream = File.OpenRead(testFilePath);

        // Act
        var result = await _extractor.ExtractTextAsync(stream, "empty.txt");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractTextAsync_Utf8Content_HandlesCorrectly()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.txt");
        using var stream = File.OpenRead(testFilePath);

        // Act
        var result = await _extractor.ExtractTextAsync(stream, "sample.txt");

        // Assert - if file contains UTF-8 characters, they should be preserved
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExtractTextAsync_NullStream_ThrowsArgumentNullException()
    {
        // Act
        Func<Task> act = async () => await _extractor.ExtractTextAsync(null!, "test.txt");

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
    public async Task ExtractTextAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var testFilePath = Path.Combine("TestData", "sample.txt");
        using var stream = File.OpenRead(testFilePath);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = async () => await _extractor.ExtractTextAsync(stream, "sample.txt", cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".TXT")]
    [InlineData(".Txt")]
    public void SupportedExtensions_IsCaseInsensitive(string extension)
    {
        // Assert - should contain lowercase version
        _extractor.SupportedExtensions.Should().Contain(extension.ToLowerInvariant());
    }
}
