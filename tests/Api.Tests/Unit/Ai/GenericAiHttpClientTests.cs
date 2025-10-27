using DocDuck.Providers.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DocDuck.Tests.Unit.Ai;

/// <summary>
/// Tests for GenericAiHttpClient focusing on validation logic and object construction.
/// HTTP integration tests are excluded as GenericAiHttpClient creates its own HttpClient internally.
/// For full integration testing with actual HTTP responses, see integration test suite.
/// </summary>
public class GenericAiHttpClientTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();
    private static readonly string[] TestTextArray = new[] { "test" };

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable?.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_ThrowsWhenModelIsNull()
    {
        var act = () => new GenericAiHttpClient(null!, NullLogger.Instance);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("model");
    }

    [Fact]
    public void Constructor_ThrowsWhenUrlIsNullOrWhitespace()
    {
        var model = new AiModelAssignment
        {
            Id = "test-model",
            DisplayName = "Test Model",
            ModelId = "test-model",
            Url = null!,
            Headers = new Dictionary<string, string>(),
            RequestTemplate = null,
            ResponseMapping = null,
            DefaultParams = new Dictionary<string, System.Text.Json.JsonElement>(),
            MaxOutputTokens = 1000,
            TimeoutSeconds = 30,
            SupportsFunctionCalling = false,
            Enabled = true
        };

        var act = () => new GenericAiHttpClient(model, NullLogger.Instance);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*URL is required*");
    }

    [Fact]
    public void Constructor_AcceptsValidModel()
    {
        var model = CreateValidModel();

        var client = new GenericAiHttpClient(model, NullLogger.Instance);
        _disposables.Add(client);

        client.Should().NotBeNull();
    }

    #endregion

    #region CompleteChatAsync - Validation Tests

    [Fact]
    public async Task CompleteChatAsync_ThrowsWhenMessagesIsNull()
    {
        var client = CreateClient();

        var act = async () => await client.CompleteChatAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("messages");
    }

    [Fact]
    public async Task CompleteChatAsync_ThrowsWhenMessagesIsEmpty()
    {
        var client = CreateClient();

        var act = async () => await client.CompleteChatAsync(new List<ChatMessagePayload>());

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("messages")
            .WithMessage("At least one message is required*");
    }

    #endregion

    #region EmbedAsync - Validation Tests

    [Fact]
    public async Task EmbedAsync_ThrowsWhenEmbeddingModelIsNull()
    {
        var client = CreateClient();

        var act = async () => await client.EmbedAsync(null!, "test");

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("embeddingModel");
    }

    [Fact]
    public async Task EmbedAsync_ThrowsWhenTextIsNullOrWhitespace()
    {
        var client = CreateClient();
        var embeddingModel = CreateValidEmbeddingModel();

        var act1 = async () => await client.EmbedAsync(embeddingModel, null!);
        await act1.Should().ThrowAsync<ArgumentException>();

        var act2 = async () => await client.EmbedAsync(embeddingModel, "");
        await act2.Should().ThrowAsync<ArgumentException>();

        var act3 = async () => await client.EmbedAsync(embeddingModel, "   ");
        await act3.Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region EmbedBatchAsync - Validation Tests

    [Fact]
    public async Task EmbedBatchAsync_ThrowsWhenEmbeddingModelIsNull()
    {
        var client = CreateClient();

        var act = async () => await client.EmbedBatchAsync(null!, TestTextArray);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("embeddingModel");
    }

    [Fact]
    public async Task EmbedBatchAsync_ThrowsWhenTextsIsNull()
    {
        var client = CreateClient();
        var embeddingModel = CreateValidEmbeddingModel();

        var act = async () => await client.EmbedBatchAsync(embeddingModel, null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("texts");
    }

    [Fact]
    public async Task EmbedBatchAsync_ReturnsEmptyArray_WhenTextsIsEmpty()
    {
        var client = CreateClient();
        var embeddingModel = CreateValidEmbeddingModel();

        var result = await client.EmbedBatchAsync(embeddingModel, Array.Empty<string>());

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    #endregion

    #region Helper Methods

    private GenericAiHttpClient CreateClient(AiModelAssignment? model = null)
    {
        model ??= CreateValidModel();

        var client = new GenericAiHttpClient(model, NullLogger.Instance);
        _disposables.Add(client);
        return client;
    }

    private static AiModelAssignment CreateValidModel()
    {
        return new AiModelAssignment
        {
            Id = "test-model",
            DisplayName = "Test Model",
            ModelId = "gpt-4",
            Url = "https://api.openai.com/v1/",
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer test-key"
            },
            RequestTemplate = null,
            ResponseMapping = null,
            DefaultParams = new Dictionary<string, System.Text.Json.JsonElement>(),
            MaxOutputTokens = 2000,
            TimeoutSeconds = 60,
            SupportsFunctionCalling = false,
            Enabled = true
        };
    }

    private static AiEmbeddingModelAssignment CreateValidEmbeddingModel()
    {
        return new AiEmbeddingModelAssignment
        {
            Id = "test-embedding",
            DisplayName = "Test Embedding Model",
            ModelId = "text-embedding-ada-002",
            Url = "https://api.openai.com/v1/embeddings",
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer test-key"
            },
            RequestTemplate = null,
            ResponseMapping = new Dictionary<string, string>(),
            TimeoutSeconds = 60
        };
    }

    #endregion
}
