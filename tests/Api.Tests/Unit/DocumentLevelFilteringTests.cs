using Api.Options;
using Api.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Api.Tests.Unit;

/// <summary>
/// Unit tests for document-level filtering feature in VectorSearchService.
/// </summary>
public sealed class DocumentLevelFilteringTests
{
    [Fact]
    public void SearchOptions_DefaultValues_DocumentFilteringEnabled()
    {
        // Arrange & Act
        var options = new SearchOptions();

        // Assert
        Assert.True(options.EnableDocumentLevelFiltering);
        Assert.Equal(20, options.DocumentLevelTopK);
    }

    [Fact]
    public void SearchOptions_CanDisableDocumentFiltering()
    {
        // Arrange & Act
        var options = new SearchOptions
        {
            EnableDocumentLevelFiltering = false,
            DocumentLevelTopK = 10
        };

        // Assert
        Assert.False(options.EnableDocumentLevelFiltering);
        Assert.Equal(10, options.DocumentLevelTopK);
    }

    [Fact]
    public void Constructor_WithValidOptions_CreatesInstance()
    {
        // Arrange
        var dbOptions = Microsoft.Extensions.Options.Options.Create(new DbOptions
        {
            ConnectionString = "Host=localhost;Database=test;Username=test;Password=test"
        });
        var searchOptions = Microsoft.Extensions.Options.Options.Create(new SearchOptions
        {
            EnableDocumentLevelFiltering = true,
            DocumentLevelTopK = 15
        });
        var logger = Mock.Of<ILogger<VectorSearchService>>();

        // Act
        var service = new VectorSearchService(dbOptions, searchOptions, logger);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public async Task SearchAsync_WithNullEmbedding_ThrowsArgumentNullException()
    {
        // Arrange
        var dbOptions = Microsoft.Extensions.Options.Options.Create(new DbOptions
        {
            ConnectionString = "Host=localhost;Database=test;Username=test;Password=test"
        });
        var searchOptions = Microsoft.Extensions.Options.Options.Create(new SearchOptions
        {
            EnableDocumentLevelFiltering = true
        });
        var logger = Mock.Of<ILogger<VectorSearchService>>();
        var service = new VectorSearchService(dbOptions, searchOptions, logger);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.SearchAsync(null!, "test", ct: CancellationToken.None));
    }
}
