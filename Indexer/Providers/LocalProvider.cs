using Indexer.Options;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace Indexer.Providers;

/// <summary>
/// Document provider for local filesystem access.
/// </summary>
public class LocalProvider : IDocumentProvider
{
    private readonly LocalProviderConfig _config;
    private readonly ILogger<LocalProvider> _logger;

    public string ProviderType => "local";
    public string ProviderName => _config.Name;
    public bool IsEnabled => _config.Enabled;

    public LocalProvider(LocalProviderConfig config, ILogger<LocalProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        _config = config;
        _logger = logger;

        if (!Directory.Exists(_config.RootPath))
        {
            _logger.LogWarning("Local provider root path does not exist: {Path}. Will create it.", _config.RootPath);
            Directory.CreateDirectory(_config.RootPath);
        }

        _logger.LogInformation("Local provider '{Name}' initialized with root path: {Path}",
            _config.Name, _config.RootPath);
    }

    public Task<IReadOnlyList<ProviderDocument>> ListDocumentsAsync(CancellationToken ct = default)
    {
        var documents = new List<ProviderDocument>();

        try
        {
            var searchOption = _config.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            foreach (var ext in _config.FileExtensions)
            {
                var pattern = $"*{ext}";
                var files = Directory.GetFiles(_config.RootPath, pattern, searchOption);

                foreach (var filePath in files)
                {
                    ct.ThrowIfCancellationRequested();

                    // Check exclusion patterns
                    if (IsExcluded(filePath))
                    {
                        _logger.LogDebug("Excluding file: {Path}", filePath);
                        continue;
                    }

                    var fileInfo = new FileInfo(filePath);
                    var relativePath = Path.GetRelativePath(_config.RootPath, filePath);
                    
                    // Generate a stable document ID based on the relative path
                    var docId = GenerateDocumentId(relativePath);

                    documents.Add(new ProviderDocument(
                        DocumentId: docId,
                        Filename: fileInfo.Name,
                        ProviderType: ProviderType,
                        ProviderName: ProviderName,
                        ETag: GenerateETag(fileInfo),
                        LastModified: fileInfo.LastWriteTimeUtc,
                        SizeBytes: fileInfo.Length,
                        MimeType: MimeTypeHelper.GetMimeType(fileInfo.Extension),
                        RelativePath: relativePath
                    ));
                }
            }

            _logger.LogInformation("Found {Count} documents in local provider '{Name}'",
                documents.Count, _config.Name);

            return Task.FromResult<IReadOnlyList<ProviderDocument>>(documents);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list documents from local provider '{Name}'", _config.Name);
            throw new InvalidOperationException($"Failed to list documents from local provider '{_config.Name}'", ex);
        }
    }

    public Task<Stream> DownloadDocumentAsync(string documentId, CancellationToken ct = default)
    {
        try
        {
            // The documentId is the hash of the relative path - we need to find the file
            // In practice, we should store a mapping or use the relative path directly
            // For simplicity, we'll search for the file with matching ID
            var files = Directory.GetFiles(_config.RootPath, "*.*", SearchOption.AllDirectories);
            
            foreach (var filePath in files)
            {
                var relativePath = Path.GetRelativePath(_config.RootPath, filePath);
                if (GenerateDocumentId(relativePath) == documentId)
                {
                    var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    _logger.LogDebug("Opened file stream for: {Path}", relativePath);
                    return Task.FromResult<Stream>(stream);
                }
            }

            throw new FileNotFoundException($"Document with ID {documentId} not found in local provider '{_config.Name}'");
        }
        catch (FileNotFoundException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download document {DocumentId} from local provider '{Name}'",
                documentId, _config.Name);
            throw new InvalidOperationException($"Failed to download document {documentId} from local provider '{_config.Name}'", ex);
        }
    }

    public Task<ProviderMetadata> GetMetadataAsync(CancellationToken ct = default)
    {
        var metadata = new ProviderMetadata(
            ProviderType: ProviderType,
            ProviderName: ProviderName,
            IsEnabled: IsEnabled,
            RegisteredAt: DateTimeOffset.UtcNow,
            AdditionalInfo: new Dictionary<string, string>
            {
                ["RootPath"] = _config.RootPath,
                ["Recursive"] = _config.Recursive.ToString(),
                ["Extensions"] = string.Join(", ", _config.FileExtensions)
            }
        );

        return Task.FromResult(metadata);
    }

    private bool IsExcluded(string filePath)
    {
        if (_config.ExcludePatterns.Count == 0) return false;

        var relativePath = Path.GetRelativePath(_config.RootPath, filePath);
        
        return _config.ExcludePatterns
            .Any(pattern => relativePath.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static string GenerateDocumentId(string relativePath)
    {
        // Generate a stable hash-based ID from the relative path
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(relativePath));
        return $"local_{Convert.ToHexString(bytes)[..16].ToLowerInvariant()}";
    }

    private static string GenerateETag(FileInfo fileInfo)
    {
        // Use last write time + size as ETag for change detection
        return $"\"{fileInfo.LastWriteTimeUtc.Ticks}-{fileInfo.Length}\"";
    }
}
