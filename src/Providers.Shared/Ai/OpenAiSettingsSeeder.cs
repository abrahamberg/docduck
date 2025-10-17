namespace DocDuck.Providers.Ai;

/// <summary>
/// Seeds OpenAI settings from environment variables when no DB record exists.
/// </summary>
public sealed class OpenAiSettingsSeeder
{
    private readonly AiProviderSettingsStore _store;

    public OpenAiSettingsSeeder(AiProviderSettingsStore store)
    {
        _store = store;
    }

    public async Task SeedFromEnvironmentAsync(CancellationToken ct = default)
    {
        var existing = await _store.GetOpenAiAsync(ct);
        if (existing != null)
        {
            return;
        }

        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
        var settings = new OpenAiProviderSettings
        {
            ApiKey = apiKey,
            BaseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "https://api.openai.com/v1/",
            EmbedModel = Environment.GetEnvironmentVariable("OPENAI_EMBED_MODEL") ?? "text-embedding-3-small",
            EmbedBatchSize = TryParseInt("OPENAI_EMBED_BATCH_SIZE") ?? 16,
            ChatModel = Environment.GetEnvironmentVariable("OPENAI_CHAT_MODEL") ?? "gpt-4o-mini",
            ChatModelSmall = Environment.GetEnvironmentVariable("OPENAI_CHAT_MODEL_SMALL") ?? "gpt-5-nano",
            ChatModelLarge = Environment.GetEnvironmentVariable("OPENAI_CHAT_MODEL_LARGE") ?? "gpt-5-mini",
            MaxTokens = TryParseInt("OPENAI_MAX_TOKENS") ?? 1000,
            Temperature = TryParseDouble("OPENAI_TEMPERATURE") ?? 0.7,
            RefineSystemPrompt = Environment.GetEnvironmentVariable("OPENAI_REFINE_SYSTEM_PROMPT") ?? new OpenAiProviderSettings().RefineSystemPrompt
        };

        // If no API key provided, mark as disabled but still persist defaults.
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            settings.Enabled = false;
        }

        settings.Validate();
        await _store.UpsertAsync(settings, ct);
    }

    private static int? TryParseInt(string env)
        => int.TryParse(Environment.GetEnvironmentVariable(env), out var value) ? value : null;

    private static double? TryParseDouble(string env)
        => double.TryParse(Environment.GetEnvironmentVariable(env), out var value) ? value : null;
}
