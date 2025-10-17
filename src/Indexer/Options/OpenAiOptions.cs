namespace Indexer.Options;

/// <summary>
/// Configuration for OpenAI embeddings API.
/// </summary>
public class OpenAiOptions
{
    public const string SectionName = "OpenAi";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string EmbedModel { get; set; } = "text-embedding-3-small";
    public int BatchSize { get; set; } = 16;
}
