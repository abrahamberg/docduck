using DocDuck.Providers.Providers.Settings;
using DocDuck.Providers.Providers;

namespace DocDuck.Providers.Configuration;

/// <summary>
/// In-memory cache of provider settings and instantiated providers.
/// </summary>
public sealed class ProviderConfigurationSnapshot
{
    private readonly Dictionary<string, IProviderSettings> _settings;
    private readonly Dictionary<string, IDocumentProvider> _providers;
    private readonly DateTimeOffset _loadedAt;

    private ProviderConfigurationSnapshot(
        Dictionary<string, IProviderSettings> settings,
        Dictionary<string, IDocumentProvider> providers,
        DateTimeOffset loadedAt)
    {
        _settings = settings;
        _providers = providers;
        _loadedAt = loadedAt;
    }

    public DateTimeOffset LoadedAt => _loadedAt;

    public IReadOnlyCollection<IDocumentProvider> Providers => _providers.Values;

    public IReadOnlyCollection<IProviderSettings> Settings => _settings.Values;

    public bool TryGetSettings(string providerType, string providerName, out IProviderSettings? settings)
    {
        return _settings.TryGetValue(Key(providerType, providerName), out settings);
    }

    public bool TryGetProvider(string providerType, string providerName, out IDocumentProvider? provider)
    {
        return _providers.TryGetValue(Key(providerType, providerName), out provider);
    }

    private static string Key(string providerType, string providerName) => $"{providerType}:{providerName}";

    public static ProviderConfigurationSnapshot Build(
        IEnumerable<IProviderSettings> settings,
        Func<IProviderSettings, IDocumentProvider> providerFactory,
        DateTimeOffset loadedAt,
        Action<IProviderSettings, Exception>? onProviderError = null)
    {
        var settingsMap = new Dictionary<string, IProviderSettings>(StringComparer.OrdinalIgnoreCase);
        var providers = new Dictionary<string, IDocumentProvider>(StringComparer.OrdinalIgnoreCase);

        foreach (var setting in settings)
        {
            var key = Key(setting.ProviderType, setting.Name);
            settingsMap[key] = setting;

            if (setting.Enabled)
            {
                try
                {
                    providers[key] = providerFactory(setting);
                }
                catch (Exception ex)
                {
                    onProviderError?.Invoke(setting, ex);
                }
            }
        }

        return new ProviderConfigurationSnapshot(settingsMap, providers, loadedAt);
    }
}
