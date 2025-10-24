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
    public string RefineSystemPrompt { get; set; } = "You are an expert at crafting semantic search queries. Given a user's question and optional conversation context, produce ONLY a concise search phrase (3-10 words) optimized for vector similarity matching.\n\nRules:\n- Output ONLY the search phrase on a single line (no quotes, no explanation, no extra text)\n- Use lowercased concrete nouns and domain-specific terms\n- Include key technical terms, product names, or specific concepts\n- If conversation context references previous topics (e.g., 'it', 'that', 'the process'), resolve pronouns to their referents\n- Capture the core information need, not conversational politeness\n- Prefer specific terms over generic ones (e.g., 'kubernetes deployment yaml' not 'how to deploy')\n\nGoal: When vectorized, this phrase should be semantically nearest to relevant document chunks in the knowledge base.";

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
