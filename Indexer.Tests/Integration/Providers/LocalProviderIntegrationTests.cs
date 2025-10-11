using Indexer.Options;
using Indexer.Providers;
using Microsoft.Extensions.Logging;

namespace Indexer.Tests.Integration.Providers;

/// <summary>
/// Integration tests for LocalProvider.
/// Requires a test directory with sample files to be set up.
/// </summary>
public class LocalProviderIntegrationTests : BaseProviderIntegrationTest
{
    private readonly string _testDataPath;
    private readonly LocalProvider _provider;

    public LocalProviderIntegrationTests()
    {
        // Set up test data directory
        _testDataPath = Path.Combine(Path.GetTempPath(), "docduck-test-local", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testDataPath);

        // Create test files
        CreateTestFiles();

        var config = new LocalProviderConfig
        {
            Enabled = true,
            Name = "TestLocal",
            RootPath = _testDataPath,
            FileExtensions = [".txt", ".docx", ".pdf"],
            Recursive = true,
            ExcludePatterns = ["temp", "backup"]
        };

        var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<LocalProvider>();
        _provider = new LocalProvider(config, logger);
    }

    [Fact]
    public async Task ListDocuments_ShouldReturnAllValidFiles()
    {
        // Act & Assert
        await TestListDocumentsWithMetadata(_provider);
        
        // Additional Local-specific assertions
        var documents = await _provider.ListDocumentsAsync();
        
        // Should find at least our test files
        documents.Count.Should().BeGreaterOrEqualTo(3);
        
        // Verify file extensions are respected
        var extensions = documents.Select(d => Path.GetExtension(d.Filename).ToLowerInvariant()).Distinct();
        extensions.Should().BeSubsetOf([".txt", ".docx", ".pdf"]);
        
        // Verify relative paths are set
        documents.Should().AllSatisfy(d => d.RelativePath.Should().NotBeNullOrEmpty());
        
        // Verify document IDs are consistent
        var docIds = documents.Select(d => d.DocumentId);
        docIds.Should().OnlyHaveUniqueItems();
        docIds.Should().AllSatisfy(id => id.Should().StartWith("local_"));
    }

    [Fact]
    public async Task ListDocuments_WithRecursiveEnabled_ShouldFindFilesInSubdirectories()
    {
        // Act
        var documents = await _provider.ListDocumentsAsync();
        
        // Assert - Should find files in subdirectory
        var subdirFiles = documents.Where(d => d.RelativePath!.Contains(Path.DirectorySeparatorChar));
        subdirFiles.Should().NotBeEmpty("Should find files in subdirectories when recursive is enabled");
    }

    [Fact]
    public async Task ListDocuments_ShouldExcludePatternedFiles()
    {
        // Act
        var documents = await _provider.ListDocumentsAsync();
        
        // Assert - Should not contain excluded files
        documents.Should().NotContain(d => d.RelativePath!.Contains("temp", StringComparison.OrdinalIgnoreCase));
        documents.Should().NotContain(d => d.RelativePath!.Contains("backup", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DownloadDocument_WithValidId_ShouldReturnFileStream()
    {
        // Act & Assert
        await TestDownloadDocument(_provider);
    }

    [Fact]
    public async Task DownloadDocument_WithInvalidId_ShouldThrowException()
    {
        // Act & Assert
        await TestDownloadNonExistentDocument(_provider);
    }

    [Fact]
    public async Task GetMetadata_ShouldReturnExpectedInformation()
    {
        // Arrange
        var expectedAdditionalInfo = new Dictionary<string, string>
        {
            ["RootPath"] = _testDataPath,
            ["Recursive"] = "True",
            ["Extensions"] = ".txt, .docx, .pdf"
        };

        // Act & Assert
        await TestGetMetadata(_provider, expectedAdditionalInfo);
    }

    [Fact]
    public async Task ListDocuments_ShouldProvideValidETags()
    {
        // Act
        var documents = await _provider.ListDocumentsAsync();
        
        // Assert
        documents.Should().NotBeEmpty();
        documents.Should().AllSatisfy(d => 
        {
            d.ETag.Should().NotBeNullOrEmpty();
            d.ETag.Should().StartWith("\"");
            d.ETag.Should().EndWith("\"");
        });
        
        // ETag should change when file is modified
        var firstDoc = documents.First();
        var originalETag = firstDoc.ETag;
        
        // Modify the file
        var filePath = Path.Combine(_testDataPath, firstDoc.RelativePath!);
        await File.AppendAllTextAsync(filePath, "\nModified content");
        
        // Re-list documents
        var updatedDocuments = await _provider.ListDocumentsAsync();
        var updatedDoc = updatedDocuments.First(d => d.DocumentId == firstDoc.DocumentId);
        
        updatedDoc.ETag.Should().NotBe(originalETag);
    }

    [Fact]
    public async Task Provider_Properties_ShouldMatchConfiguration()
    {
        // Assert
        _provider.ProviderType.Should().Be("local");
        _provider.ProviderName.Should().Be("TestLocal");
        _provider.IsEnabled.Should().BeTrue();
    }

    private void CreateTestFiles()
    {
        // Create files in root directory
        File.WriteAllText(Path.Combine(_testDataPath, "document1.txt"), "Sample text document 1");
        File.WriteAllText(Path.Combine(_testDataPath, "document2.docx"), "Sample docx content");
        File.WriteAllText(Path.Combine(_testDataPath, "document3.pdf"), "Sample pdf content");
        
        // Create a subdirectory with more files
        var subDir = Path.Combine(_testDataPath, "subfolder");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "nested.txt"), "Nested document");
        File.WriteAllText(Path.Combine(subDir, "nested.docx"), "Nested docx");
        
        // Create excluded files (should not be indexed)
        var tempDir = Path.Combine(_testDataPath, "temp");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "temp.txt"), "Temporary file");
        
        var backupDir = Path.Combine(_testDataPath, "backup");
        Directory.CreateDirectory(backupDir);
        File.WriteAllText(Path.Combine(backupDir, "backup.txt"), "Backup file");
        
        // Create files with unsupported extensions (should be ignored)
        File.WriteAllText(Path.Combine(_testDataPath, "image.jpg"), "Not a document");
        File.WriteAllText(Path.Combine(_testDataPath, "script.js"), "JavaScript file");
    }

    protected override void Dispose(bool disposing)
    {
        if (!DisposedValue && disposing)
        {
            // LocalProvider doesn't implement IDisposable, so no need to dispose
            
            // Clean up test directory
            try
            {
                if (Directory.Exists(_testDataPath))
                {
                    Directory.Delete(_testDataPath, recursive: true);
                }
            }
            catch (Exception ex)
            {
                // Log but don't fail the test
                Console.WriteLine($"Failed to clean up test directory: {ex.Message}");
            }
        }
        base.Dispose(disposing);
    }
}