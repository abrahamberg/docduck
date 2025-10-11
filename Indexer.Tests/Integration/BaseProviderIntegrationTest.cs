using Indexer.Providers;
using Microsoft.Extensions.Logging;
using Moq;

namespace Indexer.Tests.Integration;

/// <summary>
/// Base class for provider integration tests with common test patterns.
/// </summary>
public abstract class BaseProviderIntegrationTest : IDisposable
{
    protected readonly ILogger<IDocumentProvider> Logger;
    protected bool DisposedValue;

    protected BaseProviderIntegrationTest()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        Logger = loggerFactory.CreateLogger<IDocumentProvider>();
    }

    /// <summary>
    /// Tests that the provider can list documents with proper metadata.
    /// </summary>
    protected async Task TestListDocumentsWithMetadata(IDocumentProvider provider)
    {
        // Act
        var documents = await provider.ListDocumentsAsync();
        var metadata = await provider.GetMetadataAsync();

        // Assert - Basic metadata checks
        metadata.Should().NotBeNull();
        metadata.ProviderType.Should().Be(provider.ProviderType);
        metadata.ProviderName.Should().Be(provider.ProviderName);
        metadata.IsEnabled.Should().Be(provider.IsEnabled);

        // Assert - Documents structure
        documents.Should().NotBeNull();
        
        if (documents.Count > 0)
        {
            var firstDoc = documents.First();
            
            // Required fields
            firstDoc.DocumentId.Should().NotBeNullOrEmpty();
            firstDoc.Filename.Should().NotBeNullOrEmpty();
            firstDoc.ProviderType.Should().Be(provider.ProviderType);
            firstDoc.ProviderName.Should().Be(provider.ProviderName);
            
            // Validate metadata consistency
            foreach (var doc in documents)
            {
                doc.DocumentId.Should().NotBeNullOrEmpty();
                doc.Filename.Should().NotBeNullOrEmpty();
                doc.ProviderType.Should().Be(provider.ProviderType);
                doc.ProviderName.Should().Be(provider.ProviderName);
                
                // Optional but recommended fields
                if (doc.SizeBytes.HasValue)
                {
                    doc.SizeBytes.Value.Should().BeGreaterOrEqualTo(0);
                }
                
                if (doc.LastModified.HasValue)
                {
                    doc.LastModified.Value.Should().BeBefore(DateTimeOffset.UtcNow);
                }
            }
        }
    }

    /// <summary>
    /// Tests downloading a document from the provider.
    /// </summary>
    protected async Task TestDownloadDocument(IDocumentProvider provider)
    {
        // Arrange - Get a document to download
        var documents = await provider.ListDocumentsAsync();
        
        if (documents.Count == 0)
        {
            // Skip test if no documents available
            return;
        }

        var documentToDownload = documents.First();

        // Act
        using var stream = await provider.DownloadDocumentAsync(documentToDownload.DocumentId);

        // Assert
        stream.Should().NotBeNull();
        stream.CanRead.Should().BeTrue();
        
        // Try to read at least one byte to ensure stream is valid
        var buffer = new byte[1024];
        var bytesRead = await stream.ReadAsync(buffer);
        
        if (documentToDownload.SizeBytes.HasValue && documentToDownload.SizeBytes.Value > 0)
        {
            bytesRead.Should().BeGreaterThan(0);
        }
    }

    /// <summary>
    /// Tests provider behavior with invalid document IDs.
    /// </summary>
    protected async Task TestDownloadNonExistentDocument(IDocumentProvider provider)
    {
        // Act & Assert
        var invalidId = "non-existent-document-id-12345";
        
        var act = () => provider.DownloadDocumentAsync(invalidId);
        await act.Should().ThrowAsync<Exception>();
    }

    /// <summary>
    /// Tests that provider metadata contains expected information.
    /// </summary>
    protected async Task TestGetMetadata(IDocumentProvider provider, Dictionary<string, string>? expectedAdditionalInfo = null)
    {
        // Act
        var metadata = await provider.GetMetadataAsync();

        // Assert
        metadata.Should().NotBeNull();
        metadata.ProviderType.Should().Be(provider.ProviderType);
        metadata.ProviderName.Should().Be(provider.ProviderName);
        metadata.IsEnabled.Should().Be(provider.IsEnabled);
        metadata.RegisteredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        if (expectedAdditionalInfo != null)
        {
            metadata.AdditionalInfo.Should().NotBeNull();
            if (metadata.AdditionalInfo != null)
            {
                foreach (var (key, expectedValue) in expectedAdditionalInfo)
                {
                    metadata.AdditionalInfo.Should().ContainKey(key);
                    if (!string.IsNullOrEmpty(expectedValue))
                    {
                        metadata.AdditionalInfo[key].Should().Be(expectedValue);
                    }
                }
            }
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!DisposedValue)
        {
            if (disposing)
            {
                // Dispose managed resources
            }
            DisposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}