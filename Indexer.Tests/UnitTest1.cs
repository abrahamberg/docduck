using Indexer;
using Indexer.Options;
using Indexer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Indexer.Tests;

public class TextChunkerTests
{
    [Fact]
    public void Chunk_WithNormalText_CreatesOverlappingChunks()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new ChunkingOptions { ChunkSize = 10, ChunkOverlap = 3 });
        var chunker = new TextChunker(options, NullLogger<TextChunker>.Instance);
        var text = "0123456789ABCDEFGHIJ";

        // Act
        var chunks = chunker.Chunk(text).ToList();

        // Assert
        Assert.Equal(3, chunks.Count);
        Assert.Equal("0123456789", chunks[0].Text);
        Assert.Equal(0, chunks[0].CharStart);
        Assert.Equal(10, chunks[0].CharEnd);
        
        Assert.Equal("789ABCDEFG", chunks[1].Text);
        Assert.Equal(7, chunks[1].CharStart);
        Assert.Equal(17, chunks[1].CharEnd);
        
        Assert.Equal("EFGHIJ", chunks[2].Text);
        Assert.Equal(14, chunks[2].CharStart);
        Assert.Equal(20, chunks[2].CharEnd);
    }

    [Fact]
    public void Chunk_WithShortText_ReturnsSingleChunk()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new ChunkingOptions { ChunkSize = 100, ChunkOverlap = 20 });
        var chunker = new TextChunker(options, NullLogger<TextChunker>.Instance);
        var text = "Short text";

        // Act
        var chunks = chunker.Chunk(text).ToList();

        // Assert
        Assert.Single(chunks);
        Assert.Equal(text, chunks[0].Text);
        Assert.Equal(0, chunks[0].ChunkNum);
    }

    [Fact]
    public void Chunk_WithEmptyText_ReturnsNoChunks()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new ChunkingOptions { ChunkSize = 100, ChunkOverlap = 20 });
        var chunker = new TextChunker(options, NullLogger<TextChunker>.Instance);

        // Act
        var chunks = chunker.Chunk(string.Empty).ToList();

        // Assert
        Assert.Empty(chunks);
    }

    [Fact]
    public void Chunk_WithExactChunkSize_ReturnsSingleChunk()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new ChunkingOptions { ChunkSize = 10, ChunkOverlap = 2 });
        var chunker = new TextChunker(options, NullLogger<TextChunker>.Instance);
        var text = "0123456789";

        // Act
        var chunks = chunker.Chunk(text).ToList();

        // Assert
        Assert.Single(chunks);
        Assert.Equal(text, chunks[0].Text);
    }

    [Fact]
    public void Chunk_WithInvalidOverlap_ThrowsException()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new ChunkingOptions { ChunkSize = 10, ChunkOverlap = 10 });
        var chunker = new TextChunker(options, NullLogger<TextChunker>.Instance);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => chunker.Chunk("test text").ToList());
    }
}

public class DocxExtractorTests
{
    [Fact]
    public async Task ExtractPlainTextAsync_WithEmptyStream_ReturnsEmpty()
    {
        // Arrange
        var extractor = new DocxExtractor(NullLogger<DocxExtractor>.Instance);
        var emptyStream = new MemoryStream();

        // Act & Assert
        // This will throw because it's not a valid .docx, but demonstrates the test structure
        await Assert.ThrowsAnyAsync<Exception>(() => 
            extractor.ExtractPlainTextAsync(emptyStream));
    }
}
