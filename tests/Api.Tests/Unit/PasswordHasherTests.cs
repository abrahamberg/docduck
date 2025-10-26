using Api.Admin;
using FluentAssertions;

namespace Api.Tests.Unit;

[Trait("Category", "Unit")]
public class PasswordHasherTests
{
    [Fact]
    public void Hash_ValidPassword_ReturnsEncodedString()
    {
        // Arrange
        var password = "SecurePassword123!";

        // Act
        var hash = PasswordHasher.Hash(password);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().StartWith("pbkdf2-sha256");
        hash.Split('.').Should().HaveCount(4);
    }

    [Fact]
    public void Hash_SamePassword_ProducesDifferentHashes()
    {
        // Arrange
        var password = "TestPassword";

        // Act
        var hash1 = PasswordHasher.Hash(password);
        var hash2 = PasswordHasher.Hash(password);

        // Assert
        hash1.Should().NotBe(hash2, "each hash should use a unique salt");
    }

    [Fact]
    public void Hash_CustomIterations_UsesSpecifiedIterations()
    {
        // Arrange
        var password = "TestPassword";
        var customIterations = 50000;

        // Act
        var hash = PasswordHasher.Hash(password, customIterations);

        // Assert
        var parts = hash.Split('.');
        parts[1].Should().Be(customIterations.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Hash_InvalidPassword_ThrowsArgumentException(string? password)
    {
        // Act
        var act = () => PasswordHasher.Hash(password!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        // Arrange
        var password = "MySecurePassword";
        var hash = PasswordHasher.Hash(password);

        // Act
        var result = PasswordHasher.Verify(password, hash);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Verify_IncorrectPassword_ReturnsFalse()
    {
        // Arrange
        var password = "CorrectPassword";
        var wrongPassword = "WrongPassword";
        var hash = PasswordHasher.Hash(password);

        // Act
        var result = PasswordHasher.Verify(wrongPassword, hash);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_EmptyPassword_ReturnsFalse()
    {
        // Arrange
        var hash = PasswordHasher.Hash("SomePassword");

        // Act
        var result = PasswordHasher.Verify("", hash);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_EmptyHash_ReturnsFalse()
    {
        // Act
        var result = PasswordHasher.Verify("SomePassword", "");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_NullPassword_ReturnsFalse()
    {
        // Arrange
        var hash = PasswordHasher.Hash("TestPassword");

        // Act
        var result = PasswordHasher.Verify(null!, hash);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_NullHash_ReturnsFalse()
    {
        // Act
        var result = PasswordHasher.Verify("TestPassword", null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_MalformedHash_ReturnsFalse()
    {
        // Arrange - various malformed hashes
        var testCases = new[]
        {
            "invalid",
            "pbkdf2-sha256",
            "pbkdf2-sha256.100000",
            "pbkdf2-sha256.100000.salt",
            "wrong-prefix.100000.salt.hash",
            "pbkdf2-sha256.notanumber.salt.hash",
            "pbkdf2-sha256.-1.salt.hash",
            "pbkdf2-sha256.100000.!!!invalid-base64!!!.hash"
        };

        // Act & Assert
        foreach (var malformedHash in testCases)
        {
            var result = PasswordHasher.Verify("TestPassword", malformedHash);
            result.Should().BeFalse($"'{malformedHash}' should be rejected as malformed");
        }
    }

    [Fact]
    public void Verify_CustomIterations_VerifiesCorrectly()
    {
        // Arrange
        var password = "CustomIterPassword";
        var customIterations = 75000;
        var hash = PasswordHasher.Hash(password, customIterations);

        // Act
        var result = PasswordHasher.Verify(password, hash);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Hash_SpecialCharacters_HandlesCorrectly()
    {
        // Arrange
        var password = "P@ssw0rd!#$%^&*()_+-=[]{}|;:',.<>?/~`";

        // Act
        var hash = PasswordHasher.Hash(password);
        var result = PasswordHasher.Verify(password, hash);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Hash_UnicodeCharacters_HandlesCorrectly()
    {
        // Arrange
        var password = "пароль密码パスワード🔒";

        // Act
        var hash = PasswordHasher.Hash(password);
        var result = PasswordHasher.Verify(password, hash);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Verify_CaseSensitive_DifferentCasesReturnFalse()
    {
        // Arrange
        var password = "CaseSensitive";
        var hash = PasswordHasher.Hash(password);

        // Act
        var resultUpper = PasswordHasher.Verify("CASESENSITIVE", hash);
        var resultLower = PasswordHasher.Verify("casesensitive", hash);

        // Assert
        resultUpper.Should().BeFalse();
        resultLower.Should().BeFalse();
    }

    [Fact]
    public void Verify_TamperedSalt_ReturnsFalse()
    {
        // Arrange
        var password = "TestPassword";
        var hash = PasswordHasher.Hash(password);
        var parts = hash.Split('.');

        // Tamper with salt
        var tamperedHash = $"{parts[0]}.{parts[1]}.AAAAAAAAAAAAAAAAAAAAAA==.{parts[3]}";

        // Act
        var result = PasswordHasher.Verify(password, tamperedHash);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_TamperedHashValue_ReturnsFalse()
    {
        // Arrange
        var password = "TestPassword";
        var hash = PasswordHasher.Hash(password);
        var parts = hash.Split('.');

        // Tamper with hash value
        var tamperedHash = $"{parts[0]}.{parts[1]}.{parts[2]}.AAAAAAAAAAAAAAAAAAAAAA==";

        // Act
        var result = PasswordHasher.Verify(password, tamperedHash);

        // Assert
        result.Should().BeFalse();
    }
}
