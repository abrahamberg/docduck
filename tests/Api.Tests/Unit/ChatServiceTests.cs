using Api.Models;
using Api.Options;
using Api.Services;
using Api.Services.Interfaces;
using DocDuck.Providers.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Api.Tests.Unit;

public class ChatServiceTests
{
    private readonly Mock<IVectorSearchService> _mockSearchService;
    private readonly Mock<IModelAgnosticAiService> _mockAiService;
    private readonly Mock<ILogger<ChatService>> _mockLogger;
    private readonly SearchOptions _searchOptions;
    private readonly ChatService _chatService;

    public ChatServiceTests()
    {
        _mockSearchService = new Mock<IVectorSearchService>();
        _mockAiService = new Mock<IModelAgnosticAiService>();
        _mockLogger = new Mock<ILogger<ChatService>>();

        _searchOptions = new SearchOptions
        {
            DefaultSearchDepth = 2,
            MaxSearchDepth = 5,
            DefaultTopK = 10,
            MaxTopK = 100,
            EnableLexicalSearch = true,
            LexicalScoreWeight = 0.3,
            MaxLexicalResults = 30,
            LexicalConfiguration = "english"
        };

        var options = Microsoft.Extensions.Options.Options.Create(_searchOptions);

        _chatService = new ChatService(
            _mockSearchService.Object,
            _mockAiService.Object,
            options,
            _mockLogger.Object);
    }

