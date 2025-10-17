using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Api.Admin;

public sealed class AdminAuthService
{
    private readonly byte[] _signingKey;
    private readonly TimeSpan _tokenLifetime;

    public AdminAuthService(IOptions<AdminAuthOptions> options)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Secret))
        {
            throw new InvalidOperationException("Admin authentication secret is not configured.");
        }

        _signingKey = Encoding.UTF8.GetBytes(settings.Secret);
        if (_signingKey.Length < 32)
        {
            // Stretch key to minimum length to avoid weak signatures.
            _signingKey = SHA256.HashData(_signingKey);
        }

        _tokenLifetime = settings.TokenLifetime;
    }

    public string IssueToken(Guid userId)
    {
        var issuedAt = DateTimeOffset.UtcNow;
        var expiresAt = issuedAt.Add(_tokenLifetime);

        var payload = new TokenPayload(userId, expiresAt);
        var serialized = payload.Serialize();
        var signature = ComputeSignature(serialized);

        return $"{Base64UrlEncode(serialized)}.{Base64UrlEncode(signature)}";
    }

    public bool TryParseToken(string token, out TokenPayload payload)
    {
        payload = default;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parts = token.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        byte[] data;
        byte[] signature;
        try
        {
            data = Base64UrlDecode(parts[0]);
            signature = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        var expectedSignature = ComputeSignature(data);
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, signature))
        {
            return false;
        }

        if (!TokenPayload.TryDeserialize(data, out payload))
        {
            return false;
        }

        if (payload.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return false;
        }

        return true;
    }

    private byte[] ComputeSignature(byte[] data)
    {
        using var hmac = new HMACSHA256(_signingKey);
        return hmac.ComputeHash(data);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(value.PadRight(value.Length + (4 - value.Length % 4) % 4, '='));
    }

    public readonly record struct TokenPayload(Guid UserId, DateTimeOffset ExpiresAt)
    {
        private const int GuidLength = 16;
        private const int TimestampLength = sizeof(long);

        public byte[] Serialize()
        {
            var buffer = new byte[GuidLength + TimestampLength];
            if (!UserId.TryWriteBytes(buffer))
            {
                throw new InvalidOperationException("Failed to serialize admin token payload.");
            }

            if (!BitConverter.TryWriteBytes(buffer.AsSpan(GuidLength), ExpiresAt.ToUnixTimeSeconds()))
            {
                throw new InvalidOperationException("Failed to serialize admin token expiration.");
            }

            return buffer;
        }

        public static bool TryDeserialize(ReadOnlySpan<byte> data, out TokenPayload payload)
        {
            payload = default;
            if (data.Length != GuidLength + TimestampLength)
            {
                return false;
            }

            var userId = new Guid(data[..GuidLength].ToArray());

            var expiresUnix = BitConverter.ToInt64(data[GuidLength..].ToArray());
            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresUnix);

            payload = new TokenPayload(userId, expiresAt);
            return true;
        }
    }
}
