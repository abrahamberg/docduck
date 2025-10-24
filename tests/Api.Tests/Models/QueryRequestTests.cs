using Api.Models;
using FluentAssertions;
using Xunit;

namespace Api.Tests.Models;

[Trait("Category", "Unit")]
public class QueryRequestTests
{
    [Fact]
    public void QueryRequest_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var request = new QueryRequest(Question: "What is deployment?");

        // Assert
        request.Question.Should().Be("What is deployment?");
        request.TopK.Should().BeNull();
        request.ProviderType.Should().BeNull();
        request.ProviderName.Should().BeNull();
        request.SearchDepth.Should().BeNull();
        request.StreamSteps.Should().BeFalse();
        request.History.Should().BeNull();
    }

    [Fact]
    public void QueryRequest_WithAllParameters_SetsCorrectly()
    {
        // Arrange
        var history = new List<ChatMessage>
        {
            new ChatMessage("user", "What is DocDuck?"),
            new ChatMessage("assistant", "DocDuck is a RAG system.")
        };

        // Act
        var request = new QueryRequest(
            Question: "How do I deploy it?",
            TopK: 10,
            ProviderType: "local",
            ProviderName: "docs",
            SearchDepth: 3,
            StreamSteps: true,
            History: history
        );

        // Assert
        request.Question.Should().Be("How do I deploy it?");
        request.TopK.Should().Be(10);
        request.ProviderType.Should().Be("local");
        request.ProviderName.Should().Be("docs");
        request.SearchDepth.Should().Be(3);
        request.StreamSteps.Should().BeTrue();
        request.History.Should().HaveCount(2);
        request.History![0].Role.Should().Be("user");
        request.History[1].Role.Should().Be("assistant");
    }

    [Fact]
    public void QueryRequest_WithHistory_PreservesConversationContext()
    {
        // Arrange
        var history = new List<ChatMessage>
        {
            new ChatMessage("user", "Tell me about Kubernetes"),
            new ChatMessage("assistant", "[Found in: k8s-guide.md]"),
            new ChatMessage("assistant", "Answer:\nKubernetes is a container orchestration platform.")
        };

        // Act
        var request = new QueryRequest(
            Question: "How does it handle scaling?",
            SearchDepth: 2,
            History: history
        );

        // Assert
        request.History.Should().HaveCount(3);
        request.History![0].Content.Should().Contain("Kubernetes");
        request.History[1].Content.Should().Contain("k8s-guide.md");
        request.History[2].Content.Should().Contain("container orchestration");
    }
}
