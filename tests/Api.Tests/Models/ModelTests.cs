using Api.Models;
using FluentAssertions;

namespace Api.Tests.Models;

[Trait("Category", "Unit")]
public class ChatResponseTests
{
    [Fact]
    public void ChatResponse_WithAllProperties_SetsCorrectly()
    {
        // Arrange
        var answer = "Test answer";
        var steps = new List<string> { "Step 1", "Step 2" };
        var files = new List<DocumentResult>
        {
            new DocumentResult("doc1", "file.txt", "local/docs:file.txt", "content", 0.5)
        };
        var sources = new List<Source>
        {
            new Source("doc1", "file.txt", 1, "content", 0.5, "[local/docs:file.txt#chunk1]", "local", "docs")
        };
        var tokens = 100;
        var history = new List<ChatMessage>
        {
            new ChatMessage("user", "question"),
            new ChatMessage("assistant", "answer")
        };
        var modelUsage = new List<ModelUsageInfo>
        {
            new ModelUsageInfo("gpt-4", "chat", 50)
        };

        // Act
        var response = new ChatResponse(answer, steps, files, sources, tokens, history, modelUsage);

        // Assert
        response.Answer.Should().Be(answer);
        response.Steps.Should().BeEquivalentTo(steps);
        response.Files.Should().BeEquivalentTo(files);
        response.Sources.Should().BeEquivalentTo(sources);
        response.TokensUsed.Should().Be(tokens);
        response.History.Should().BeEquivalentTo(history);
        response.ModelUsage.Should().BeEquivalentTo(modelUsage);
    }

    [Fact]
    public void ChatResponse_WithNullModelUsage_HandlesCorrectly()
    {
        // Arrange & Act
        var response = new ChatResponse(
            "answer",
            new List<string>(),
            new List<DocumentResult>(),
            new List<Source>(),
            50,
            new List<ChatMessage>(),
            null);

        // Assert
        response.ModelUsage.Should().BeNull();
    }
}

[Trait("Category", "Unit")]
public class ChatMessageTests
{
    [Fact]
    public void ChatMessage_ConstructsCorrectly()
    {
        // Arrange & Act
        var message = new ChatMessage("user", "Hello");

        // Assert
        message.Role.Should().Be("user");
        message.Content.Should().Be("Hello");
    }

    [Fact]
    public void ChatMessage_WithDifferentRoles_StoresCorrectly()
    {
        // Arrange & Act
        var userMessage = new ChatMessage("user", "Question");
        var assistantMessage = new ChatMessage("assistant", "Answer");
        var systemMessage = new ChatMessage("system", "Instructions");

        // Assert
        userMessage.Role.Should().Be("user");
        assistantMessage.Role.Should().Be("assistant");
        systemMessage.Role.Should().Be("system");
    }
}

[Trait("Category", "Unit")]
public class SourceTests
{
    [Fact]
    public void Source_WithAllParameters_ConstructsCorrectly()
    {
        // Arrange & Act
        var source = new Source(
            DocId: "doc123",
            Filename: "test.md",
            ChunkNum: 5,
            Text: "Sample text content",
            Distance: 0.25,
            Citation: "[local/docs:test.md#chunk5]",
            ProviderType: "local",
            ProviderName: "docs"
        );

        // Assert
        source.DocId.Should().Be("doc123");
        source.Filename.Should().Be("test.md");
        source.ChunkNum.Should().Be(5);
        source.Text.Should().Be("Sample text content");
        source.Distance.Should().Be(0.25);
        source.Citation.Should().Be("[local/docs:test.md#chunk5]");
        source.ProviderType.Should().Be("local");
        source.ProviderName.Should().Be("docs");
    }

    [Fact]
    public void Source_WithNullProvider_HandlesCorrectly()
    {
        // Arrange & Act
        var source = new Source(
            "doc1",
            "file.txt",
            1,
            "text",
            0.5,
            "[file.txt#chunk1]");

        // Assert
        source.ProviderType.Should().BeNull();
        source.ProviderName.Should().BeNull();
    }
}

[Trait("Category", "Unit")]
public class DocumentResultTests
{
    [Fact]
    public void DocumentResult_WithAllParameters_ConstructsCorrectly()
    {
        // Arrange & Act
        var result = new DocumentResult(
            DocId: "doc456",
            Filename: "readme.md",
            Address: "s3/bucket:readme.md",
            Text: "Document snippet",
            Distance: 0.15,
            ProviderType: "s3",
            ProviderName: "bucket"
        );

        // Assert
        result.DocId.Should().Be("doc456");
        result.Filename.Should().Be("readme.md");
        result.Address.Should().Be("s3/bucket:readme.md");
        result.Text.Should().Be("Document snippet");
        result.Distance.Should().Be(0.15);
        result.ProviderType.Should().Be("s3");
        result.ProviderName.Should().Be("bucket");
    }
}

