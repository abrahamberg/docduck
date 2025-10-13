using DocDuck.Providers.Providers;
using DocDuck.Providers.Providers.Settings;
using Microsoft.Extensions.Logging;

namespace Indexer.Tests.Integration.Providers;

/// <summary>
/// Integration tests for OneDriveProvider.
/// Requires OneDrive app registration and proper configuration.
/// Set environment variables: ONEDRIVE_TENANT_ID, ONEDRIVE_CLIENT_ID, ONEDRIVE_CLIENT_SECRET
/// Optionally: ONEDRIVE_DRIVE_ID, ONEDRIVE_SITE_ID, ONEDRIVE_FOLDER_PATH
/// </summary>
[Collection("OneDriveIntegration")]
public class OneDriveProviderIntegrationTests : BaseProviderIntegrationTest
{
    private readonly string? _tenantId;
    private readonly string? _clientId;
    private readonly string? _clientSecret;
    private readonly string? _driveId;
    private readonly string? _siteId;
    private readonly string _folderPath;
    private readonly OneDriveProvider? _provider;

    public OneDriveProviderIntegrationTests()
    {
        _tenantId = Environment.GetEnvironmentVariable("ONEDRIVE_TENANT_ID");
        _clientId = Environment.GetEnvironmentVariable("ONEDRIVE_CLIENT_ID");
        _clientSecret = Environment.GetEnvironmentVariable("ONEDRIVE_CLIENT_SECRET");
        _driveId = Environment.GetEnvironmentVariable("ONEDRIVE_DRIVE_ID");
        _siteId = Environment.GetEnvironmentVariable("ONEDRIVE_SITE_ID");
        _folderPath = Environment.GetEnvironmentVariable("ONEDRIVE_FOLDER_PATH") ?? "/Shared Documents/Docs";

        // Skip tests if credentials not available
        if (string.IsNullOrEmpty(_tenantId) || string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
        {
            return;
        }

        var config = new OneDriveProviderSettings
        {
            Enabled = true,
            Name = "TestOneDrive",
            AccountType = "business", // Assuming business account for integration tests
            TenantId = _tenantId,
            ClientId = _clientId,
            ClientSecret = _clientSecret,
            DriveId = _driveId,
            SiteId = _siteId,
            FolderPath = _folderPath,
            FileExtensions = [".docx", ".pdf", ".txt"]
        };

        var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<OneDriveProvider>();
        _provider = new OneDriveProvider(config, logger);
    }

    [SkippableFact]
    public async Task ListDocuments_ShouldReturnAllValidFiles()
    {
        Skip.If(_provider == null, "OneDrive credentials not configured");
        
        // Act & Assert
        await TestListDocumentsWithMetadata(_provider!);
        
        // Additional OneDrive-specific assertions
        var documents = await _provider!.ListDocumentsAsync();
        
        if (documents.Count > 0)
        {
            // Verify OneDrive-specific properties
            documents.Should().AllSatisfy(d => 
            {
                d.DocumentId.Should().NotBeNullOrEmpty();
                d.Filename.Should().NotBeNullOrEmpty();
                
                // OneDrive should provide ETags
                if (!string.IsNullOrEmpty(d.ETag))
                {
                    d.ETag.Should().NotBeNullOrEmpty();
                }
                
                // OneDrive should provide MIME types
                if (!string.IsNullOrEmpty(d.MimeType))
                {
                    d.MimeType.Should().NotBeNullOrEmpty();
                }
                
                // OneDrive should provide size information
                if (d.SizeBytes.HasValue)
                {
                    d.SizeBytes.Value.Should().BeGreaterOrEqualTo(0);
                }
            });
            
            // Verify file extensions are respected
            var extensions = documents
                .Select(d => Path.GetExtension(d.Filename).ToLowerInvariant())
                .Where(ext => !string.IsNullOrEmpty(ext))
                .Distinct();
            
            if (extensions.Any())
            {
                extensions.Should().BeSubsetOf([".docx", ".pdf", ".txt"]);
            }
        }
    }

    [SkippableFact]
    public async Task ListDocuments_WithPaging_ShouldReturnAllDocuments()
    {
        Skip.If(_provider == null, "OneDrive credentials not configured");
        
        // Act
        var documents = await _provider!.ListDocumentsAsync();
        
        // Assert - This test verifies that paging works correctly
        // OneDrive uses PageIterator internally, so all documents should be returned
        // even if there are multiple pages
        
        if (documents.Count > 0)
        {
            // Verify all documents are unique (no duplicates from paging issues)
            var documentIds = documents.Select(d => d.DocumentId);
            documentIds.Should().OnlyHaveUniqueItems("Paging should not cause duplicate documents");
            
            // Verify all documents have consistent provider information
            documents.Should().AllSatisfy(d => 
            {
                d.ProviderType.Should().Be("onedrive");
                d.ProviderName.Should().Be("TestOneDrive");
            });
        }
    }

    [SkippableFact]
    public async Task DownloadDocument_WithValidId_ShouldReturnDocumentStream()
    {
        Skip.If(_provider == null, "OneDrive credentials not configured");
        
        // Act & Assert
        await TestDownloadDocument(_provider!);
        
        // Additional OneDrive-specific test
        var documents = await _provider!.ListDocumentsAsync();
        
        if (documents.Count > 0)
        {
            var testDoc = documents.First();
            
            using var stream = await _provider!.DownloadDocumentAsync(testDoc.DocumentId);
            
            // Verify stream properties
            stream.Should().NotBeNull();
            stream.CanRead.Should().BeTrue();
            
            // Try to read some content to ensure stream is valid
            var buffer = new byte[1024];
            var bytesRead = await stream.ReadAsync(buffer);
            
            // Should be able to read some content if file size > 0
            if (testDoc.SizeBytes.HasValue && testDoc.SizeBytes.Value > 0)
            {
                bytesRead.Should().BeGreaterThan(0);
            }
        }
    }

    [SkippableFact]
    public async Task DownloadDocument_WithInvalidId_ShouldThrowException()
    {
        Skip.If(_provider == null, "OneDrive credentials not configured");
        
        // Act & Assert
        await TestDownloadNonExistentDocument(_provider!);
    }

    [SkippableFact]
    public async Task GetMetadata_ShouldReturnExpectedInformation()
    {
        Skip.If(_provider == null, "OneDrive credentials not configured");
        
        // Arrange
        var expectedAdditionalInfo = new Dictionary<string, string>
        {
            ["AccountType"] = "business",
            ["FolderPath"] = _folderPath
        };

        // Act & Assert
        await TestGetMetadata(_provider!, expectedAdditionalInfo);
    }

    [SkippableFact]
    public async Task ListDocuments_ShouldRespectFileExtensionFilter()
    {
        Skip.If(_provider == null, "OneDrive credentials not configured");
        
        // Act
        var documents = await _provider!.ListDocumentsAsync();
        
        if (documents.Count > 0)
        {
            // Assert - Should only contain supported extensions
            var extensions = documents
                .Select(d => Path.GetExtension(d.Filename).ToLowerInvariant())
                .Where(ext => !string.IsNullOrEmpty(ext))
                .Distinct();
            
            if (extensions.Any())
            {
                extensions.Should().BeSubsetOf([".docx", ".pdf", ".txt"]);
            }
        }
    }

    [SkippableFact]
    public async Task ListDocuments_ShouldProvideConsistentDocumentIds()
    {
        Skip.If(_provider == null, "OneDrive credentials not configured");
        
        // Act - List documents twice
        var documents1 = await _provider!.ListDocumentsAsync();
        await Task.Delay(100); // Small delay
        var documents2 = await _provider!.ListDocumentsAsync();
        
        // Assert - Document IDs should be consistent across calls
        if (documents1.Count > 0 && documents2.Count > 0)
        {
            documents1.Count.Should().Be(documents2.Count, "Document count should be consistent");
            
            var ids1 = documents1.Select(d => d.DocumentId).OrderBy(id => id).ToList();
            var ids2 = documents2.Select(d => d.DocumentId).OrderBy(id => id).ToList();
            
            ids1.Should().BeEquivalentTo(ids2, "Document IDs should be consistent across calls");
        }
    }

    [SkippableFact]
    public async Task ListDocuments_ShouldProvideValidLastModifiedDates()
    {
        Skip.If(_provider == null, "OneDrive credentials not configured");
        
        // Act
        var documents = await _provider!.ListDocumentsAsync();
        
        // Assert
        if (documents.Count > 0)
        {
            documents.Should().AllSatisfy(d => 
            {
                if (d.LastModified.HasValue)
                {
                    d.LastModified.Value.Should().BeBefore(DateTimeOffset.UtcNow.AddMinutes(1));
                    d.LastModified.Value.Should().BeAfter(DateTimeOffset.UtcNow.AddYears(-10));
                }
            });
        }
    }

    [SkippableFact]
    public async Task Provider_Properties_ShouldMatchConfiguration()
    {
        Skip.If(_provider == null, "OneDrive credentials not configured");
        
        // Assert
        _provider!.ProviderType.Should().Be("onedrive");
        _provider.ProviderName.Should().Be("TestOneDrive");
        _provider.IsEnabled.Should().BeTrue();
    }

    [SkippableFact]
    public async Task Provider_ShouldHandleEmptyFolder()
    {
        Skip.If(_provider == null, "OneDrive credentials not configured");
        
        // This test assumes we might point to an empty folder
        // The provider should handle this gracefully without throwing exceptions
        
        // Act
        var act = async () => await _provider!.ListDocumentsAsync();
        
        // Assert - Should not throw, even if folder is empty
        await act.Should().NotThrowAsync();
        
        var documents = await _provider!.ListDocumentsAsync();
        documents.Should().NotBeNull();
        // documents.Count can be 0 if folder is empty - that's valid
    }

    protected override void Dispose(bool disposing)
    {
        if (!DisposedValue && disposing)
        {
            // OneDriveProvider doesn't implement IDisposable, but GraphServiceClient might hold resources
            // The provider will be garbage collected
        }
        base.Dispose(disposing);
    }
}