namespace Api.Options;

/// <summary>
/// Configuration for OpenAI API.
/// </summary>
public class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string EmbedModel { get; set; } = "text-embedding-3-small";
    public string ChatModel { get; set; } = "gpt-4o-mini";
    public int MaxTokens { get; set; } = 1000;
    public double Temperature { get; set; } = 0.7;
}
