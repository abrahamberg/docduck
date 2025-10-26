using Api.Admin;
using FluentAssertions;

namespace Api.Tests.Unit;

[Trait("Category", "Unit")]
public class AdminDtoTests
{
    [Fact]
    public void AdminAuthOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new AdminAuthOptions();

        // Assert
        options.TokenLifetimeMinutes.Should().Be(480);
        options.Secret.Should().Be(string.Empty);
    }

    [Fact]
    public void AdminAuthOptions_SetValues_StoresCorrectly()
    {
        // Arrange & Act
        var options = new AdminAuthOptions
        {
            Secret = "my-secret-key",
            TokenLifetimeMinutes = 120
        };

        // Assert
        options.Secret.Should().Be("my-secret-key");
        options.TokenLifetimeMinutes.Should().Be(120);
    }

    [Fact]
    public void AdminAuthOptions_TokenLifetime_ConvertsToTimeSpan()
    {
        // Arrange
        var options = new AdminAuthOptions
        {
            TokenLifetimeMinutes = 30
        };

        // Act
        var timeSpan = options.TokenLifetime;

        // Assert
        timeSpan.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void AdminUser_ConstructsCorrectly()
    {
        // Arrange
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddDays(-7);
        var updatedAt = DateTimeOffset.UtcNow;

        // Act
        var user = new AdminUser(
            Id: id,
            Username: "admin",
            IsAdmin: true,
            CreatedAt: createdAt,
            UpdatedAt: updatedAt,
            PasswordHash: "hashed-password");

        // Assert
        user.Id.Should().Be(id);
        user.Username.Should().Be("admin");
        user.IsAdmin.Should().BeTrue();
        user.CreatedAt.Should().Be(createdAt);
        user.UpdatedAt.Should().Be(updatedAt);
        user.PasswordHash.Should().Be("hashed-password");
    }

    [Fact]
    public void AdminUser_WithoutPasswordHash_HandlesCorrectly()
    {
        // Arrange & Act
        var user = new AdminUser(
            Guid.NewGuid(),
            "testuser",
            false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);

        // Assert
        user.PasswordHash.Should().BeNull();
        user.IsAdmin.Should().BeFalse();
    }
}
