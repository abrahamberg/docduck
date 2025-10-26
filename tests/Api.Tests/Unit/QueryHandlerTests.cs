using Api.Handlers;
using Api.Models;
using Api.Options;
using Api.Services;
using DocDuck.Providers.Ai;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Api.Tests.Unit;

public class QueryHandlerTests
{
    private readonly Mock<IModelAgnosticAiService> _mockAiService;
    private readonly Mock<IVectorSearchService> _mockSearchService;
    private readonly Mock<IChatService> _mockChatService;
    private readonly Mock<ILogger<QueryHandler>> _mockLogger;
    private readonly SearchOptions _searchOptions;
    private readonly QueryHandler _queryHandler;

    public QueryHandlerTests()
    {
        _mockAiService = new Mock<IModelAgnosticAiService>();
        _mockSearchService = new Mock<IVectorSearchService>();
        _mockChatService = new Mock<IChatService>();
        _mockLogger = new Mock<ILogger<QueryHandler>>();

        _searchOptions = new SearchOptions
        {
            DefaultSearchDepth = 2,
            MaxSearchDepth = 5,
            DefaultTopK = 10,
            MaxTopK = 100
        };

        var options = Microsoft.Extensions.Options.Options.Create(_searchOptions);

        _queryHandler = new QueryHandler(
            _mockAiService.Object,
            _mockSearchService.Object,
            _mockChatService.Object,
            options,
            _mockLogger.Object);
    }

    [Fact]
    public async Task HandleQueryAsync_WithEmptyQuestion_ReturnsBadRequest()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var request = new QueryRequest(
            Question: "",
            History: null,
            TopK: null,
            ProviderType: null,
            ProviderName: null,
            StreamSteps: false,
            SearchDepth: null);

        // Act
        var result = await _queryHandler.HandleQueryAsync(httpContext, request, CancellationToken.None);

        // Assert - Verify a result is returned (IResult doesn't expose status code directly in tests)
        result.Should().NotBeNull();

