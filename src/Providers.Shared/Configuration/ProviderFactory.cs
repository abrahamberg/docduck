using DocDuck.Providers.Providers;
using DocDuck.Providers.Providers.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace DocDuck.Providers.Configuration;

/// <summary>
/// Responsible for turning raw settings into concrete providers. Uses DI container to resolve logger instances.
/// </summary>
public sealed class ProviderFactory
{
    private readonly IServiceProvider _services;
    private readonly ConcurrentDictionary<string, Type> _settingsRegistry = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Func<IProviderSettings, IDocumentProvider>> _providerRegistry = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ProviderFactory> _logger;

    public ProviderFactory(IServiceProvider services, ILogger<ProviderFactory> logger)
    {
        _services = services;
        _logger = logger;

        RegisterProvider("local", (LocalProviderSettings settings) => ActivatorUtilities.CreateInstance<LocalProvider>(_services, settings));
        RegisterProvider("onedrive", (OneDriveProviderSettings settings) => ActivatorUtilities.CreateInstance<OneDriveProvider>(_services, settings));
        RegisterProvider("s3", (S3ProviderSettings settings) => ActivatorUtilities.CreateInstance<S3Provider>(_services, settings));
    }

    public void RegisterProvider<TSettings>(string providerType, Func<TSettings, IDocumentProvider> factory)
        where TSettings : class, IProviderSettings
    {
        var settingsType = typeof(TSettings);
        var providerKey = GetRegistryKey(providerType);

        _settingsRegistry[providerKey] = settingsType;
        _providerRegistry[providerKey] = settings => factory((TSettings)settings);

        _logger.LogDebug("Registered provider factory for {ProviderType} (settings: {SettingsType})", providerType, settingsType.Name);
    }

    public bool TryCreateSettings(ProviderSettingsRecord record, out IProviderSettings settings)
    {
        if (!_settingsRegistry.TryGetValue(GetRegistryKey(record.ProviderType), out var settingsType))
        {
            settings = null!;
            return false;
        }

    settings = (IProviderSettings)record.Payload.RootElement.Deserialize(settingsType, ConfigurationJson.Default)!;
        settings.Validate();
        return true;
    }

    public IDocumentProvider CreateProvider(IProviderSettings settings)
    {
        if (!_providerRegistry.TryGetValue(GetRegistryKey(settings.ProviderType), out var factory))
        {
            throw new InvalidOperationException($"No provider factory registered for type {settings.ProviderType}");
        }

        return factory(settings);
    }

    private static string GetRegistryKey(string providerType) => providerType.ToLowerInvariant();
}
