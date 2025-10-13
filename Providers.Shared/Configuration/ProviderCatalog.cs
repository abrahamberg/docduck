using DocDuck.Providers.Providers;

namespace DocDuck.Providers.Configuration;

/// <summary>
/// Provides convenient access to the current provider snapshot. Wraps async reload behind a simple interface.
/// </summary>
public sealed class ProviderCatalog
{
    private readonly ProviderConfigurationService _service;

    public ProviderCatalog(ProviderConfigurationService service)
    {
        _service = service;
    }

    public async Task<IReadOnlyList<IDocumentProvider>> GetProvidersAsync(CancellationToken ct = default)
    {
        var snapshot = await _service.GetSnapshotAsync(ct);
        return snapshot.Providers.ToList();
    }

    public async Task<IDocumentProvider?> GetProviderAsync(string providerType, string providerName, CancellationToken ct = default)
    {
        var snapshot = await _service.GetSnapshotAsync(ct);
        return snapshot.TryGetProvider(providerType, providerName, out var provider) ? provider : null;
    }
}
