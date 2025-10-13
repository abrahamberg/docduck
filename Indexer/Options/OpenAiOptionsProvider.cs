using DocDuck.Providers.Ai;
using Microsoft.Extensions.Options;

namespace Indexer.Options;

/// <summary>
/// Bridges OpenAI provider settings from the configuration database into IOptions for DI consumers.
/// </summary>
public sealed class OpenAiOptionsProvider : IOptions<OpenAiOptions>
{
    public OpenAiOptions Value { get; }

    public OpenAiOptionsProvider(AiConfigurationService aiConfig)
    {
        var settings = aiConfig.GetOpenAiAsync().GetAwaiter().GetResult();

        if (settings == null)
        {
            Value = new OpenAiOptions
            {
                ApiKey = string.Empty,
                BaseUrl = "https://api.openai.com/v1",
                EmbedModel = "text-embedding-3-small",
                BatchSize = 16
            };
            return;
        }

        Value = new OpenAiOptions
        {
            ApiKey = settings.ApiKey,
            BaseUrl = settings.BaseUrl.TrimEnd('/'),
            EmbedModel = settings.EmbedModel,
            BatchSize = settings.EmbedBatchSize
        };
    }
}
