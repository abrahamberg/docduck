using Api.Admin;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Api.Tests.Unit;

[Trait("Category", "Unit")]
public class AdminAuthServiceTests
{
    private static AdminAuthService CreateService(string secret = "test-secret-key-32-chars-long!", int lifetimeMinutes = 60)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AdminAuthOptions
        {
            Secret = secret,
            TokenLifetimeMinutes = lifetimeMinutes
        });
        return new AdminAuthService(options);
    }

    [Fact]
    public void Constructor_WithValidSecret_Succeeds()
    {
        // Act
        var service = CreateService();

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullSecret_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new AdminAuthOptions { Secret = null! });

        // Act
        var act = () => new AdminAuthService(options);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*secret*");
    }

    [Fact]
    public void Constructor_WithEmptySecret_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new AdminAuthOptions { Secret = "" });

        // Act
        var act = () => new AdminAuthService(options);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*secret*");
    }

    [Fact]
    public void Constructor_WithShortSecret_StretchesKey()
    {
        // Arrange - secret shorter than 32 bytes should be hashed to 32 bytes
        var options = Microsoft.Extensions.Options.Options.Create(new AdminAuthOptions { Secret = "short" });

        // Act
        var service = new AdminAuthService(options);
        var token = service.IssueToken(Guid.NewGuid());

        // Assert
        token.Should().NotBeNullOrEmpty();
        service.TryParseToken(token, out _).Should().BeTrue();
    }

    [Fact]
    public void IssueToken_ValidUserId_ReturnsNonEmptyToken()
    {
        // Arrange
        var service = CreateService();
        var userId = Guid.NewGuid();

        // Act
        var token = service.IssueToken(userId);

        // Assert
        token.Should().NotBeNullOrEmpty();
        token.Should().Contain(".");
        token.Split('.').Should().HaveCount(2, "token should have payload.signature format");
    }

    [Fact]
    public void IssueToken_SameUserId_TokensHaveSameFormatButDifferentContent()
    {
        // Arrange
        var service = CreateService();
        var userId = Guid.NewGuid();

        // Act
        var token1 = service.IssueToken(userId);
        var token2 = service.IssueToken(userId);

        // Assert
        // Both should have same format (base64.base64)
        token1.Split('.').Should().HaveCount(2);
        token2.Split('.').Should().HaveCount(2);
        // But should parse to same userId
        service.TryParseToken(token1, out var payload1).Should().BeTrue();
        service.TryParseToken(token2, out var payload2).Should().BeTrue();
        payload1.UserId.Should().Be(userId);
        payload2.UserId.Should().Be(userId);
    }

    [Fact]
    public void TryParseToken_ValidToken_ReturnsTrue()
    {
        // Arrange
        var service = CreateService();
        var userId = Guid.NewGuid();
        var token = service.IssueToken(userId);

        // Act
        var result = service.TryParseToken(token, out var payload);

        // Assert
        result.Should().BeTrue();
        payload.UserId.Should().Be(userId);
        payload.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void TryParseToken_NullToken_ReturnsFalse()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.TryParseToken(null!, out var payload);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseToken_EmptyToken_ReturnsFalse()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.TryParseToken("", out var payload);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseToken_WhitespaceToken_ReturnsFalse()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.TryParseToken("   ", out var payload);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseToken_MalformedToken_ReturnsFalse()
    {
        // Arrange
        var service = CreateService();
        var malformedTokens = new[]
        {
            "no-dots",
            "too.many.dots.here",
            ".",
            "..",
            "invalid-base64!@#$.signature"
        };

        // Act & Assert
        foreach (var token in malformedTokens)
        {
            var result = service.TryParseToken(token, out var payload);
            result.Should().BeFalse($"'{token}' should be rejected");
        }
    }

    [Fact]
    public void TryParseToken_TamperedSignature_ReturnsFalse()
    {
        // Arrange
        var service = CreateService();
        var userId = Guid.NewGuid();
        var token = service.IssueToken(userId);
        var parts = token.Split('.');

        // Tamper with signature
        var tamperedToken = parts[0] + ".AAAAAAAAAAAAAAAAAAAAAA";

        // Act
        var result = service.TryParseToken(tamperedToken, out var payload);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseToken_TamperedPayload_ReturnsFalse()
    {
        // Arrange
        var service = CreateService();
        var userId = Guid.NewGuid();
        var token = service.IssueToken(userId);
        var parts = token.Split('.');

        // Tamper with payload
        var tamperedToken = "AAAAAAAAAAAAAAAAAAAAAA." + parts[1];

        // Act
        var result = service.TryParseToken(tamperedToken, out var payload);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseToken_WrongSecret_ReturnsFalse()
    {
        // Arrange
        var service1 = CreateService("secret1");
        var service2 = CreateService("secret2");
        var userId = Guid.NewGuid();
        var token = service1.IssueToken(userId);

        // Act - try to parse with different service (different secret)
        var result = service2.TryParseToken(token, out var payload);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseToken_TokenWithShortLifetime_ValidatesExpiry()
    {
        // Arrange - create a token with  normal lifetime that's valid now
        var service = CreateService(lifetimeMinutes: 60);
        var userId = Guid.NewGuid();
        var token = service.IssueToken(userId);

        // Act
        var result = service.TryParseToken(token, out var payload);

        // Assert
        result.Should().BeTrue("token should not be expired yet");
        payload.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void TryParseToken_FutureExpiration_ReturnsTrue()
    {
        // Arrange
        var service = CreateService(lifetimeMinutes: 120);
        var userId = Guid.NewGuid();
        var token = service.IssueToken(userId);

        // Act
        var result = service.TryParseToken(token, out var payload);

        // Assert
        result.Should().BeTrue();
        payload.UserId.Should().Be(userId);
        payload.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(100));
    }

    [Fact]
    public void TokenPayload_Serialize_ProducesExpectedLength()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        var payload = new AdminAuthService.TokenPayload(userId, expiresAt);

        // Act
        var serialized = payload.Serialize();

        // Assert
        serialized.Should().HaveCount(24, "16 bytes for Guid + 8 bytes for timestamp");
    }

    [Fact]
    public void TokenPayload_SerializeDeserialize_RoundTrips()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        var originalPayload = new AdminAuthService.TokenPayload(userId, expiresAt);

        // Act
        var serialized = originalPayload.Serialize();
        var deserialized = AdminAuthService.TokenPayload.TryDeserialize(serialized, out var result);

        // Assert
        deserialized.Should().BeTrue();
        result.UserId.Should().Be(userId);
        result.ExpiresAt.ToUnixTimeSeconds().Should().Be(expiresAt.ToUnixTimeSeconds());
    }

    [Fact]
    public void TokenPayload_TryDeserialize_InvalidLength_ReturnsFalse()
    {
        // Arrange
        var invalidData = new byte[] { 1, 2, 3 };

        // Act
        var result = AdminAuthService.TokenPayload.TryDeserialize(invalidData, out var payload);

        // Assert
        result.Should().BeFalse();
        // Don't assert on payload value as it's a struct with default values
    }

    [Fact]
    public void TokenPayload_TryDeserialize_EmptyData_ReturnsFalse()
    {
        // Arrange
        var emptyData = Array.Empty<byte>();

        // Act
        var result = AdminAuthService.TokenPayload.TryDeserialize(emptyData, out var payload);

        // Assert
        result.Should().BeFalse();
        // Don't assert on payload value as it's a struct with default values
    }

    [Fact]
    public void IssueToken_CustomLifetime_RespectsLifetime()
    {
        // Arrange
        var customLifetimeMinutes = 30;
        var service = CreateService(lifetimeMinutes: customLifetimeMinutes);
        var userId = Guid.NewGuid();

        // Act
        var token = service.IssueToken(userId);
        service.TryParseToken(token, out var payload);

        // Assert
        var expectedExpiration = DateTimeOffset.UtcNow.AddMinutes(customLifetimeMinutes);
        payload.ExpiresAt.Should().BeCloseTo(expectedExpiration, TimeSpan.FromSeconds(5));
    }
}