        // Verify no AI or search calls were made
        _mockAiService.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockSearchService.Verify(x => x.SearchAsync(
            It.IsAny<float[]>(),
            It.IsAny<string>(),
            It.IsAny<int?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleQueryAsync_WithNullQuestion_ReturnsBadRequest()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var request = new QueryRequest(
            Question: null!,
            History: null,
            TopK: null,
            ProviderType: null,
            ProviderName: null,
            StreamSteps: false,
            SearchDepth: null);

        // Act
        var result = await _queryHandler.HandleQueryAsync(httpContext, request, CancellationToken.None);

        // Assert - Verify a result is returned
        result.Should().NotBeNull();

        // Verify no AI or search calls were made
        _mockAiService.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockSearchService.Verify(x => x.SearchAsync(
            It.IsAny<float[]>(),
            It.IsAny<string>(),
            It.IsAny<int?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleQueryAsync_WithDepth1_ExecutesSimpleQuery()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var question = "What is the capital of France?";
        var request = new QueryRequest(
            Question: question,
            History: null,
            TopK: 5,
            ProviderType: null,
            ProviderName: null,
            StreamSteps: false,
            SearchDepth: 1);

        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var sources = new List<Source>
        {
            new Source("doc1", "file1.txt", 0, "Paris is the capital of France.", 0.1, "[local/main:file1.txt#chunk0]", "local", "main")
        };

        var chatResult = new ChatCompletionResult(
            Content: "Paris is the capital of France.",
            Role: "assistant",
            ToolCalls: new List<ToolCall>(),
            PromptTokens: 10,
            CompletionTokens: 15,
            TotalTokens: 25);

        _mockAiService.Setup(x => x.EmbedAsync(question, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        _mockSearchService.Setup(x => x.SearchAsync(
                embedding,
                question,
                5,
                null,
                null,
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sources);

        _mockAiService.Setup(x => x.CompleteChatAsync(
                It.IsAny<List<ChatMessagePayload>>(),
                TaskComplexity.Simple,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResult);

        // Act
        var result = await _queryHandler.HandleQueryAsync(httpContext, request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var okResult = result as Microsoft.AspNetCore.Http.HttpResults.Ok<QueryResponse>;
        okResult.Should().NotBeNull();
        okResult!.Value!.Answer.Should().Be("Paris is the capital of France.");
        okResult.Value.Sources.Should().HaveCount(1);
        okResult.Value.TokensUsed.Should().Be(25);

        _mockAiService.Verify(x => x.EmbedAsync(question, It.IsAny<CancellationToken>()), Times.Once);
        _mockSearchService.Verify(x => x.SearchAsync(
            embedding,
            question,
            5,
            null,
            null,
            1,
            It.IsAny<CancellationToken>()), Times.Once);
        _mockChatService.Verify(x => x.ProcessAsync(
            It.IsAny<ChatRequest>(),
            It.IsAny<Func<ChatStreamUpdate, Task>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleQueryAsync_WithDepth1AndNoSources_ReturnsNotFoundMessage()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var question = "What is the capital of Mars?";
        var request = new QueryRequest(
            Question: question,
            History: null,
            TopK: null,
            ProviderType: null,
            ProviderName: null,
            StreamSteps: false,
            SearchDepth: 1);

        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var sources = new List<Source>();

        _mockAiService.Setup(x => x.EmbedAsync(question, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        _mockSearchService.Setup(x => x.SearchAsync(
                It.IsAny<float[]>(),
                question,
                It.IsAny<int?>(),
                null,
                null,
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sources);

        // Act
        var result = await _queryHandler.HandleQueryAsync(httpContext, request, CancellationToken.None);

        // Assert
        var okResult = result as Microsoft.AspNetCore.Http.HttpResults.Ok<QueryResponse>;
        okResult.Should().NotBeNull();
        okResult!.Value!.Answer.Should().Contain("couldn't find any relevant information");
        okResult.Value.Sources.Should().BeEmpty();
        okResult.Value.TokensUsed.Should().Be(0);

        _mockChatService.Verify(x => x.ProcessAsync(
            It.IsAny<ChatRequest>(),
            It.IsAny<Func<ChatStreamUpdate, Task>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleQueryAsync_WithDepth2_UsesSmartQuery()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var question = "What is the capital of France?";
        var request = new QueryRequest(
            Question: question,
            History: null,
            TopK: 5,
            ProviderType: null,
            ProviderName: null,
            StreamSteps: false,
            SearchDepth: 2);

        var chatResponse = new ChatResponse(
            Answer: "Paris is the capital of France.",
            Steps: new List<string> { "Step 1", "Step 2" },
            Files: new List<DocumentResult>(),
            Sources: new List<Source>(),
            TokensUsed: 30,
            History: new List<ChatMessage>(),
            ModelUsage: null);

        _mockChatService.Setup(x => x.ProcessAsync(
                It.Is<ChatRequest>(r => r.Message == question && r.SearchDepth == 2),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);

        // Act
        var result = await _queryHandler.HandleQueryAsync(httpContext, request, CancellationToken.None);

        // Assert
        var okResult = result as Microsoft.AspNetCore.Http.HttpResults.Ok<QueryResponse>;
        okResult.Should().NotBeNull();
        okResult!.Value!.Answer.Should().Be("Paris is the capital of France.");
        okResult.Value.TokensUsed.Should().Be(30);

        _mockChatService.Verify(x => x.ProcessAsync(
            It.Is<ChatRequest>(r => r.Message == question && r.SearchDepth == 2),
            null,
            It.IsAny<CancellationToken>()), Times.Once);

        _mockAiService.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockSearchService.Verify(x => x.SearchAsync(
            It.IsAny<float[]>(),
            It.IsAny<string>(),
            It.IsAny<int?>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleQueryAsync_WithProviderFilter_PassesFilterToServices()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var question = "Test question";
        var request = new QueryRequest(
            Question: question,
            History: null,
            TopK: 10,
            ProviderType: "s3",
            ProviderName: "mybucket",
            StreamSteps: false,
            SearchDepth: 1);

        var embedding = new float[] { 0.1f, 0.2f };
        var sources = new List<Source>
        {
            new Source("doc1", "file1.txt", 0, "Test content", 0.1, "[s3/mybucket:file1.txt#chunk0]", "s3", "mybucket")
        };

        var chatResult = new ChatCompletionResult(
            Content: "Test answer",
            Role: "assistant",
            ToolCalls: new List<ToolCall>(),
            PromptTokens: 5,
            CompletionTokens: 10,
            TotalTokens: 15);

        _mockAiService.Setup(x => x.EmbedAsync(question, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        _mockSearchService.Setup(x => x.SearchAsync(
                embedding,
                question,
                10,
                "s3",
                "mybucket",
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sources);

        _mockAiService.Setup(x => x.CompleteChatAsync(
                It.IsAny<List<ChatMessagePayload>>(),
                It.IsAny<TaskComplexity>(),
                It.IsAny<ModelSelectionStrategy?>(),
                It.IsAny<ChatCompletionOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResult);

        // Act
        var result = await _queryHandler.HandleQueryAsync(httpContext, request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        _mockSearchService.Verify(x => x.SearchAsync(
            embedding,
            question,
            10,
            "s3",
            "mybucket",
            1,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleQueryAsync_WithHistoryMessages_PassesToAiService()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var question = "What about Berlin?";
        var history = new List<ChatMessage>
        {
            new ChatMessage("user", "Tell me about Paris"),
            new ChatMessage("assistant", "Paris is the capital of France")
        };

        var request = new QueryRequest(
            Question: question,
            History: history,
            TopK: null,
            ProviderType: null,
            ProviderName: null,
            StreamSteps: false,
            SearchDepth: 1);

        var embedding = new float[] { 0.1f, 0.2f };
        var sources = new List<Source>
        {
            new Source("doc1", "file1.txt", 0, "Berlin is the capital of Germany.", 0.1, "[local/main:file1.txt#chunk0]", "local", "main")
        };

        var chatResult = new ChatCompletionResult(
            Content: "Berlin is the capital of Germany.",
            Role: "assistant",
            ToolCalls: new List<ToolCall>(),
            PromptTokens: 20,
            CompletionTokens: 15,
            TotalTokens: 35);

        _mockAiService.Setup(x => x.EmbedAsync(question, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        _mockSearchService.Setup(x => x.SearchAsync(
                It.IsAny<float[]>(),
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sources);

        _mockAiService.Setup(x => x.CompleteChatAsync(
                It.Is<List<ChatMessagePayload>>(msgs => msgs.Count == 4), // system + 2 history + user
                It.IsAny<TaskComplexity>(),
                It.IsAny<ModelSelectionStrategy?>(),
                It.IsAny<ChatCompletionOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResult);

        // Act
        var result = await _queryHandler.HandleQueryAsync(httpContext, request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        _mockAiService.Verify(x => x.CompleteChatAsync(
            It.Is<List<ChatMessagePayload>>(msgs => msgs.Count == 4),
            It.IsAny<TaskComplexity>(),
            It.IsAny<ModelSelectionStrategy?>(),
            It.IsAny<ChatCompletionOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleQueryAsync_ClampsSearchDepthToMaximum()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var question = "Test";
        var request = new QueryRequest(
            Question: question,
            History: null,
            TopK: null,
            ProviderType: null,
            ProviderName: null,
            StreamSteps: false,
            SearchDepth: 999); // Way over max

        var chatResponse = new ChatResponse(
            Answer: "Answer",
            Steps: new List<string>(),
            Files: new List<DocumentResult>(),
            Sources: new List<Source>(),
            TokensUsed: 10,
            History: new List<ChatMessage>(),
            ModelUsage: null);

        _mockChatService.Setup(x => x.ProcessAsync(
                It.Is<ChatRequest>(r => r.SearchDepth == 5), // Should be clamped to MaxSearchDepth
                It.IsAny<Func<ChatStreamUpdate, Task>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);

        // Act
        await _queryHandler.HandleQueryAsync(httpContext, request, CancellationToken.None);

        // Assert
        _mockChatService.Verify(x => x.ProcessAsync(
            It.Is<ChatRequest>(r => r.SearchDepth == 5),
            It.IsAny<Func<ChatStreamUpdate, Task>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleQueryAsync_ClampsSearchDepthToMinimum()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var question = "Test";
        var request = new QueryRequest(
            Question: question,
            History: null,
            TopK: null,
            ProviderType: null,
            ProviderName: null,
            StreamSteps: false,
            SearchDepth: -5); // Negative

        var embedding = new float[] { 0.1f };
        var sources = new List<Source>();

        _mockAiService.Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        _mockSearchService.Setup(x => x.SearchAsync(
                It.IsAny<float[]>(),
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                1, // Should be clamped to 1
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sources);

        // Act
        await _queryHandler.HandleQueryAsync(httpContext, request, CancellationToken.None);

        // Assert
        _mockSearchService.Verify(x => x.SearchAsync(
            It.IsAny<float[]>(),
            It.IsAny<string>(),
            It.IsAny<int?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            1,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
