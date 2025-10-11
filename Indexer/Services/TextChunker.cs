using Indexer.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Indexer.Services;

/// <summary>
/// Chunks text into overlapping segments by character count.
/// </summary>
public class TextChunker
{
    private readonly ChunkingOptions _options;
    private readonly ILogger<TextChunker> _logger;

    public TextChunker(IOptions<ChunkingOptions> options, ILogger<TextChunker> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Splits text into overlapping chunks with position metadata.
    /// </summary>
    public IEnumerable<Chunk> Chunk(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogDebug("Text is empty or whitespace, no chunks created");
            yield break;
        }

        var chunkSize = _options.ChunkSize;
        var overlap = _options.ChunkOverlap;

        if (overlap >= chunkSize)
        {
            throw new InvalidOperationException($"Overlap ({overlap}) must be less than chunk size ({chunkSize})");
        }

        var chunkNum = 0;
        var position = 0;

        while (position < text.Length)
        {
            var end = Math.Min(position + chunkSize, text.Length);
            var chunkText = text[position..end];

            yield return new Chunk(
                ChunkNum: chunkNum,
                CharStart: position,
                CharEnd: end,
                Text: chunkText
            );

            chunkNum++;
            position += chunkSize - overlap;

            // Stop if we've processed all text
            if (end == text.Length)
            {
                break;
            }
        }

        _logger.LogDebug("Created {Count} chunks from {Length} characters", chunkNum, text.Length);
    }
}
