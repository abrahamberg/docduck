using Indexer.Options;
using Indexer.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text.Json;

namespace Indexer;

/// <summary>
/// Main orchestration service that coordinates the indexing pipeline.
/// </summary>
public class IndexerService
{
    private readonly GraphClient _graphClient;
    private readonly DocxExtractor _docxExtractor;
    private readonly TextChunker _textChunker;
    private readonly OpenAiEmbeddingsClient _embeddingsClient;
    private readonly VectorRepository _vectorRepository;
    private readonly ChunkingOptions _chunkingOptions;
    private readonly ILogger<IndexerService> _logger;

    public IndexerService(
        GraphClient graphClient,
        DocxExtractor docxExtractor,
        TextChunker textChunker,
        OpenAiEmbeddingsClient embeddingsClient,
        VectorRepository vectorRepository,
        IOptions<ChunkingOptions> chunkingOptions,
        ILogger<IndexerService> logger)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentNullException.ThrowIfNull(docxExtractor);
        ArgumentNullException.ThrowIfNull(textChunker);
        ArgumentNullException.ThrowIfNull(embeddingsClient);
        ArgumentNullException.ThrowIfNull(vectorRepository);
        ArgumentNullException.ThrowIfNull(chunkingOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _graphClient = graphClient;
        _docxExtractor = docxExtractor;
        _textChunker = textChunker;
        _embeddingsClient = embeddingsClient;
        _vectorRepository = vectorRepository;
        _chunkingOptions = chunkingOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Executes the full indexing pipeline: list, download, extract, chunk, embed, and store.
    /// </summary>
    public async Task<int> ExecuteAsync(CancellationToken ct = default)
    {
        var totalStopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Starting indexer pipeline");

        try
        {
            // List all .docx files from OneDrive
            var items = await _graphClient.ListDocxItemsAsync(ct);

            if (items.Count == 0)
            {
                _logger.LogInformation("No .docx files found. Exiting.");
                return 0;
            }

            // Apply MAX_FILES limit if configured
            var itemsToProcess = _chunkingOptions.MaxFiles.HasValue
                ? items.Take(_chunkingOptions.MaxFiles.Value).ToList()
                : items.ToList();

            _logger.LogInformation("Processing {Count} files (total available: {Total})",
                itemsToProcess.Count, items.Count);

            var processedCount = 0;
            var skippedCount = 0;
            var totalChunks = 0;

            foreach (var item in itemsToProcess)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    // Check if document has already been indexed
                    if (!string.IsNullOrEmpty(item.ETag) &&
                        await _vectorRepository.IsDocumentIndexedAsync(item.Id, item.ETag, ct))
                    {
                        _logger.LogInformation("Skipping unchanged file: {Filename} (ETag: {ETag})",
                            item.Name, item.ETag);
                        skippedCount++;
                        continue;
                    }

                    _logger.LogInformation("Processing file: {Filename}", item.Name);
                    var fileStopwatch = Stopwatch.StartNew();

                    // Download and extract text
                    await using var stream = await _graphClient.DownloadStreamAsync(item.Id, ct);
                    var text = await _docxExtractor.ExtractPlainTextAsync(stream, ct);

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        _logger.LogWarning("File {Filename} contains no extractable text. Skipping.", item.Name);
                        skippedCount++;
                        continue;
                    }

                    // Chunk the text
                    var chunks = _textChunker.Chunk(text).ToList();

                    if (chunks.Count == 0)
                    {
                        _logger.LogWarning("File {Filename} produced no chunks. Skipping.", item.Name);
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
                            doc_id = item.Id,
                            filename = item.Name,
                            chunk_num = chunk.ChunkNum,
                            char_start = chunk.CharStart,
                            char_end = chunk.CharEnd,
                            etag = item.ETag,
                            last_modified = item.LastModified
                        }));

                        return new ChunkRecord(
                            DocId: item.Id,
                            Filename: item.Name,
                            Chunk: chunk,
                            Embedding: embeddings[index],
                            Metadata: metadata
                        );
                    }).ToList();

                    // Upsert to database
                    await _vectorRepository.InsertOrUpsertChunksAsync(records, ct);

                    // Update file tracking
                    if (!string.IsNullOrEmpty(item.ETag) && item.LastModified.HasValue)
                    {
                        await _vectorRepository.UpdateFileTrackingAsync(
                            item.Id,
                            item.Name,
                            item.ETag,
                            item.LastModified.Value,
                            ct);
                    }

                    fileStopwatch.Stop();
                    processedCount++;
                    totalChunks += chunks.Count;

                    _logger.LogInformation(
                        "Processed {Filename}: {ChunkCount} chunks in {Elapsed:F2}s",
                        item.Name, chunks.Count, fileStopwatch.Elapsed.TotalSeconds);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process file {Filename}. Continuing with next file.", item.Name);
                    // Continue processing other files
                }
            }

            totalStopwatch.Stop();

            _logger.LogInformation(
                "Indexing complete. Processed: {Processed}, Skipped: {Skipped}, Total chunks: {Chunks}, Elapsed: {Elapsed:F2}s",
                processedCount, skippedCount, totalChunks, totalStopwatch.Elapsed.TotalSeconds);

            return processedCount > 0 ? 0 : 1; // Exit 0 on success, 1 if nothing processed
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Indexing was cancelled");
            return 130; // Standard exit code for SIGINT
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error during indexing");
            return 1;
        }
    }
}
