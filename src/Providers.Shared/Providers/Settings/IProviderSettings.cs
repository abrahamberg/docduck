namespace DocDuck.Providers.Providers.Settings;

/// <summary>
/// Marker contract for provider configuration objects.
/// </summary>
public interface IProviderSettings
{
    /// <summary>
    /// Provider type identifier (e.g. "onedrive", "s3").
    /// </summary>
    string ProviderType { get; }

    /// <summary>
    /// Logical provider name configured by the operator.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Whether this provider should be considered active.
    /// </summary>
    bool Enabled { get; }

    /// <summary>
    /// Validates required fields; throw <see cref="InvalidOperationException"/> on failure.
    /// </summary>
    void Validate();
}