[Trait("Category", "Unit")]
public class ModelUsageInfoTests
{
    [Fact]
    public void ModelUsageInfo_ConstructsCorrectly()
    {
        // Arrange & Act
        var usage = new ModelUsageInfo("gpt-4-turbo", "query_refinement", 75);

        // Assert
        usage.ModelId.Should().Be("gpt-4-turbo");
        usage.Purpose.Should().Be("query_refinement");
        usage.Tokens.Should().Be(75);
    }

    [Fact]
    public void ModelUsageInfo_WithZeroTokens_HandlesCorrectly()
    {
        // Arrange & Act
        var usage = new ModelUsageInfo("model", "purpose", 0);

        // Assert
        usage.Tokens.Should().Be(0);
    }
}

[Trait("Category", "Unit")]
public class ChatRequestTests
{
    [Fact]
    public void ChatRequest_WithMinimalParameters_SetsDefaults()
    {
        // Arrange & Act
        var request = new ChatRequest(Message: "Test message");

        // Assert
        request.Message.Should().Be("Test message");
        request.History.Should().BeNull();
        request.TopK.Should().BeNull();
        request.ProviderType.Should().BeNull();
        request.ProviderName.Should().BeNull();
        request.StreamSteps.Should().BeFalse();
        request.SearchDepth.Should().BeNull();
    }

    [Fact]
    public void ChatRequest_WithAllParameters_SetsCorrectly()
    {
        // Arrange
        var history = new List<ChatMessage>
        {
            new ChatMessage("user", "Previous question")
        };

        // Act
        var request = new ChatRequest(
            Message: "Follow-up question",
            History: history,
            TopK: 10,
            ProviderType: "s3",
            ProviderName: "docs",
            StreamSteps: true,
            SearchDepth: 3
        );

        // Assert
        request.Message.Should().Be("Follow-up question");
        request.History.Should().HaveCount(1);
        request.TopK.Should().Be(10);
        request.ProviderType.Should().Be("s3");
        request.ProviderName.Should().Be("docs");
        request.StreamSteps.Should().BeTrue();
        request.SearchDepth.Should().Be(3);
    }
}

[Trait("Category", "Unit")]
public class ChatStreamUpdateTests
{
    [Fact]
    public void ChatStreamUpdate_StepType_ConstructsCorrectly()
    {
        // Arrange & Act
        var update = new ChatStreamUpdate(
            Type: "step",
            Message: "Processing query",
            Files: null,
            Final: null);

        // Assert
        update.Type.Should().Be("step");
        update.Message.Should().Be("Processing query");
        update.Files.Should().BeNull();
        update.Final.Should().BeNull();
    }

    [Fact]
    public void ChatStreamUpdate_FinalType_IncludesResponse()
    {
        // Arrange
        var finalResponse = new ChatResponse(
            "Final answer",
            new List<string>(),
            new List<DocumentResult>(),
            new List<Source>(),
            100,
            new List<ChatMessage>());

        // Act
        var update = new ChatStreamUpdate(
            Type: "final",
            Message: null,
            Files: new List<DocumentResult>(),
            Final: finalResponse);

        // Assert
        update.Type.Should().Be("final");
        update.Final.Should().NotBeNull();
        update.Final!.Answer.Should().Be("Final answer");
    }

    [Fact]
    public void ChatStreamUpdate_ErrorType_IncludesMessage()
    {
        // Arrange & Act
        var update = new ChatStreamUpdate(
            Type: "error",
            Message: "An error occurred",
            Files: null,
            Final: null);

        // Assert
        update.Type.Should().Be("error");
        update.Message.Should().Be("An error occurred");
    }
}

[Trait("Category", "Unit")]
public class ProviderInfoTests
{
    [Fact]
    public void ProviderInfo_WithAllParameters_ConstructsCorrectly()
    {
        // Arrange
        var registeredAt = DateTimeOffset.UtcNow.AddDays(-10);
        var lastSyncAt = DateTimeOffset.UtcNow.AddHours(-2);
        var metadata = new Dictionary<string, string>
        {
            ["region"] = "us-east-1",
            ["bucket"] = "my-docs"
        };

        // Act
        var info = new ProviderInfo(
            ProviderType: "s3",
            ProviderName: "production",
            IsEnabled: true,
            RegisteredAt: registeredAt,
            LastSyncAt: lastSyncAt,
            Metadata: metadata);

        // Assert
        info.ProviderType.Should().Be("s3");
        info.ProviderName.Should().Be("production");
        info.IsEnabled.Should().BeTrue();
        info.RegisteredAt.Should().Be(registeredAt);
        info.LastSyncAt.Should().Be(lastSyncAt);
        info.Metadata.Should().ContainKey("region");
        info.Metadata!["region"].Should().Be("us-east-1");
    }

    [Fact]
    public void ProviderInfo_WithNullMetadata_HandlesCorrectly()
    {
        // Arrange & Act
        var info = new ProviderInfo(
            "local",
            "docs",
            true,
            DateTimeOffset.UtcNow,
            null,
            null);

        // Assert
        info.Metadata.Should().BeNull();
        info.LastSyncAt.Should().BeNull();
    }
}
