using Api.Models;
using FluentAssertions;
using Xunit;

namespace Api.Tests.Models;

[Trait("Category", "Unit")]
public class QueryResponseTests
{
    [Fact]
    public void QueryResponse_FromChatResponse_ConvertsCorrectly()
    {
        // Arrange
        var sources = new List<Source>
        {
            new Source("doc1", "readme.md", 0, "DocDuck is a RAG system", 0.123, "[1]", "local", "docs")
        };

        var files = new List<DocumentResult>
        {
            new DocumentResult("doc1", "readme.md", "local/docs:readme.md", "DocDuck is...", 0.123, "local", "docs")
        };

        var steps = new List<string>
        {
            "Rephrased the question for retrieval: \"docduck features\"",
            "Found 3 chunks across 1 document"
        };

        var history = new List<ChatMessage>
        {
            new ChatMessage("user", "What is DocDuck?"),
            new ChatMessage("assistant", "[Found in: readme.md]"),
            new ChatMessage("assistant", "Answer:\nDocDuck is a RAG system.")
        };

        var chatResponse = new ChatResponse(
            Answer: "DocDuck is a RAG system.",
            Steps: steps,
            Files: files,
            Sources: sources,
            TokensUsed: 250,
            History: history
        );

        // Act
        var queryResponse = QueryResponse.FromChatResponse(chatResponse);

        // Assert
        queryResponse.Answer.Should().Be("DocDuck is a RAG system.");
        queryResponse.Sources.Should().HaveCount(1);
        queryResponse.TokensUsed.Should().Be(250);
        queryResponse.Steps.Should().HaveCount(2);
        queryResponse.Files.Should().HaveCount(1);
        queryResponse.History.Should().HaveCount(3);
    }

    [Fact]
    public void QueryResponse_WithDepthOne_DoesNotIncludeStepsOrFiles()
    {
        // Arrange
        var sources = new List<Source>
        {
            new Source("doc1", "guide.md", 0, "Deployment guide", 0.234, "[1]")
        };

        // Act
        var response = new QueryResponse(
            Answer: "Follow the deployment guide",
            Sources: sources,
            TokensUsed: 150
        );

        // Assert
        response.Answer.Should().Be("Follow the deployment guide");
        response.Sources.Should().HaveCount(1);
        response.TokensUsed.Should().Be(150);
        response.Steps.Should().BeNull();
        response.Files.Should().BeNull();
        response.History.Should().BeNull();
    }

    [Fact]
    public void QueryResponse_WithHistory_PreservesSourceFiles()
    {
        // Arrange
        var sources = new List<Source>
        {
            new Source("doc1", "deploy.md", 0, "Deployment text", 0.1, "[1]", "local", "docs"),
            new Source("doc2", "config.md", 0, "Config text", 0.2, "[2]", "local", "docs")
        };

        var files = new List<DocumentResult>
        {
            new DocumentResult("doc1", "deploy.md", "local/docs:deploy.md", "Deployment...", 0.1),
            new DocumentResult("doc2", "config.md", "local/docs:config.md", "Config...", 0.2)
        };

        var history = new List<ChatMessage>
        {
            new ChatMessage("user", "How to deploy?"),
            new ChatMessage("assistant", "[Found in: deploy.md, config.md]"),
            new ChatMessage("assistant", "Answer:\nUse kubectl apply")
        };

        // Act
        var response = new QueryResponse(
            Answer: "Use kubectl apply",
            Sources: sources,
            TokensUsed: 300,
            Files: files,
            History: history
        );

        // Assert
        response.Files.Should().HaveCount(2);
        response.History.Should().HaveCount(3);
        response.History![1].Content.Should().Contain("deploy.md");
        response.History[1].Content.Should().Contain("config.md");
    }
}
