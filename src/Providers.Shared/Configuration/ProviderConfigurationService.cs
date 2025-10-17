using DocDuck.Providers.Providers;
using DocDuck.Providers.Providers.Settings;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace DocDuck.Providers.Configuration;

/// <summary>
/// Loads provider settings from the store, caches them, and instantiates providers via a flexible factory.
/// </summary>
public sealed class ProviderConfigurationService
{
    private readonly ProviderSettingsStore _store;
    private readonly ProviderFactory _factory;
    private readonly ILogger<ProviderConfigurationService> _logger;

    private ProviderConfigurationSnapshot? _snapshot;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    public ProviderConfigurationService(
        ProviderSettingsStore store,
        ProviderFactory factory,
        ILogger<ProviderConfigurationService> logger)
    {
        _store = store;
        _factory = factory;
        _logger = logger;
    }

    public async Task<ProviderConfigurationSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        if (_snapshot != null)
        {
            return _snapshot;
        }

        await ReloadAsync(ct);
        return _snapshot!;
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await _reloadLock.WaitAsync(ct);
        try
        {
            var records = await _store.GetAllAsync(ct);
            var settings = new List<IProviderSettings>();

            foreach (var record in records)
            {
                try
                {
                    if (!_factory.TryCreateSettings(record, out var setting))
                    {
                        _logger.LogWarning("Skipping provider {Type}/{Name} because no registered settings type was found", record.ProviderType, record.ProviderName);
                        continue;
                    }

                    settings.Add(setting);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Provider settings validation failed for {Type}/{Name}", record.ProviderType, record.ProviderName);
                }
                finally
                {
                    record.Payload.Dispose();
                }
            }

            _snapshot = ProviderConfigurationSnapshot.Build(
                settings,
                _factory.CreateProvider,
                DateTimeOffset.UtcNow,
                (setting, ex) => _logger.LogError(ex, "Failed to instantiate provider {Type}/{Name}", setting.ProviderType, setting.Name));
            _logger.LogInformation("Loaded {Count} provider configurations", _snapshot.Providers.Count);
        }
        finally
        {
            _reloadLock.Release();
        }
    }
}
