using DocDuck.Providers.Providers;
using DocDuck.Providers.Providers.Settings;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Indexer.Tests.Unit.Providers;

[Trait("Category", "Unit")]
public class LocalProviderTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly LocalProviderSettings _settings;
    private readonly LocalProvider _provider;

    public LocalProviderTests()
    {
        // Create a temporary test directory
        _testDirectory = Path.Combine(Path.GetTempPath(), $"docduck-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);

        _settings = new LocalProviderSettings
        {
            Name = "TestLocal",
            Enabled = true,
            RootPath = _testDirectory,
            FileExtensions = new List<string> { ".txt", ".md", ".docx", ".pdf" }
        };

        _provider = new LocalProvider(_settings, NullLogger<LocalProvider>.Instance);
    }

    public void Dispose()
    {
        // Cleanup test directory
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_ValidConfig_Initializes()
    {
        // Assert
        _provider.ProviderType.Should().Be("local");
        _provider.ProviderName.Should().Be("TestLocal");
        _provider.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Constructor_NonExistentPath_CreatesDirectory()
    {
        // Arrange
        var testPath = Path.Combine(Path.GetTempPath(), $"docduck-nonexistent-{Guid.NewGuid()}");
        var config = new LocalProviderSettings
        {
            Name = "Test",
            Enabled = true,
            RootPath = testPath,
            FileExtensions = new List<string> { ".txt" }
        };

        try
        {
            // Act
            var provider = new LocalProvider(config, NullLogger<LocalProvider>.Instance);

            // Assert - directory should be created
            Directory.Exists(testPath).Should().BeTrue();
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(testPath))
            {
                Directory.Delete(testPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ListDocumentsAsync_EmptyDirectory_ReturnsEmpty()
    {
        // Act
        var documents = await _provider.ListDocumentsAsync();

        // Assert
        documents.Should().BeEmpty();
    }

    [Fact]
    public async Task ListDocumentsAsync_WithMatchingFiles_ReturnsDocuments()
    {
        // Arrange
        var file1 = Path.Combine(_testDirectory, "test1.txt");
        var file2 = Path.Combine(_testDirectory, "test2.md");
        File.WriteAllText(file1, "Content 1");
        File.WriteAllText(file2, "Content 2");

        // Act
        var documents = await _provider.ListDocumentsAsync();

        // Assert
        documents.Should().HaveCount(2);
        documents.Should().Contain(d => d.Filename == "test1.txt");
        documents.Should().Contain(d => d.Filename == "test2.md");
    }

    [Fact]
    public async Task ListDocumentsAsync_SkipsUnsupportedExtensions()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_testDirectory, "test.txt"), "Supported");
        File.WriteAllText(Path.Combine(_testDirectory, "test.xyz"), "Unsupported");

        // Act
        var documents = await _provider.ListDocumentsAsync();

        // Assert
        documents.Should().HaveCount(1);
        documents.Single().Filename.Should().Be("test.txt");
    }

    [Fact]
    public async Task ListDocumentsAsync_RecursiveSearch_FindsNestedFiles()
    {
        // Arrange
        var subDir = Path.Combine(_testDirectory, "subfolder");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(_testDirectory, "root.txt"), "Root");
        File.WriteAllText(Path.Combine(subDir, "nested.txt"), "Nested");

        // Act
        var documents = await _provider.ListDocumentsAsync();

        // Assert
        documents.Should().HaveCount(2);
        documents.Should().Contain(d => d.Filename == "root.txt");
        documents.Should().Contain(d => d.Filename == "nested.txt");
    }

    [Fact]
    public async Task ListDocumentsAsync_GeneratesStableDocumentIds()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_testDirectory, "test.txt"), "Content");

        // Act
        var docs1 = await _provider.ListDocumentsAsync();
        var docs2 = await _provider.ListDocumentsAsync();

        // Assert
        docs1.Single().DocumentId.Should().Be(docs2.Single().DocumentId);
    }

    [Fact]
    public async Task ListDocumentsAsync_SetsProviderInfo()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_testDirectory, "test.txt"), "Content");

        // Act
        var documents = await _provider.ListDocumentsAsync();

        // Assert
        var doc = documents.Single();
        doc.ProviderType.Should().Be("local");
        doc.ProviderName.Should().Be("TestLocal");
    }

    [Fact]
    public async Task ListDocumentsAsync_SetsFileMetadata()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "test.txt");
        File.WriteAllText(filePath, "Test content");
        var fileInfo = new FileInfo(filePath);

        // Act
        var documents = await _provider.ListDocumentsAsync();

        // Assert
        var doc = documents.Single();
        doc.Filename.Should().Be("test.txt");
        doc.SizeBytes.Should().BeGreaterThan(0);
        doc.LastModified.Should().BeCloseTo(fileInfo.LastWriteTimeUtc, TimeSpan.FromSeconds(2));
        doc.MimeType.Should().Be("text/plain");
        doc.ETag.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ListDocumentsAsync_GeneratesDifferentETagForModifiedFile()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "test.txt");
        File.WriteAllText(filePath, "Original");
        var docs1 = await _provider.ListDocumentsAsync();
        var etag1 = docs1.Single().ETag;

        // Wait to ensure timestamp changes
        await Task.Delay(100);

        // Modify file
        File.WriteAllText(filePath, "Modified content");

        // Act
        var docs2 = await _provider.ListDocumentsAsync();
        var etag2 = docs2.Single().ETag;

        // Assert
        etag2.Should().NotBe(etag1);
    }

    [Fact]
    public async Task DownloadDocumentAsync_ValidDocumentId_ReturnsStream()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "test.txt");
        var content = "Test file content";
        File.WriteAllText(filePath, content);
        
        var documents = await _provider.ListDocumentsAsync();
        var docId = documents.Single().DocumentId;

        // Act
        using var stream = await _provider.DownloadDocumentAsync(docId);
        using var reader = new StreamReader(stream);
        var result = await reader.ReadToEndAsync();

        // Assert
        result.Should().Be(content);
    }

    [Fact]
    public async Task DownloadDocumentAsync_NonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var invalidDocId = "local:nonexistent.txt";

        // Act
        Func<Task> act = async () => await _provider.DownloadDocumentAsync(invalidDocId);

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task DownloadDocumentAsync_NestedFile_Works()
    {
        // Arrange
        var subDir = Path.Combine(_testDirectory, "subfolder");
        Directory.CreateDirectory(subDir);
        var filePath = Path.Combine(subDir, "nested.txt");
        File.WriteAllText(filePath, "Nested content");

        var documents = await _provider.ListDocumentsAsync();
        var doc = documents.Single(d => d.Filename == "nested.txt");

        // Act
        using var stream = await _provider.DownloadDocumentAsync(doc.DocumentId);
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        // Assert
        content.Should().Be("Nested content");
    }

    [Fact]
    public async Task GetMetadataAsync_ReturnsCorrectMetadata()
    {
        // Act
        var metadata = await _provider.GetMetadataAsync();

        // Assert
        metadata.ProviderType.Should().Be("local");
        metadata.ProviderName.Should().Be("TestLocal");
        metadata.IsEnabled.Should().BeTrue();
        metadata.AdditionalInfo.Should().ContainKey("RootPath");
        metadata.AdditionalInfo["RootPath"]!.Should().Be(_testDirectory);
    }

    [Fact]
    public async Task ListDocumentsAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        // Create many files to increase chance of cancellation
        for (int i = 0; i < 100; i++)
        {
            File.WriteAllText(Path.Combine(_testDirectory, $"file{i}.txt"), "Content");
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = async () => await _provider.ListDocumentsAsync(cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ListDocumentsAsync_SetsRelativePath()
    {
        // Arrange
        var subDir = Path.Combine(_testDirectory, "docs", "projects");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "file.txt"), "Content");

        // Act
        var documents = await _provider.ListDocumentsAsync();

        // Assert
        var doc = documents.Single();
        doc.RelativePath.Should().Be(Path.Combine("docs", "projects", "file.txt"));
    }

    [Fact]
    public async Task ListDocumentsAsync_MultipleExtensions_FiltersCorrectly()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_testDirectory, "doc.txt"), "Text");
        File.WriteAllText(Path.Combine(_testDirectory, "doc.md"), "Markdown");
        File.WriteAllText(Path.Combine(_testDirectory, "doc.docx"), "Word");
        File.WriteAllText(Path.Combine(_testDirectory, "doc.xyz"), "Unknown");

        // Act
        var documents = await _provider.ListDocumentsAsync();

        // Assert
        documents.Should().HaveCount(3); // txt, md, docx (xyz is not in config)
    }

    [Fact]
    public void Constructor_NullConfig_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => new LocalProvider(null!, NullLogger<LocalProvider>.Instance);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => new LocalProvider(_config, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
