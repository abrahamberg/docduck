using Indexer.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Embeddings;

namespace Indexer.Services;

/// <summary>
/// SDK-based embeddings client. Replaces the previous raw-HTTP implementation.
/// </summary>
public class OpenAiEmbeddingsClient
{
    private readonly EmbeddingClient _client;
    private readonly ILogger<OpenAiEmbeddingsClient> _logger;

    public OpenAiEmbeddingsClient(IOptions<OpenAiOptions> options, ILogger<OpenAiEmbeddingsClient> logger)
    {
        var opts = options.Value;
        _logger = logger;

        // Prefer ApiKey from configured options (useful for tests), fallback to env var.
        var apiKey = !string.IsNullOrWhiteSpace(opts.ApiKey)
            ? opts.ApiKey
            : Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("OPENAI_API_KEY not set");

        // Normalize BaseUrl to end with '/'
        if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
        {
            var normalized = opts.BaseUrl.EndsWith('/') ? opts.BaseUrl : opts.BaseUrl + "/";
            Environment.SetEnvironmentVariable("OPENAI_BASE_URL", normalized);
        }

        _client = new EmbeddingClient(opts.EmbedModel, apiKey);
    }

    public Task<float[][]> EmbedBatchedAsync(IEnumerable<string> inputs, CancellationToken ct = default)
    {
        var list = inputs.ToList();
        if (list.Count == 0) return Task.FromResult(Array.Empty<float[]>());

        return Task.Run(async () =>
        {
            var outputs = new List<float[]>();
            foreach (var text in list)
            {
                ct.ThrowIfCancellationRequested();
                var r = await _client.GenerateEmbeddingAsync(text, options: null, cancellationToken: ct);
                outputs.Add(r.Value.ToFloats().ToArray());
            }
            _logger.LogDebug("Generated {Count} embeddings", outputs.Count);
            return outputs.ToArray();
        }, ct);
    }

    public async Task<float[]> EmbedAsync(string input, CancellationToken ct = default)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        var r = await _client.GenerateEmbeddingAsync(input, options: null, cancellationToken: ct);
        return r.Value.ToFloats().ToArray();
    }

    public Task<float[][]> EmbedAsync(IEnumerable<string> inputs, CancellationToken ct = default)
        => EmbedBatchedAsync(inputs, ct);
}
