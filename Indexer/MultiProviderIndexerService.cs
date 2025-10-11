using Indexer.Options;
using Indexer.Providers;
using Indexer.Services;
using Indexer.Services.TextExtraction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text.Json;

namespace Indexer;

/// <summary>
/// Main orchestration service that coordinates indexing across multiple document providers.
/// Supports modular, plugin-based provider architecture.
/// </summary>
public class MultiProviderIndexerService
{
    private readonly IEnumerable<IDocumentProvider> _providers;
    private readonly TextExtractionService _textExtractor;
    private readonly TextChunker _textChunker;
    private readonly OpenAiEmbeddingsClient _embeddingsClient;
    private readonly VectorRepository _vectorRepository;
    private readonly ChunkingOptions _chunkingOptions;
    private readonly ILogger<MultiProviderIndexerService> _logger;

    public MultiProviderIndexerService(
        IEnumerable<IDocumentProvider> providers,
        TextExtractionService textExtractor,
        TextChunker textChunker,
        OpenAiEmbeddingsClient embeddingsClient,
        VectorRepository vectorRepository,
        IOptions<ChunkingOptions> chunkingOptions,
        ILogger<MultiProviderIndexerService> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(textExtractor);
        ArgumentNullException.ThrowIfNull(textChunker);
        ArgumentNullException.ThrowIfNull(embeddingsClient);
        ArgumentNullException.ThrowIfNull(vectorRepository);
        ArgumentNullException.ThrowIfNull(chunkingOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _providers = providers;
        _textExtractor = textExtractor;
        _textChunker = textChunker;
        _embeddingsClient = embeddingsClient;
        _vectorRepository = vectorRepository;
        _chunkingOptions = chunkingOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Executes the full indexing pipeline across all enabled providers.
    /// </summary>
    public async Task<int> ExecuteAsync(CancellationToken ct = default)
    {
        var totalStopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Starting multi-provider indexer pipeline");

        try
        {
            var enabledProviders = _providers.Where(p => p.IsEnabled).ToList();

            if (enabledProviders.Count == 0)
            {
                _logger.LogWarning("No enabled providers found. Exiting.");
                return 1;
            }

            _logger.LogInformation("Found {Count} enabled provider(s): {Providers}",
                enabledProviders.Count,
                string.Join(", ", enabledProviders.Select(p => $"{p.ProviderType}/{p.ProviderName}")));

            // Register all enabled providers in database
            foreach (var provider in enabledProviders)
            {
                var metadata = await provider.GetMetadataAsync(ct);
                await _vectorRepository.RegisterProviderAsync(metadata, ct);
            }

            var totalProcessed = 0;
            var totalSkipped = 0;
            var totalChunks = 0;

            // Process each provider
            foreach (var provider in enabledProviders)
            {
                ct.ThrowIfCancellationRequested();

                _logger.LogInformation("Processing provider: {Type}/{Name}",
                    provider.ProviderType, provider.ProviderName);

                var (processed, skipped, chunks) = await ProcessProviderAsync(provider, ct);
                
                totalProcessed += processed;
                totalSkipped += skipped;
                totalChunks += chunks;

                // Update provider sync time
                await _vectorRepository.UpdateProviderSyncTimeAsync(
                    provider.ProviderType, 
                    provider.ProviderName, 
                    ct);
            }

            totalStopwatch.Stop();

            _logger.LogInformation(
                "Indexing complete. Providers: {Providers}, Processed: {Processed}, Skipped: {Skipped}, Total chunks: {Chunks}, Elapsed: {Elapsed:F2}s",
                enabledProviders.Count, totalProcessed, totalSkipped, totalChunks, totalStopwatch.Elapsed.TotalSeconds);

            return totalProcessed > 0 ? 0 : 1;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Indexing operation was cancelled");
            return 130;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error during indexing");
            return 1;
        }
    }

    private async Task<(int processed, int skipped, int chunks)> ProcessProviderAsync(
        IDocumentProvider provider,
        CancellationToken ct)
    {
        try
        {
            // FORCE FULL REINDEX: Delete all existing data for this provider
            if (_chunkingOptions.ForceFullReindex)
            {
                _logger.LogWarning("Force full reindex enabled. Deleting all existing data for {Type}/{Name}",
                    provider.ProviderType, provider.ProviderName);
                
                await _vectorRepository.DeleteAllProviderDocumentsAsync(
                    provider.ProviderType,
                    provider.ProviderName,
                    ct);
            }
            
            // List all documents from this provider
            var documents = await provider.ListDocumentsAsync(ct);

            if (documents.Count == 0)
            {
                _logger.LogInformation("No documents found in provider {Type}/{Name}",
                    provider.ProviderType, provider.ProviderName);
                
                // Cleanup: if provider has no documents, remove all indexed data
                if (_chunkingOptions.CleanupOrphanedDocuments)
                {
                    await _vectorRepository.CleanupOrphanedDocumentsAsync(
                        provider.ProviderType,
                        provider.ProviderName,
                        Array.Empty<string>(),
                        ct);
                }
                
                return (0, 0, 0);
            }

            // Apply MAX_FILES limit if configured
            var docsToProcess = _chunkingOptions.MaxFiles.HasValue
                ? documents.Take(_chunkingOptions.MaxFiles.Value).ToList()
                : documents.ToList();

            _logger.LogInformation("Processing {Count} documents from {Type}/{Name} (total available: {Total})",
                docsToProcess.Count, provider.ProviderType, provider.ProviderName, documents.Count);

            // CLEANUP ORPHANED DOCUMENTS: Remove deleted/moved files from database
            if (_chunkingOptions.CleanupOrphanedDocuments)
            {
                var currentDocIds = documents.Select(d => d.DocumentId).ToList();
                await _vectorRepository.CleanupOrphanedDocumentsAsync(
                    provider.ProviderType,
                    provider.ProviderName,
                    currentDocIds,
                    ct);
            }

            var processedCount = 0;
            var skippedCount = 0;
            var totalChunks = 0;

            foreach (var doc in docsToProcess)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    // Check if document has already been indexed
                    if (!string.IsNullOrEmpty(doc.ETag) &&
                        await _vectorRepository.IsDocumentIndexedAsync(
                            doc.DocumentId, 
                            doc.ETag, 
                            provider.ProviderType, 
                            provider.ProviderName, 
                            ct))
                    {
                        _logger.LogInformation("Skipping unchanged file: {Filename} from {Provider} (ETag: {ETag})",
                            doc.Filename, provider.ProviderName, doc.ETag);
                        skippedCount++;
                        continue;
                    }

                    _logger.LogInformation("Processing file: {Filename} from {Provider}",
                        doc.Filename, provider.ProviderName);
                    
                    var fileStopwatch = Stopwatch.StartNew();

                    // Download and extract text using appropriate extractor
                    await using var stream = await provider.DownloadDocumentAsync(doc.DocumentId, ct);
                    
                    // Check if file type is supported
                    if (!_textExtractor.IsSupported(doc.Filename))
                    {
                        _logger.LogWarning("File type not supported: {Filename} from {Provider}. Skipping.",
                            doc.Filename, provider.ProviderName);
                        skippedCount++;
                        continue;
                    }
                    
                    var text = await _textExtractor.ExtractTextAsync(stream, doc.Filename, ct);

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        _logger.LogWarning("File {Filename} from {Provider} contains no extractable text. Skipping.",
                            doc.Filename, provider.ProviderName);
                        skippedCount++;
                        continue;
                    }

                    // Chunk the text
                    var chunks = _textChunker.Chunk(text).ToList();

                    if (chunks.Count == 0)
                    {
                        _logger.LogWarning("File {Filename} from {Provider} produced no chunks. Skipping.",
                            doc.Filename, provider.ProviderName);
                        skippedCount++;
                        continue;
                    }

                    // Generate embeddings in batches
                    var chunkTexts = chunks.Select(c => c.Text).ToList();
                    var embeddings = await _embeddingsClient.EmbedBatchedAsync(chunkTexts, ct);

                    // Create chunk records with metadata
                    var records = chunks.Select((chunk, index) =>
                    {
                        var metadata = JsonDocument.Parse(JsonSerializer.Serialize(new
                        {
                            doc_id = doc.DocumentId,
                            filename = doc.Filename,
                            provider_type = doc.ProviderType,
                            provider_name = doc.ProviderName,
                            chunk_num = chunk.ChunkNum,
                            char_start = chunk.CharStart,
                            char_end = chunk.CharEnd,
                            etag = doc.ETag,
                            last_modified = doc.LastModified,
                            relative_path = doc.RelativePath
                        }));

                        return new ChunkRecord(
                            DocId: doc.DocumentId,
                            Filename: doc.Filename,
                            Chunk: chunk,
                            Embedding: embeddings[index],
                            Metadata: metadata
                        );
                    }).ToList();

                    // Upsert to database
                    await _vectorRepository.InsertOrUpsertChunksAsync(
                        records, 
                        provider.ProviderType, 
                        provider.ProviderName, 
                        ct);

                    // Update file tracking
                    if (!string.IsNullOrEmpty(doc.ETag) && doc.LastModified.HasValue)
                    {
                        await _vectorRepository.UpdateFileTrackingAsync(
                            doc.DocumentId,
                            doc.Filename,
                            doc.ETag,
                            doc.LastModified.Value,
                            provider.ProviderType,
                            provider.ProviderName,
                            doc.RelativePath,
                            ct);
                    }

                    fileStopwatch.Stop();
                    processedCount++;
                    totalChunks += chunks.Count;

                    _logger.LogInformation(
                        "Processed {Filename} from {Provider}: {ChunkCount} chunks in {Elapsed:F2}s",
                        doc.Filename, provider.ProviderName, chunks.Count, fileStopwatch.Elapsed.TotalSeconds);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process file {Filename} from {Provider}. Continuing with next file.",
                        doc.Filename, provider.ProviderName);
                }
            }

            return (processedCount, skippedCount, totalChunks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process provider {Type}/{Name}",
                provider.ProviderType, provider.ProviderName);
            return (0, 0, 0);
        }
    }
}
