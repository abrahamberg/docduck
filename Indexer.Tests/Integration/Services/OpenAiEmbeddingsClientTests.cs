using Indexer.Options;
using Indexer.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Indexer.Tests.Integration.Services;

/// <summary>
/// Integration tests for OpenAiEmbeddingsClient.
/// These tests require a valid OpenAI API key to run.
/// Set OPENAI_API_KEY environment variable or skip with: dotnet test --filter "Category!=Integration"
/// </summary>
[Collection("OpenAiIntegration")]
[Trait("Category", "Integration")]
public class OpenAiEmbeddingsClientTests : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiEmbeddingsClient _client;
    private readonly ILogger<OpenAiEmbeddingsClient> _logger;
    private readonly bool _skipTests;

    public OpenAiEmbeddingsClientTests()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        _skipTests = string.IsNullOrEmpty(apiKey);

        if (_skipTests)
        {
            // Create dummy objects for skipped tests
            _httpClient = new HttpClient();
            _logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<OpenAiEmbeddingsClient>();
            _client = null!;
            return;
        }

        var options = Microsoft.Extensions.Options.Options.Create(new OpenAiOptions
        {
            ApiKey = apiKey!,
            BaseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "https://api.openai.com/v1",
            EmbedModel = Environment.GetEnvironmentVariable("OPENAI_EMBED_MODEL") ?? "text-embedding-3-small",
            BatchSize = 10
        });

        _httpClient = new HttpClient();
        _logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<OpenAiEmbeddingsClient>();
        _client = new OpenAiEmbeddingsClient(_httpClient, options, _logger);
    }

    [SkippableFact]
    public async Task EmbedAsync_WithSingleInput_ReturnsEmbedding()
    {
        // Skip test if no API key
        Skip.If(_skipTests, "OPENAI_API_KEY environment variable not set");

        // Arrange
        var input = new[] { "Hello, world!" };

        // Act
        var embeddings = await _client.EmbedAsync(input);

        // Assert
        embeddings.Should().NotBeNull();
        embeddings.Should().HaveCount(1);
        embeddings[0].Should().NotBeNull();
        embeddings[0].Length.Should().Be(1536); // text-embedding-3-small dimension
        embeddings[0].Should().AllSatisfy(v => v.Should().BeInRange(-1f, 1f));
    }

    [SkippableFact]
    public async Task EmbedAsync_WithMultipleInputs_ReturnsMultipleEmbeddings()
    {
        // Skip test if no API key
        Skip.If(_skipTests, "OPENAI_API_KEY environment variable not set");

        // Arrange
        var inputs = new[]
        {
            "The quick brown fox jumps over the lazy dog",
            "Machine learning is a subset of artificial intelligence",
            "Vector databases are used for similarity search"
        };

        // Act
        var embeddings = await _client.EmbedAsync(inputs);

        // Assert
        embeddings.Should().NotBeNull();
        embeddings.Should().HaveCount(3);
        
        foreach (var embedding in embeddings)
        {
            embedding.Should().NotBeNull();
            embedding.Length.Should().Be(1536);
            embedding.Should().AllSatisfy(v => v.Should().BeInRange(-1f, 1f));
        }

        // Verify that different inputs produce different embeddings
        var similarity1 = CosineSimilarity(embeddings[0], embeddings[1]);
        var similarity2 = CosineSimilarity(embeddings[0], embeddings[2]);
        
        // Different topics should produce different embeddings (not be identical)
        similarity1.Should().BeInRange(0.0f, 0.99f);
        similarity2.Should().BeInRange(0.0f, 0.99f);
        similarity1.Should().NotBe(similarity2); // They should be different from each other
    }

    [SkippableFact]
    public async Task EmbedAsync_WithEmptyInput_ReturnsEmptyArray()
    {
        // Skip test if no API key
        Skip.If(_skipTests, "OPENAI_API_KEY environment variable not set");

        // Arrange
        var inputs = Array.Empty<string>();

        // Act
        var embeddings = await _client.EmbedAsync(inputs);

        // Assert
        embeddings.Should().NotBeNull();
        embeddings.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task EmbedAsync_WithNullInput_ThrowsArgumentNullException()
    {
        // Skip test if no API key
        Skip.If(_skipTests, "OPENAI_API_KEY environment variable not set");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _client.EmbedAsync(null!));
    }

    [SkippableFact]
    public async Task EmbedBatchedAsync_WithLargeInput_ProcessesInBatches()
    {
        // Skip test if no API key
        Skip.If(_skipTests, "OPENAI_API_KEY environment variable not set");

        // Arrange
        // Create 25 inputs (should be processed in 3 batches with BatchSize=10)
        var inputs = Enumerable.Range(1, 25)
            .Select(i => $"This is test sentence number {i} for embedding.")
            .ToList();

        // Act
        var embeddings = await _client.EmbedBatchedAsync(inputs);

        // Assert
        embeddings.Should().NotBeNull();
        embeddings.Should().HaveCount(25);
        
        foreach (var embedding in embeddings)
        {
            embedding.Should().NotBeNull();
            embedding.Length.Should().Be(1536);
            embedding.Should().AllSatisfy(v => v.Should().BeInRange(-1f, 1f));
        }
    }

    [SkippableFact]
    public async Task EmbedBatchedAsync_WithSmallInput_ProcessesInSingleBatch()
    {
        // Skip test if no API key
        Skip.If(_skipTests, "OPENAI_API_KEY environment variable not set");

        // Arrange
        var inputs = new[]
        {
            "First sentence",
            "Second sentence",
            "Third sentence"
        };

        // Act
        var embeddings = await _client.EmbedBatchedAsync(inputs);

        // Assert
        embeddings.Should().NotBeNull();
        embeddings.Should().HaveCount(3);
        
        foreach (var embedding in embeddings)
        {
            embedding.Should().NotBeNull();
            embedding.Length.Should().Be(1536);
        }
    }

    [SkippableFact]
    public async Task EmbedAsync_WithCancellationToken_CanBeCancelled()
    {
        // Skip test if no API key
        Skip.If(_skipTests, "OPENAI_API_KEY environment variable not set");

        // Arrange
        var inputs = Enumerable.Range(1, 100)
            .Select(i => $"Sentence {i}")
            .ToList();

        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _client.EmbedBatchedAsync(inputs, cts.Token));
    }

    [SkippableFact]
    public async Task EmbedAsync_SimilarTexts_ProduceSimilarEmbeddings()
    {
        // Skip test if no API key
        Skip.If(_skipTests, "OPENAI_API_KEY environment variable not set");

        // Arrange
        var inputs = new[]
        {
            "The cat sat on the mat",
            "A feline rested on the rug",
            "Quantum physics is fascinating"
        };

        // Act
        var embeddings = await _client.EmbedAsync(inputs);

        // Assert
        var similarity12 = CosineSimilarity(embeddings[0], embeddings[1]); // Similar sentences
        var similarity13 = CosineSimilarity(embeddings[0], embeddings[2]); // Dissimilar sentences

        // Similar sentences should have higher similarity than dissimilar ones
        similarity12.Should().BeGreaterThan(similarity13);
        similarity12.Should().BeGreaterThan(0.5f); // Similar texts should have reasonable similarity
        similarity13.Should().BeLessThan(0.8f); // Dissimilar texts should have lower similarity
    }

    [SkippableFact]
    public async Task EmbedAsync_SameTextTwice_ProducesIdenticalEmbeddings()
    {
        // Skip test if no API key
        Skip.If(_skipTests, "OPENAI_API_KEY environment variable not set");

        // Arrange
        var text = "This is a test sentence for deterministic embedding generation.";
        var inputs = new[] { text, text };

        // Act
        var embeddings = await _client.EmbedAsync(inputs);

        // Assert
        embeddings.Should().HaveCount(2);
        
        var similarity = CosineSimilarity(embeddings[0], embeddings[1]);
        similarity.Should().BeGreaterThan(0.9999f); // Should be nearly identical (allowing for floating point precision)
        
        // Check element-wise equality (within floating point tolerance)
        for (int i = 0; i < embeddings[0].Length; i++)
        {
            Math.Abs(embeddings[0][i] - embeddings[1][i]).Should().BeLessThan(0.0001f);
        }
    }

    [SkippableFact]
    public async Task EmbedAsync_WithLongText_SucceedsWithinTokenLimit()
    {
        // Skip test if no API key
        Skip.If(_skipTests, "OPENAI_API_KEY environment variable not set");

        // Arrange
        // Create a reasonably long text (but within token limits ~8000 tokens for text-embedding-3-small)
        var longText = string.Join(" ", Enumerable.Repeat("This is a sentence in a longer document.", 100));
        var inputs = new[] { longText };

        // Act
        var embeddings = await _client.EmbedAsync(inputs);

        // Assert
        embeddings.Should().NotBeNull();
        embeddings.Should().HaveCount(1);
        embeddings[0].Should().NotBeNull();
        embeddings[0].Length.Should().Be(1536);
    }

    /// <summary>
    /// Calculates cosine similarity between two vectors.
    /// Returns a value between -1 and 1, where 1 means identical vectors.
    /// </summary>
    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must have the same length");

        var dotProduct = 0f;
        var magnitudeA = 0f;
        var magnitudeB = 0f;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        magnitudeA = (float)Math.Sqrt(magnitudeA);
        magnitudeB = (float)Math.Sqrt(magnitudeB);

        if (magnitudeA == 0 || magnitudeB == 0)
            return 0f;

        return dotProduct / (magnitudeA * magnitudeB);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        GC.SuppressFinalize(this);
    }
}
