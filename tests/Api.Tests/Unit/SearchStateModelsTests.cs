using Api.Models;
using Api.Services;
using Api.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Api.Tests.Unit;

public class SearchStateModelsTests
{
    [Fact]
    public void SearchFinding_IsValid_ReturnsTrueForValidData()
    {
        // Arrange
        var finding = new SearchFinding(
            DocId: "doc123",
            Filename: "test.md",
            ProviderType: "filesystem",
            ProviderName: "local",
            Strength: 85,
            Comment: "Good match with keywords",
            Distance: 0.25,
            Keywords: new List<string> { "test", "keyword" },
            ChunkCount: 3,
            Chunks: new List<ChunkInfo>
            {
                new("chunk1", 1, 0.25, "chunk text", null)
            }
        );

        // Act
        var isValid = finding.IsValid();

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void SearchFinding_IsValid_ReturnsFalseForInvalidStrength()
    {
        // Arrange
        var finding = new SearchFinding(
            DocId: "doc123",
            Filename: "test.md",
            ProviderType: "filesystem",
            ProviderName: "local",
            Strength: 150, // Invalid: > 100
            Comment: "Test",
            Distance: 0.25,
            Keywords: null,
            ChunkCount: 1,
            Chunks: new List<ChunkInfo> { new("chunk1", 1, 0.25, "text", null) }
        );

        // Act
        var isValid = finding.IsValid();

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void SearchFinding_IsValid_ReturnsFalseForLongComment()
    {
        // Arrange
        var longComment = new string('x', 350); // > 300 chars
        var finding = new SearchFinding(
            DocId: "doc123",
            Filename: "test.md",
            ProviderType: "filesystem",
            ProviderName: "local",
            Strength: 85,
            Comment: longComment,
            Distance: 0.25,
            Keywords: null,
            ChunkCount: 1,
            Chunks: new List<ChunkInfo> { new("chunk1", 1, 0.25, "text", null) }
        );

        // Act
        var isValid = finding.IsValid();

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void SearchState_AllDocumentIds_ReturnsUniqueDocIds()
    {
        // Arrange
        var step1 = new SearchStep(
            StepName: "step1",
            Findings: new List<SearchFinding>
            {
                CreateFinding("doc1"),
                CreateFinding("doc2")
            },
            Language: null,
            LookingFor: "test",
            Keywords: new List<string>(),
            Phrase: "test phrase",
            DocType: null,
            StepPrompt: "test"
        );

        var step2 = new SearchStep(
            StepName: "step2",
            Findings: new List<SearchFinding>
            {
                CreateFinding("doc2"), // Duplicate
                CreateFinding("doc3")
            },
            Language: null,
            LookingFor: "test",
            Keywords: new List<string>(),
            Phrase: "test phrase",
            DocType: null,
            StepPrompt: "test"
        );

        var state = new SearchState(
            OriginalPrompt: "test query",
            Steps: new List<SearchStep> { step1, step2 },
            CreatedAt: DateTime.UtcNow
        );

        // Act
        var docIds = state.AllDocumentIds;

        // Assert
        Assert.Equal(3, docIds.Count);
        Assert.Contains("doc1", docIds);
        Assert.Contains("doc2", docIds);
        Assert.Contains("doc3", docIds);
    }

    [Fact]
    public void SearchState_TopFinding_ReturnsHighestStrength()
    {
        // Arrange
        var step = new SearchStep(
            StepName: "step1",
            Findings: new List<SearchFinding>
            {
                CreateFinding("doc1", strength: 70),
                CreateFinding("doc2", strength: 95),
                CreateFinding("doc3", strength: 80)
            },
            Language: null,
            LookingFor: "test",
            Keywords: new List<string>(),
            Phrase: "test phrase",
            DocType: null,
            StepPrompt: "test"
        );

        var state = new SearchState(
            OriginalPrompt: "test query",
            Steps: new List<SearchStep> { step },
            CreatedAt: DateTime.UtcNow
        );

        // Act
        var topFinding = state.TopFinding;

        // Assert
        Assert.NotNull(topFinding);
        Assert.Equal(95, topFinding.Strength);
        Assert.Equal("doc2", topFinding.DocId);
    }

    [Fact]
    public void SearchStep_DocumentCount_ReturnsCorrectCount()
    {
        // Arrange
        var step = new SearchStep(
            StepName: "step1",
            Findings: new List<SearchFinding>
            {
                CreateFinding("doc1"),
                CreateFinding("doc1"), // Same doc
                CreateFinding("doc2")
            },
            Language: null,
            LookingFor: "test",
            Keywords: new List<string>(),
            Phrase: "test phrase",
            DocType: null,
            StepPrompt: "test"
        );

        // Act
        var docCount = step.DocumentCount;

        // Assert
        Assert.Equal(2, docCount); // Should count unique docs
    }

    private static SearchFinding CreateFinding(string docId, int strength = 85)
    {
        return new SearchFinding(
            DocId: docId,
            Filename: $"{docId}.md",
            ProviderType: "filesystem",
            ProviderName: "local",
            Strength: strength,
            Comment: "Test finding",
            Distance: 0.25,
            Keywords: null,
            ChunkCount: 1,
            Chunks: new List<ChunkInfo>
            {
                new($"{docId}_chunk1", 1, 0.25, "chunk text", null)
            }
        );
    }
}
