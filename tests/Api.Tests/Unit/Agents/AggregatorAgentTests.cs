using Api.Models;
using Api.Services.Agents;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Api.Tests.Unit.Agents;

public class AggregatorAgentTests
{
    [Fact]
    public async Task AggregateAsync_EmptySteps_ReturnsEmpty()
    {
        // Arrange
        var logger = Mock.Of<ILogger<AggregatorAgent>>();
        var agent = new AggregatorAgent(logger);

        // Act
        var result = await agent.AggregateAsync(new List<SearchStep>(), CancellationToken.None);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task AggregateAsync_SingleStep_ReturnsFindings()
    {
        // Arrange
        var logger = Mock.Of<ILogger<AggregatorAgent>>();
        var agent = new AggregatorAgent(logger);

        var findings = new List<SearchFinding>
        {
            CreateFinding("doc1", strength: 85),
            CreateFinding("doc2", strength: 90)
        };

        var step = new SearchStep(
            StepName: "initial_search",
            Findings: findings,
            Language: null,
            LookingFor: "test",
            Keywords: new List<string>(),
            Phrase: "test",
            DocType: null,
            StepPrompt: "test"
        );

        // Act
        var result = await agent.AggregateAsync(new List<SearchStep> { step }, CancellationToken.None);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(90, result[0].Strength); // Should be sorted by strength descending
        Assert.Equal(85, result[1].Strength);
    }

    [Fact]
    public async Task AggregateAsync_MergesSameDocument()
    {
        // Arrange
        var logger = Mock.Of<ILogger<AggregatorAgent>>();
        var agent = new AggregatorAgent(logger);

        var step1 = new SearchStep(
            StepName: "step1",
            Findings: new List<SearchFinding>
            {
                new SearchFinding(
                    DocId: "doc1",
                    Filename: "test.md",
                    ProviderType: "fs",
                    ProviderName: "local",
                    Strength: 80,
                    Comment: "First comment",
                    Distance: 0.3,
                    Keywords: new List<string> { "keyword1" },
                    ChunkCount: 1,
                    Chunks: new List<ChunkInfo>
                    {
                        new("doc1_1", 1, 0.3, "chunk 1", null)
                    }
                )
            },
            Language: null,
            LookingFor: "test",
            Keywords: new List<string>(),
            Phrase: "test",
            DocType: null,
            StepPrompt: "test"
        );

        var step2 = new SearchStep(
            StepName: "step2",
            Findings: new List<SearchFinding>
            {
                new SearchFinding(
                    DocId: "doc1", // Same document
                    Filename: "test.md",
                    ProviderType: "fs",
                    ProviderName: "local",
                    Strength: 85, // Higher strength
                    Comment: "Second comment - longer",
                    Distance: 0.2, // Lower distance (better)
                    Keywords: new List<string> { "keyword2" },
                    ChunkCount: 1,
                    Chunks: new List<ChunkInfo>
                    {
                        new("doc1_2", 2, 0.2, "chunk 2", null)
                    }
                )
            },
            Language: null,
            LookingFor: "test",
            Keywords: new List<string>(),
            Phrase: "test",
            DocType: null,
            StepPrompt: "test"
        );

        // Act
        var result = await agent.AggregateAsync(new List<SearchStep> { step1, step2 }, CancellationToken.None);

        // Assert
        Assert.Single(result); // Should merge into one finding
        var merged = result[0];
        Assert.Equal("doc1", merged.DocId);
        Assert.Equal(85, merged.Strength); // Should take highest strength
        Assert.Equal(0.2, merged.Distance); // Should take best (lowest) distance
        Assert.Equal(2, merged.Chunks.Count); // Should have both chunks
        Assert.NotNull(merged.Keywords);
        Assert.Equal(2, merged.Keywords?.Count); // Should merge keywords
        Assert.Contains("keyword1", merged.Keywords!);
        Assert.Contains("keyword2", merged.Keywords!);
    }

    [Fact]
    public async Task AggregateAsync_DeduplicatesChunks()
    {
        // Arrange
        var logger = Mock.Of<ILogger<AggregatorAgent>>();
        var agent = new AggregatorAgent(logger);

        var step1 = new SearchStep(
            StepName: "step1",
            Findings: new List<SearchFinding>
            {
                new SearchFinding(
                    DocId: "doc1",
                    Filename: "test.md",
                    ProviderType: "fs",
                    ProviderName: "local",
                    Strength: 80,
                    Comment: "Test",
                    Distance: 0.3,
                    Keywords: null,
                    ChunkCount: 1,
                    Chunks: new List<ChunkInfo>
                    {
                        new("doc1_1", 1, 0.3, "chunk 1", null) // Chunk 1 with distance 0.3
                    }
                )
            },
            Language: null,
            LookingFor: "test",
            Keywords: new List<string>(),
            Phrase: "test",
            DocType: null,
            StepPrompt: "test"
        );

        var step2 = new SearchStep(
            StepName: "step2",
            Findings: new List<SearchFinding>
            {
                new SearchFinding(
                    DocId: "doc1",
                    Filename: "test.md",
                    ProviderType: "fs",
                    ProviderName: "local",
                    Strength: 85,
                    Comment: "Test",
                    Distance: 0.2,
                    Keywords: null,
                    ChunkCount: 1,
                    Chunks: new List<ChunkInfo>
                    {
                        new("doc1_1", 1, 0.2, "chunk 1 better", null) // Same chunk, better distance
                    }
                )
            },
            Language: null,
            LookingFor: "test",
            Keywords: new List<string>(),
            Phrase: "test",
            DocType: null,
            StepPrompt: "test"
        );

        // Act
        var result = await agent.AggregateAsync(new List<SearchStep> { step1, step2 }, CancellationToken.None);

        // Assert
        Assert.Single(result);
        var merged = result[0];
        Assert.Single(merged.Chunks); // Should deduplicate to one chunk
        Assert.Equal(0.2, merged.Chunks[0].Distance); // Should keep better score
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
