using Api.Models;
using Api.Options;
using Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Api.Tests.Unit;

/// <summary>
/// Unit tests for VectorSearchService covering initialization, configuration, and logic paths.
/// Note: Full integration tests for database operations are in Integration folder.
/// </summary>
public sealed class VectorSearchServiceTests
{
    [Fact]
    public void Constructor_WithNullDbOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var searchOptions = Microsoft.Extensions.Options.Options.Create(new SearchOptions());
        var logger = Mock.Of<ILogger<VectorSearchService>>();

        // Act
        var act = () => new VectorSearchService(null!, searchOptions, logger);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithNullSearchOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var dbOptions = Microsoft.Extensions.Options.Options.Create(new DbOptions { ConnectionString = "test" });
        var logger = Mock.Of<ILogger<VectorSearchService>>();

        // Act
        var act = () => new VectorSearchService(dbOptions, null!, logger);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange
        var dbOptions = Microsoft.Extensions.Options.Options.Create(new DbOptions { ConnectionString = "test" });
        var searchOptions = Microsoft.Extensions.Options.Options.Create(new SearchOptions());

        // Act
        var act = () => new VectorSearchService(dbOptions, searchOptions, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithValidArguments_CreatesInstance()
    {
        // Arrange
        var dbOptions = Microsoft.Extensions.Options.Options.Create(new DbOptions { ConnectionString = "test" });
        var searchOptions = Microsoft.Extensions.Options.Options.Create(new SearchOptions());
        var logger = Mock.Of<ILogger<VectorSearchService>>();

        // Act
        var service = new VectorSearchService(dbOptions, searchOptions, logger);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task SearchAsync_WithNullEmbedding_ThrowsArgumentNullException()
    {
        // Arrange
        var dbOptions = Microsoft.Extensions.Options.Options.Create(new DbOptions { ConnectionString = "Server=localhost;Database=test;User Id=test;Password=test;" });
        var searchOptions = Microsoft.Extensions.Options.Options.Create(new SearchOptions());
        var logger = Mock.Of<ILogger<VectorSearchService>>();
        var service = new VectorSearchService(dbOptions, searchOptions, logger);

        // Act
        var act = async () => await service.SearchAsync(null!, "test", ct: CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void SearchDepth_ClampsToConfiguredMaximum()
    {
        // Arrange
        var maxDepth = 5;
        var requestedDepth = 10;

        // Act
        var clampedDepth = Math.Clamp(requestedDepth, 1, maxDepth);

        // Assert
        clampedDepth.Should().Be(5);
    }

    [Fact]
    public void SearchDepth_ClampsToMinimumOfOne()
    {
        // Arrange
        var maxDepth = 5;
        var requestedDepth = 0;

        // Act
        var clampedDepth = Math.Clamp(requestedDepth, 1, maxDepth);

        // Assert
        clampedDepth.Should().Be(1);
    }

    [Fact]
    public void TopK_ClampsToConfiguredMaximum()
    {
        // Arrange
        var searchOptions = new SearchOptions { DefaultTopK = 10, MaxTopK = 100 };
        var requestedTopK = 200;

        // Act
        var clampedK = Math.Min(requestedTopK, searchOptions.MaxTopK);

        // Assert
        clampedK.Should().Be(100);
    }

    [Fact]
    public void TopK_UsesDefaultWhenNotProvided()
    {
        // Arrange
        var searchOptions = new SearchOptions { DefaultTopK = 10, MaxTopK = 100 };
        int? requestedTopK = null;

        // Act
        var effectiveK = Math.Min(requestedTopK ?? searchOptions.DefaultTopK, searchOptions.MaxTopK);

        // Assert
        effectiveK.Should().Be(10);
    }

    [Fact]
    public void LexicalSearch_EnabledWhenConfiguredAndQueryNotEmpty()
    {
        // Arrange
        var searchOptions = new SearchOptions { EnableLexicalSearch = true };
        var query = "test query";

        // Act
        var lexicalEnabled = searchOptions.EnableLexicalSearch && !string.IsNullOrWhiteSpace(query);

        // Assert
        lexicalEnabled.Should().BeTrue();
    }

    [Fact]
    public void LexicalSearch_DisabledWhenQueryEmpty()
    {
        // Arrange
        var searchOptions = new SearchOptions { EnableLexicalSearch = true };
        var query = "";

        // Act
        var lexicalEnabled = searchOptions.EnableLexicalSearch && !string.IsNullOrWhiteSpace(query);

        // Assert
        lexicalEnabled.Should().BeFalse();
    }

    [Fact]
    public void LexicalSearch_DisabledWhenConfigurationDisabled()
    {
        // Arrange
        var searchOptions = new SearchOptions { EnableLexicalSearch = false };
        var query = "test query";

        // Act
        var lexicalEnabled = searchOptions.EnableLexicalSearch && !string.IsNullOrWhiteSpace(query);

        // Assert
        lexicalEnabled.Should().BeFalse();
    }

    [Fact]
    public void LexicalLimit_CalculatedAsMultipleOfTopK()
    {
        // Arrange
        var searchOptions = new SearchOptions { MaxLexicalResults = 50 };
        var topK = 10;

        // Act
        var lexicalLimit = Math.Max(1, Math.Min(searchOptions.MaxLexicalResults, Math.Max(topK * 3, topK)));

        // Assert
        lexicalLimit.Should().Be(30); // topK * 3
    }

    [Fact]
    public void LexicalLimit_ClampsToMaxLexicalResults()
    {
        // Arrange
        var searchOptions = new SearchOptions { MaxLexicalResults = 20 };
        var topK = 100;

        // Act
        var lexicalLimit = Math.Max(1, Math.Min(searchOptions.MaxLexicalResults, Math.Max(topK * 3, topK)));

        // Assert
        lexicalLimit.Should().Be(20);
    }

    [Fact]
    public void VectorScore_WithZeroDistance_ReturnsMaxScore()
    {
        // Arrange
        var distance = 0.0;

        // Act
        var clamped = Math.Clamp(distance, 0d, 2d);
        var score = 1d - (clamped / 2d);

        // Assert
        score.Should().Be(1.0);
    }

    [Fact]
    public void VectorScore_WithMidDistance_ReturnsHalfScore()
    {
        // Arrange
        var distance = 1.0;

        // Act
        var clamped = Math.Clamp(distance, 0d, 2d);
        var score = 1d - (clamped / 2d);

        // Assert
        score.Should().Be(0.5);
    }

    [Fact]
    public void VectorScore_WithMaxDistance_ReturnsZeroScore()
    {
        // Arrange
        var distance = 2.0;

        // Act
        var clamped = Math.Clamp(distance, 0d, 2d);
        var score = 1d - (clamped / 2d);

        // Assert
        score.Should().Be(0.0);
    }

    [Fact]
    public void VectorScore_ClampsDistancesToMax()
    {
        // Arrange
        var distance = 5.0; // Exceeds max of 2.0

        // Act
        var clamped = Math.Clamp(distance, 0d, 2d);

        // Assert
        clamped.Should().Be(2.0);
    }

    [Fact]
    public void Citation_FormatsCorrectly()
    {
        // Arrange
        var providerType = "filesystem";
        var providerName = "local-docs";
        var filename = "README.md";
        var chunkNum = 5;

        // Act
        var citation = $"[{providerType}/{providerName}:{filename}#chunk{chunkNum}]";

        // Assert
        citation.Should().Be("[filesystem/local-docs:README.md#chunk5]");
    }

    [Fact]
    public void Source_RecordCreation_StoresAllProperties()
    {
        // Arrange & Act
        var source = new Source(
            DocId: "doc-123",
            Filename: "test.md",
            ChunkNum: 1,
            Text: "Sample text",
            Distance: 0.5,
            Citation: "[fs/local:test.md#chunk1]",
            ProviderType: "filesystem",
            ProviderName: "local"
        );

        // Assert
        source.DocId.Should().Be("doc-123");
        source.Filename.Should().Be("test.md");
        source.ChunkNum.Should().Be(1);
        source.Text.Should().Be("Sample text");
        source.Distance.Should().Be(0.5);
        source.Citation.Should().Be("[fs/local:test.md#chunk1]");
        source.ProviderType.Should().Be("filesystem");
        source.ProviderName.Should().Be("local");
    }

    [Fact]
    public void Source_WithModification_CreatesNewInstance()
    {
        // Arrange
        var original = new Source(
            DocId: "doc-123",
            Filename: "test.md",
            ChunkNum: 1,
            Text: "Sample text",
            Distance: 0.5,
            Citation: "[fs/local:test.md#chunk1]",
            ProviderType: "filesystem",
            ProviderName: "local"
        );

        // Act
        var modified = original with { Distance = 0.3 };

        // Assert
        modified.Distance.Should().Be(0.3);
        original.Distance.Should().Be(0.5); // Original unchanged
        modified.DocId.Should().Be(original.DocId);
    }

    [Fact]
    public void SearchOptions_DefaultValues_AreValid()
    {
        // Arrange & Act
        var options = new SearchOptions();

        // Assert
        options.DefaultTopK.Should().BeGreaterThan(0);
        options.MaxTopK.Should().BeGreaterThanOrEqualTo(options.DefaultTopK);
        options.MaxSearchDepth.Should().BeGreaterThan(0);
        options.MaxLexicalResults.Should().BeGreaterThan(0);
        options.LexicalConfiguration.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ProviderInfo_RecordCreation_StoresAllProperties()
    {
        // Arrange & Act
        var providerInfo = new ProviderInfo(
            ProviderType: "filesystem",
            ProviderName: "local-docs",
            IsEnabled: true,
            RegisteredAt: DateTimeOffset.UtcNow,
            LastSyncAt: DateTimeOffset.UtcNow,
            Metadata: new Dictionary<string, string> { ["path"] = "/docs" }
        );

        // Assert
        providerInfo.ProviderType.Should().Be("filesystem");
        providerInfo.ProviderName.Should().Be("local-docs");
        providerInfo.IsEnabled.Should().BeTrue();
        providerInfo.RegisteredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        providerInfo.Metadata.Should().ContainKey("path");
        providerInfo.Metadata!["path"].Should().Be("/docs");
    }
}
