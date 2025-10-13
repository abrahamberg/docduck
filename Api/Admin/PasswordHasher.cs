using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Api.Admin;

internal static class PasswordHasher
{
    private const string Prefix = "pbkdf2-sha256";
    private const int SaltSizeBytes = 16;
    private const int KeySizeBytes = 32;
    private const int DefaultIterations = 100_000;

    public static string Hash(string password, int iterations = DefaultIterations)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        Span<byte> salt = stackalloc byte[SaltSizeBytes];
        RandomNumberGenerator.Fill(salt);

        var derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, KeySizeBytes);

        return string.Create(CultureInfo.InvariantCulture, $"{Prefix}.{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(derived)}");
    }

    public static bool Verify(string password, string encoded)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(encoded))
        {
            return false;
        }

        var parts = encoded.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4 || !string.Equals(parts[0], Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var iterations) || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
