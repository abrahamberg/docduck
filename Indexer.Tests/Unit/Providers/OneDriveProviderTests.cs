using DocDuck.Providers.Providers;
using DocDuck.Providers.Providers.Settings;
using Microsoft.Extensions.Logging;
using Moq;

namespace Indexer.Tests.Unit.Providers;

public class OneDriveProviderTests
{
    private readonly Mock<ILogger<OneDriveProvider>> _mockLogger = new();

    [Fact]
    public void Constructor_WithValidConfig_Succeeds()
    {
        // Arrange
        var config = CreateValidConfig();

        // Act
        var provider = new OneDriveProvider(config, _mockLogger.Object);

        // Assert
        Assert.NotNull(provider);
        Assert.Equal("onedrive", provider.ProviderType);
        Assert.Equal("TestOneDrive", provider.ProviderName);
        Assert.True(provider.IsEnabled);
    }

    [Fact]
    public void Constructor_WithNullConfig_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new OneDriveProvider(null!, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange
        var config = CreateValidConfig();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new OneDriveProvider(config, null!));
    }

    [Fact]
    public void Constructor_WithMissingTenantId_ThrowsInvalidOperationException()
    {
        // Arrange
        var config = CreateValidConfig();
        config.TenantId = null;

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => new OneDriveProvider(config, _mockLogger.Object));
        Assert.Contains("TenantId", ex.Message);
    }

    [Fact]
    public void Constructor_WithMissingClientId_ThrowsInvalidOperationException()
    {
        // Arrange
        var config = CreateValidConfig();
        config.ClientId = null;

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => new OneDriveProvider(config, _mockLogger.Object));
        Assert.Contains("ClientId", ex.Message);
    }

    [Fact]
    public void Constructor_WithMissingClientSecret_ThrowsInvalidOperationException()
    {
        // Arrange
        var config = CreateValidConfig();
        config.ClientSecret = null;

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => new OneDriveProvider(config, _mockLogger.Object));
        Assert.Contains("ClientSecret", ex.Message);
    }

    [Fact]
    public void Constructor_WithPersonalAccount_Succeeds()
    {
        // Arrange
        var config = CreateValidConfig();
        config.AccountType = "personal";
        config.TenantId = "consumers";
        config.SiteId = null;
        config.DriveId = null;

        // Act
        var provider = new OneDriveProvider(config, _mockLogger.Object);

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    public void Constructor_WithBusinessAccountAndDriveId_Succeeds()
    {
        // Arrange
        var config = CreateValidConfig();
        config.AccountType = "business";
        config.DriveId = "b!drive123";
        config.SiteId = null;

        // Act
        var provider = new OneDriveProvider(config, _mockLogger.Object);

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    public void Constructor_WithBusinessAccountAndSiteId_Succeeds()
    {
        // Arrange
        var config = CreateValidConfig();
        config.AccountType = "business";
        config.SiteId = "contoso.sharepoint.com,site123,web456";
        config.DriveId = null;

        // Act
        var provider = new OneDriveProvider(config, _mockLogger.Object);

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    public async Task GetMetadataAsync_ReturnsCorrectMetadata()
    {
        // Arrange
        var config = CreateValidConfig();
        var provider = new OneDriveProvider(config, _mockLogger.Object);

        // Act
        var metadata = await provider.GetMetadataAsync();

        // Assert
        Assert.NotNull(metadata);
        Assert.Equal("onedrive", metadata.ProviderType);
        Assert.Equal("TestOneDrive", metadata.ProviderName);
        Assert.True(metadata.IsEnabled);
        Assert.NotNull(metadata.AdditionalInfo);
        Assert.Equal("business", metadata.AdditionalInfo["AccountType"]);
        Assert.Equal("/Documents", metadata.AdditionalInfo["FolderPath"]);
    }

    [Fact]
    public async Task GetMetadataAsync_WithPersonalAccount_ReturnsCorrectAccountType()
    {
        // Arrange
        var config = CreateValidConfig();
        config.AccountType = "personal";
        config.TenantId = "consumers";
        var provider = new OneDriveProvider(config, _mockLogger.Object);

        // Act
        var metadata = await provider.GetMetadataAsync();

        // Assert
        Assert.NotNull(metadata.AdditionalInfo);
        Assert.Equal("personal", metadata.AdditionalInfo["AccountType"]);
    }

    // Note: Integration tests for ListDocumentsAsync and DownloadDocumentAsync
    // would require actual Microsoft Graph API credentials and are better suited
    // for integration test suites with proper mocking or test environments.
    // The tests above cover the critical configuration and initialization logic.

    private static OneDriveProviderSettings CreateValidConfig() => new()
    {
        Enabled = true,
        Name = "TestOneDrive",
        AccountType = "business",
        TenantId = "tenant123",
        ClientId = "client456",
        ClientSecret = "secret789",
        SiteId = "site123",
        DriveId = null,
        FolderPath = "/Documents",
        FileExtensions = new List<string> { ".docx", ".pdf" }
    };
}
