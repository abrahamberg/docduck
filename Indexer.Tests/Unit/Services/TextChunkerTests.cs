using FluentAssertions;
using Indexer.Options;
using Indexer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Indexer.Tests.Unit.Services;

[Trait("Category", "Unit")]
public class TextChunkerTests
{
    [Fact]
    public void Chunk_TextSmallerThanChunkSize_ReturnsSingleChunk()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new ChunkingOptions { ChunkSize = 100, ChunkOverlap = 20 });
        var chunker = new TextChunker(options, NullLogger<TextChunker>.Instance);
        var text = "This is a short text.";

        // Act
        var chunks = chunker.Chunk(text).ToList();

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].Text.Should().Be(text);
        chunks[0].ChunkNum.Should().Be(0);
        chunks[0].CharStart.Should().Be(0);
        chunks[0].CharEnd.Should().Be(text.Length);
    }

    [Fact]
    public void Chunk_TextExactlyChunkSize_ReturnsSingleChunk()
    {
        // Arrange
        var chunkSize = 50;
        var options = Microsoft.Extensions.Options.Options.Create(new ChunkingOptions { ChunkSize = chunkSize, ChunkOverlap = 10 });
        var chunker = new TextChunker(options, NullLogger<TextChunker>.Instance);
        var text = new string('a', chunkSize);

        // Act
        var chunks = chunker.Chunk(text).ToList();

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].Text.Length.Should().Be(chunkSize);
    }

    [Fact]
    public void Chunk_TextLargerThanChunkSize_ReturnsMultipleChunks()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new ChunkingOptions { ChunkSize = 10, ChunkOverlap = 2 });
        var chunker = new TextChunker(options, NullLogger<TextChunker>.Instance);
        var text = "This is a longer text that needs chunking";

        // Act
        var chunks = chunker.Chunk(text).ToList();

        // Assert
        chunks.Should().HaveCountGreaterThan(1);
        chunks.Select(c => c.ChunkNum).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Chunk_WithOverlap_ChunksOverlapCorrectly()
    {
        // Arrange
        var chunkSize = 10;
        var overlap = 3;
        var options = Microsoft.Extensions.Options.Options.Create(new ChunkingOptions { ChunkSize = chunkSize, ChunkOverlap = overlap });
        var chunker = new TextChunker(options, NullLogger<TextChunker>.Instance);
        var text = "0123456789ABCDEFGHIJ"; // 20 chars

        // Act
        var chunks = chunker.Chunk(text).ToList();

        // Assert
        chunks.Should().HaveCount(3);
        
        // First chunk: positions 0-9 = "0123456789"
        chunks[0].CharStart.Should().Be(0);
        chunks[0].CharEnd.Should().Be(10);
        chunks[0].Text.Should().Be("0123456789");
        
        // Second chunk: positions 7-16 = "789ABCDEFG" (overlaps 3 chars: 789)
        chunks[1].CharStart.Should().Be(7);
        chunks[1].CharEnd.Should().Be(17);
        chunks[1].Text.Should().Be("789ABCDEFG");
        
        // Third chunk: positions 14-19 = "EFGHIJ" (overlaps 3 chars: EFG)
        chunks[2].CharStart.Should().Be(14);
        chunks[2].CharEnd.Should().Be(20);
        chunks[2].Text.Should().Be("EFGHIJ");
    }

    [Fact]
    public void Chunk_EmptyString_ReturnsNoChunks()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new ChunkingOptions { ChunkSize = 100, ChunkOverlap = 20 });
        var chunker = new TextChunker(options, NullLogger<TextChunker>.Instance);
        var text = "";

        // Act
        var chunks = chunker.Chunk(text).ToList();

        // Assert
        chunks.Should().BeEmpty();
    }

    [Fact]
    public void Chunk_WhitespaceOnly_ReturnsNoChunks()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new ChunkingOptions { ChunkSize = 100, ChunkOverlap = 20 });
        var chunker = new TextChunker(options, NullLogger<TextChunker>.Instance);
        var text = "   \n\t  ";

        // Act
        var chunks = chunker.Chunk(text).ToList();

        // Assert
        chunks.Should().BeEmpty();
    }

    [Fact]
    public void Chunk_NullText_ThrowsArgumentNullException()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new ChunkingOptions { ChunkSize = 100, ChunkOverlap = 20 });
        var chunker = new TextChunker(options, NullLogger<TextChunker>.Instance);

        // Act
        Action act = () => chunker.Chunk(null!).ToList();

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Chunk_OverlapEqualToChunkSize_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new ChunkingOptions { ChunkSize = 100, ChunkOverlap = 100 });
        var chunker = new TextChunker(options, NullLogger<TextChunker>.Instance);
        var text = "Some text";

        // Act
        Action act = () => chunker.Chunk(text).ToList();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Overlap*must be less than chunk size*");
    }

    [Fact]
    public void Chunk_OverlapGreaterThanChunkSize_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new ChunkingOptions { ChunkSize = 50, ChunkOverlap = 60 });
        var chunker = new TextChunker(options, NullLogger<TextChunker>.Instance);
        var text = "Some text";

        // Act
        Action act = () => chunker.Chunk(text).ToList();

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Chunk_NoOverlap_ChunksAreAdjacent()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new ChunkingOptions { ChunkSize = 5, ChunkOverlap = 0 });
        var chunker = new TextChunker(options, NullLogger<TextChunker>.Instance);
        var text = "0123456789"; // 10 chars

        // Act
        var chunks = chunker.Chunk(text).ToList();

        // Assert
        chunks.Should().HaveCount(2);
        chunks[0].Text.Should().Be("01234");
        chunks[1].Text.Should().Be("56789");
        chunks[0].CharEnd.Should().Be(chunks[1].CharStart); // Adjacent
    }

    [Fact]
    public void Chunk_ChunkNumbersAreSequential()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new ChunkingOptions { ChunkSize = 10, ChunkOverlap = 2 });
        var chunker = new TextChunker(options, NullLogger<TextChunker>.Instance);
        var text = new string('a', 50);

        // Act
        var chunks = chunker.Chunk(text).ToList();

        // Assert
        for (int i = 0; i < chunks.Count; i++)
        {
            chunks[i].ChunkNum.Should().Be(i);
        }
    }

    [Fact]
    public void Chunk_LastChunkCanBeSmallerThanChunkSize()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new ChunkingOptions { ChunkSize = 10, ChunkOverlap = 0 });
        var chunker = new TextChunker(options, NullLogger<TextChunker>.Instance);
        var text = "0123456789ABC"; // 13 chars

        // Act
        var chunks = chunker.Chunk(text).ToList();

        // Assert
        chunks.Should().HaveCount(2);
        chunks[0].Text.Length.Should().Be(10);
        chunks[1].Text.Length.Should().Be(3);
        chunks[1].Text.Should().Be("ABC");
    }

    [Fact]
    public void Chunk_VeryLongText_HandlesCorrectly()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new ChunkingOptions { ChunkSize = 100, ChunkOverlap = 20 });
        var chunker = new TextChunker(options, NullLogger<TextChunker>.Instance);
        var text = new string('x', 10000);

        // Act
        var chunks = chunker.Chunk(text).ToList();

        // Assert
        chunks.Should().HaveCountGreaterThan(50);
        chunks.All(c => c.Text.Length <= 100).Should().BeTrue();
    }

    [Fact]
    public void Chunk_PreservesOriginalText_WhenConcatenated()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new ChunkingOptions { ChunkSize = 10, ChunkOverlap = 0 });
        var chunker = new TextChunker(options, NullLogger<TextChunker>.Instance);
        var text = "0123456789ABCDEFGHIJ";

        // Act
        var chunks = chunker.Chunk(text).ToList();
        var reconstructed = string.Concat(chunks.Select(c => c.Text));

        // Assert
        reconstructed.Should().Be(text);
    }
}
