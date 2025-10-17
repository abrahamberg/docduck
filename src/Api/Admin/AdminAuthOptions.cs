using System;

namespace Api.Admin;

public sealed class AdminAuthOptions
{
    public string Secret { get; set; } = string.Empty;
    public int TokenLifetimeMinutes { get; set; } = 480;

    public TimeSpan TokenLifetime => TimeSpan.FromMinutes(TokenLifetimeMinutes <= 0 ? 480 : TokenLifetimeMinutes);
}
