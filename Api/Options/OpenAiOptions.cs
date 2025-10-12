namespace Api.Options;

/// <summary>
/// Configuration for OpenAI API.
/// </summary>
public class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    public string ApiKey { get; set; } = string.Empty;
    // Ensure trailing slash so relative paths like "/chat/completions" resolve to
    // https://api.openai.com/v1/chat/completions instead of dropping the /v1 segment.
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string EmbedModel { get; set; } = "text-embedding-3-small";
    public string ChatModel { get; set; } = "gpt-4o-mini";
    /// <summary>
    /// Smaller, cheaper chat model for light contexts and query refinement.
    /// </summary>
    public string ChatModelSmall { get; set; } = "gpt-5-nano";
    /// <summary>
    /// Larger chat model for answering with broader context.
    /// </summary>
    public string ChatModelLarge { get; set; } = "gpt-5-mini";
    public int MaxTokens { get; set; } = 1000;
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// System prompt used when refining user queries into concise search phrases.
    /// Can be overridden in configuration (OpenAI:RefineSystemPrompt).
    /// </summary>
    public string RefineSystemPrompt { get; set; } = "Produce exactly one concise search phrase (3-8 words) optimized for semantic embedding similarity. Output ONLY the phrase on a single line with no surrounding quotes, punctuation, explanation, or additional text. Use lowercased main nouns and essential modifiers (no pleasantries or stopwords unless essential). Prefer concrete, domain-specific keywords that capture the user's core intent so that when vectorized the phrase will be nearest to relevant document vectors.";
}
