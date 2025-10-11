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
    
    /// <summary>
    /// If true, removes orphaned documents (deleted/moved files) from database.
    /// If false, keeps historical data even if source files are deleted.
    /// Default: true (cleanup enabled)
    /// </summary>
    public bool CleanupOrphanedDocuments { get; set; } = true;
    
    /// <summary>
    /// If true, deletes ALL existing data for each provider before re-indexing.
    /// Forces complete re-index regardless of ETags.
    /// Default: false (incremental indexing)
    /// </summary>
    public bool ForceFullReindex { get; set; } = false;
}

