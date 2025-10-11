namespace Indexer.Options;

/// <summary>
/// Configuration for text chunking behavior.
/// </summary>
public class ChunkingOptions
{
    public const string SectionName = "Chunking";

    public int ChunkSize { get; set; } = 1000;
    public int ChunkOverlap { get; set; } = 200;
    public int? MaxFiles { get; set; }
}
