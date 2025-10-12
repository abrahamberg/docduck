using Indexer.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Indexer.Services;

/// <summary>
/// Client for OpenAI embeddings API with batching support.
/// </summary>
public class OpenAiEmbeddingsClient
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;
    private readonly ILogger<OpenAiEmbeddingsClient> _logger;

    public OpenAiEmbeddingsClient(
        HttpClient httpClient,
        IOptions<OpenAiOptions> options,
        ILogger<OpenAiEmbeddingsClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        // Ensure BaseUrl ends with a slash for proper relative path resolution
        var baseUrl = _options.BaseUrl.TrimEnd('/') + "/";
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.ApiKey}");
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    /// <summary>
    /// Generates embeddings for a batch of input texts.
    /// </summary>
    public async Task<float[][]> EmbedAsync(IEnumerable<string> inputs, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var inputList = inputs.ToList();
        if (inputList.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        var request = new EmbeddingRequest
        {
            Model = _options.EmbedModel,
            Input = inputList
        };

        try
        {
            // Use relative path (no leading slash) so it appends to BaseAddress
            var response = await _httpClient.PostAsJsonAsync("embeddings", request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: ct);

            if (result?.Data == null || result.Data.Count == 0)
            {
                throw new InvalidOperationException("OpenAI API returned no embeddings");
            }

            var embeddings = result.Data
                .OrderBy(d => d.Index)
                .Select(d => d.Embedding)
                .ToArray();

            _logger.LogDebug("Generated {Count} embeddings", embeddings.Length);
            return embeddings;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request to OpenAI failed");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embeddings");
            throw;
        }
    }

    /// <summary>
    /// Generates embeddings with automatic batching for large input sets.
    /// </summary>
    public async Task<float[][]> EmbedBatchedAsync(IEnumerable<string> inputs, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var inputList = inputs.ToList();
        var batchSize = _options.BatchSize;
        var allEmbeddings = new List<float[]>();

        for (var i = 0; i < inputList.Count; i += batchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = inputList.Skip(i).Take(batchSize);
            var embeddings = await EmbedAsync(batch, ct);
            allEmbeddings.AddRange(embeddings);

            _logger.LogDebug("Processed batch {Current}/{Total}",
                Math.Min(i + batchSize, inputList.Count), inputList.Count);
        }

        return allEmbeddings.ToArray();
    }

    private record EmbeddingRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; init; } = string.Empty;

        [JsonPropertyName("input")]
        public List<string> Input { get; init; } = new();
    }

    private record EmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingData> Data { get; init; } = new();
    }

    private record EmbeddingData
    {
        [JsonPropertyName("index")]
        public int Index { get; init; }

        [JsonPropertyName("embedding")]
        public float[] Embedding { get; init; } = Array.Empty<float>();
    }
}
