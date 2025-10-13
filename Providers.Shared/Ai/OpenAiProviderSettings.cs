namespace DocDuck.Providers.Ai;

/// <summary>
/// Configuration for the OpenAI provider persisted in the database.
/// </summary>
public sealed class OpenAiProviderSettings
{
    public const string ProviderType = "openai";

    public bool Enabled { get; set; } = true;
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string EmbedModel { get; set; } = "text-embedding-3-small";
    public int EmbedBatchSize { get; set; } = 16;
    public string ChatModel { get; set; } = "gpt-4o-mini";
    public string ChatModelSmall { get; set; } = "gpt-5-nano";
    public string ChatModelLarge { get; set; } = "gpt-5-mini";
    public int MaxTokens { get; set; } = 1000;
    public double Temperature { get; set; } = 0.7;
    public string RefineSystemPrompt { get; set; } = "Produce exactly one concise search phrase (3-8 words) optimized for semantic embedding similarity. Output ONLY the phrase on a single line with no surrounding quotes, punctuation, explanation, or additional text. Use lowercased main nouns and essential modifiers (no pleasantries or stopwords unless essential). Prefer concrete, domain-specific keywords that capture the user's core intent so that when vectorized the phrase will be nearest to relevant document vectors.";

    public void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("OpenAI provider requires an API key when enabled.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            throw new InvalidOperationException("OpenAI provider requires a base URL.");
        }

        if (!BaseUrl.EndsWith('/'))
        {
            BaseUrl += "/";
        }
    }

    public OpenAiProviderSettings Clone() => (OpenAiProviderSettings)MemberwiseClone();
}
