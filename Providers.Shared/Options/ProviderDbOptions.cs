namespace DocDuck.Providers.Options;

/// <summary>
/// Options wrapper for provider settings database connection.
/// </summary>
public sealed class ProviderDbOptions
{
    public const string SectionName = "Database";
    public string ConnectionString { get; set; } = string.Empty;
}