    [Fact]
    public async Task ProcessAsync_WithSuccessfulAnswer_ReturnsResponseWithSteps()
    {
        // Arrange
        var request = new ChatRequest(
            Message: "What is the capital of France?",
            History: null,
            TopK: null,
            ProviderType: null,
            ProviderName: null,
            StreamSteps: false,
            SearchDepth: 2);

        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var sources = new List<Source>
        {
            new Source("doc1", "file1.txt", 0, "Paris is the capital of France.", 0.1, "[local/main:file1.txt#chunk0]", "local", "main")
        };

        // Setup refinement call (rephrases query)
        var refinedResult = new ChatCompletionResult(
            Content: "capital of France",
            Role: "assistant",
            ToolCalls: new List<ToolCall>(),
            PromptTokens: 10,
            CompletionTokens: 5,
            TotalTokens: 15);

        // Setup evaluation call (answer_ready tool)
        var evaluationResult = new ChatCompletionResult(
            Content: "",
            Role: "assistant",
            ToolCalls: new List<ToolCall>
            {
                new ToolCall(
                    Id: "call_1",
                    FunctionName: "answer_ready",
                    ArgumentsJson: "{\"confidence\":\"high\",\"reasoning\":\"Context is sufficient\"}")
            },
            PromptTokens: 50,
            CompletionTokens: 20,
            TotalTokens: 70);

        // Setup answer generation call
        var answerResult = new ChatCompletionResult(
            Content: "Paris is the capital of France.",
            Role: "assistant",
            ToolCalls: new List<ToolCall>(),
            PromptTokens: 60,
            CompletionTokens: 30,
            TotalTokens: 90);

        _mockAiService.SetupSequence(x => x.CompleteChatAsync(
                It.IsAny<List<ChatMessagePayload>>(),
                It.IsAny<TaskComplexity>(),
                It.IsAny<ModelSelectionStrategy?>(),
                It.IsAny<ChatCompletionOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(refinedResult)      // First call: refine query
            .ReturnsAsync(evaluationResult)    // Second call: evaluate context
            .ReturnsAsync(answerResult);       // Third call: generate answer

        _mockAiService.Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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

        // Act
        var response = await _chatService.ProcessAsync(request, null, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Answer.Should().Be("Paris is the capital of France.");
        response.Sources.Should().HaveCount(1);
        response.History.Should().NotBeEmpty();
        response.History.Should().Contain(m => m.Role == "user" && m.Content == request.Message);
        response.History.Should().Contain(m => m.Role == "assistant" && m.Content.Contains("Paris"));

        _mockAiService.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockSearchService.Verify(x => x.SearchAsync(
            It.IsAny<float[]>(),
            It.IsAny<string>(),
            It.IsAny<int?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithNoSources_ReturnsCannotAnswerMessage()
    {
        // Arrange
        var request = new ChatRequest(
            Message: "What is the capital of Mars?",
            History: null,
            TopK: null,
            ProviderType: null,
            ProviderName: null,
            StreamSteps: false,
            SearchDepth: 1);

        var embedding = new float[] { 0.1f, 0.2f };
        var emptySources = new List<Source>();

        var refinedResult = new ChatCompletionResult(
            Content: "capital of Mars",
            Role: "assistant",
            ToolCalls: new List<ToolCall>(),
            PromptTokens: 10,
            CompletionTokens: 5,
            TotalTokens: 15);

        _mockAiService.Setup(x => x.CompleteChatAsync(
                It.IsAny<List<ChatMessagePayload>>(),
                It.IsAny<TaskComplexity>(),
                It.IsAny<ModelSelectionStrategy?>(),
                It.IsAny<ChatCompletionOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(refinedResult);

        _mockAiService.Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        _mockSearchService.Setup(x => x.SearchAsync(
                It.IsAny<float[]>(),
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptySources);

        // Act
        var response = await _chatService.ProcessAsync(request, null, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Answer.Should().Contain("rephrase");
        response.Sources.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_WithCannotAnswerDecision_ReturnsCannotAnswerResponse()
    {
        // Arrange
        var request = new ChatRequest(
            Message: "How do I hack NASA?",
            History: null,
            TopK: null,
            ProviderType: null,
            ProviderName: null,
            StreamSteps: false,
            SearchDepth: 2);

        var embedding = new float[] { 0.1f, 0.2f };
        var sources = new List<Source>
        {
            new Source("doc1", "file1.txt", 0, "Unrelated content about astronomy.", 0.8, "[local/main:file1.txt#chunk0]", "local", "main")
        };

        var refinedResult = new ChatCompletionResult(
            Content: "hack NASA",
            Role: "assistant",
            ToolCalls: new List<ToolCall>(),
            PromptTokens: 10,
            CompletionTokens: 5,
            TotalTokens: 15);

        var cannotAnswerResult = new ChatCompletionResult(
            Content: "",
            Role: "assistant",
            ToolCalls: new List<ToolCall>
            {
                new ToolCall(
                    Id: "call_1",
                    FunctionName: "cannot_answer",
                    ArgumentsJson: "{\"reason\":\"inappropriate_question\",\"explanation\":\"This question is outside the scope of documentation.\"}")
            },
            PromptTokens: 50,
            CompletionTokens: 20,
            TotalTokens: 70);

        _mockAiService.SetupSequence(x => x.CompleteChatAsync(
                It.IsAny<List<ChatMessagePayload>>(),
                It.IsAny<TaskComplexity>(),
                It.IsAny<ModelSelectionStrategy?>(),
                It.IsAny<ChatCompletionOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(refinedResult)
            .ReturnsAsync(cannotAnswerResult);

        _mockAiService.Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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

        // Act
        var response = await _chatService.ProcessAsync(request, null, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Answer.Should().Contain("cannot answer");
        response.Answer.Should().Contain("outside the scope");
    }

    [Fact]
    public async Task ProcessAsync_WithProviderFilter_PassesToSearchService()
    {
        // Arrange
        var request = new ChatRequest(
            Message: "Test question",
            History: null,
            TopK: 5,
            ProviderType: "s3",
            ProviderName: "mybucket",
            StreamSteps: false,
            SearchDepth: 2);

        var embedding = new float[] { 0.1f, 0.2f };
        var sources = new List<Source>
        {
            new Source("doc1", "file1.txt", 0, "Content", 0.1, "[s3/mybucket:file1.txt#chunk0]", "s3", "mybucket")
        };

        var refinedResult = new ChatCompletionResult(
            Content: "refined query",
            Role: "assistant",
            ToolCalls: new List<ToolCall>(),
            PromptTokens: 10,
            CompletionTokens: 5,
            TotalTokens: 15);

        var evaluationResult = new ChatCompletionResult(
            Content: "",
            Role: "assistant",
            ToolCalls: new List<ToolCall>
            {
                new ToolCall("call_1", "answer_ready", "{\"confidence\":\"high\",\"reasoning\":\"Good\"}")
            },
            PromptTokens: 20,
            CompletionTokens: 10,
            TotalTokens: 30);

        var answerResult = new ChatCompletionResult(
            Content: "Answer",
            Role: "assistant",
            ToolCalls: new List<ToolCall>(),
            PromptTokens: 30,
            CompletionTokens: 15,
            TotalTokens: 45);

        _mockAiService.SetupSequence(x => x.CompleteChatAsync(
                It.IsAny<List<ChatMessagePayload>>(),
                It.IsAny<TaskComplexity>(),
                It.IsAny<ModelSelectionStrategy?>(),
                It.IsAny<ChatCompletionOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(refinedResult)
            .ReturnsAsync(evaluationResult)
            .ReturnsAsync(answerResult);

        _mockAiService.Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        _mockSearchService.Setup(x => x.SearchAsync(
                It.IsAny<float[]>(),
                It.IsAny<string>(),
                5,
                "s3",
                "mybucket",
                2,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sources);

        // Act
        await _chatService.ProcessAsync(request, null, CancellationToken.None);

        // Assert
        _mockSearchService.Verify(x => x.SearchAsync(
            It.IsAny<float[]>(),
            It.IsAny<string>(),
            5,
            "s3",
            "mybucket",
            2,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithConversationHistory_IncludesInRefinement()
    {
        // Arrange
        var history = new List<ChatMessage>
        {
            new ChatMessage("user", "What is Paris?"),
            new ChatMessage("assistant", "Paris is the capital of France.")
        };

        var request = new ChatRequest(
            Message: "What about its population?",
            History: history,
            TopK: null,
            ProviderType: null,
            ProviderName: null,
            StreamSteps: false,
            SearchDepth: 2);

        var embedding = new float[] { 0.1f };
        var sources = new List<Source>
        {
            new Source("doc1", "file1.txt", 0, "Paris has about 2.1 million residents.", 0.1, "[local/main:file1.txt#chunk0]", "local", "main")
        };

        var refinedResult = new ChatCompletionResult(
            Content: "population of Paris France",
            Role: "assistant",
            ToolCalls: new List<ToolCall>(),
            PromptTokens: 20,
            CompletionTokens: 10,
            TotalTokens: 30);

        var evaluationResult = new ChatCompletionResult(
            Content: "",
            Role: "assistant",
            ToolCalls: new List<ToolCall>
            {
                new ToolCall("call_1", "answer_ready", "{\"confidence\":\"high\",\"reasoning\":\"Context sufficient\"}")
            },
            PromptTokens: 30,
            CompletionTokens: 15,
            TotalTokens: 45);

        var answerResult = new ChatCompletionResult(
            Content: "Paris has about 2.1 million residents.",
            Role: "assistant",
            ToolCalls: new List<ToolCall>(),
            PromptTokens: 40,
            CompletionTokens: 20,
            TotalTokens: 60);

        _mockAiService.SetupSequence(x => x.CompleteChatAsync(
                It.IsAny<List<ChatMessagePayload>>(),
                It.IsAny<TaskComplexity>(),
                It.IsAny<ModelSelectionStrategy?>(),
                It.IsAny<ChatCompletionOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(refinedResult)
            .ReturnsAsync(evaluationResult)
            .ReturnsAsync(answerResult);

        _mockAiService.Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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

        // Act
        var response = await _chatService.ProcessAsync(request, null, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.History.Should().Contain(m => m.Content == "What is Paris?");
        response.History.Should().Contain(m => m.Content.Contains("population"));
    }

    [Fact]
    public async Task ProcessAsync_WithProgressCallback_CallsProgressForSteps()
    {
        // Arrange
        var progressCalls = new List<ChatStreamUpdate>();

        var request = new ChatRequest(
            Message: "Test",
            History: null,
            TopK: null,
            ProviderType: null,
            ProviderName: null,
            StreamSteps: true,
            SearchDepth: 2);

        var embedding = new float[] { 0.1f };
        var sources = new List<Source>
        {
            new Source("doc1", "file1.txt", 0, "Content", 0.1, "[local/main:file1.txt#chunk0]", "local", "main")
        };

        var refinedResult = new ChatCompletionResult(
            Content: "refined test",
            Role: "assistant",
            ToolCalls: new List<ToolCall>(),
            PromptTokens: 10,
            CompletionTokens: 5,
            TotalTokens: 15);

        var evaluationResult = new ChatCompletionResult(
            Content: "",
            Role: "assistant",
            ToolCalls: new List<ToolCall>
            {
                new ToolCall("call_1", "answer_ready", "{\"confidence\":\"high\",\"reasoning\":\"OK\"}")
            },
            PromptTokens: 20,
            CompletionTokens: 10,
            TotalTokens: 30);

        var answerResult = new ChatCompletionResult(
            Content: "Answer",
            Role: "assistant",
            ToolCalls: new List<ToolCall>(),
            PromptTokens: 30,
            CompletionTokens: 15,
            TotalTokens: 45);

        _mockAiService.SetupSequence(x => x.CompleteChatAsync(
                It.IsAny<List<ChatMessagePayload>>(),
                It.IsAny<TaskComplexity>(),
                It.IsAny<ModelSelectionStrategy?>(),
                It.IsAny<ChatCompletionOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(refinedResult)
            .ReturnsAsync(evaluationResult)
            .ReturnsAsync(answerResult);

        _mockAiService.Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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

        // Act
        var response = await _chatService.ProcessAsync(request, update =>
        {
            progressCalls.Add(update);
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        progressCalls.Should().NotBeEmpty();
        progressCalls.Should().Contain(u => u.Type == "step");
        progressCalls.Should().Contain(u => u.Type == "final");
        progressCalls.Last().Type.Should().Be("final");
    }
}
