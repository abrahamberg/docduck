using DocDuck.Providers.Providers.Settings;
using System.Text.Json;

namespace DocDuck.Providers.Configuration;

/// <summary>
/// Represents a provider settings row fetched from the database.
/// </summary>
public sealed class ProviderSettingsRecord
{
    public required string ProviderType { get; init; }
    public required string ProviderName { get; init; }
    public required JsonDocument Payload { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }

    public T ToSettings<T>() where T : IProviderSettings
    {
        return JsonSerializer.Deserialize<T>(Payload, ConfigurationJson.Default)
            ?? throw new InvalidOperationException($"Failed to deserialize provider settings for {ProviderType}/{ProviderName}");
    }
}
