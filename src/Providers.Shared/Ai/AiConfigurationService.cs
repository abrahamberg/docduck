namespace DocDuck.Providers.Ai;

/// <summary>
/// Lightweight cache for AI provider settings.
/// </summary>
public sealed class AiConfigurationService
{
    private readonly AiProviderSettingsStore _store;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private OpenAiProviderSettings? _openAi;
    private DateTimeOffset _loadedAt;

    public AiConfigurationService(AiProviderSettingsStore store)
    {
        _store = store;
    }

    public async Task<OpenAiProviderSettings?> GetOpenAiAsync(CancellationToken ct = default)
    {
        if (_openAi != null)
        {
            return _openAi.Clone();
        }

        await ReloadAsync(ct);
        return _openAi?.Clone();
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await _reloadLock.WaitAsync(ct);
        try
        {
            _openAi = await _store.GetOpenAiAsync(ct);
            _loadedAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    public DateTimeOffset LoadedAt => _loadedAt;
}
