using DocDuck.Providers.Providers.Settings;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Linq;

namespace DocDuck.Providers.Providers;

/// <summary>
/// Document provider for local filesystem access.
/// </summary>
public sealed class LocalProvider : IDocumentProvider
{
    private readonly LocalProviderSettings _settings;
    private readonly ILogger<LocalProvider> _logger;

    public string ProviderType => "local";
    public string ProviderName => _settings.Name;
    public bool IsEnabled => _settings.Enabled;

    public LocalProvider(LocalProviderSettings settings, ILogger<LocalProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _logger = logger;

        if (!Directory.Exists(_settings.RootPath))
        {
            _logger.LogWarning("Local provider root path does not exist: {Path}. Creating directory.", _settings.RootPath);
            Directory.CreateDirectory(_settings.RootPath);
        }

        _logger.LogInformation("Local provider '{Name}' initialized with root path: {Path}", _settings.Name, _settings.RootPath);
    }

    public Task<IReadOnlyList<ProviderDocument>> ListDocumentsAsync(CancellationToken ct = default)
    {
        var documents = new List<ProviderDocument>();

        try
        {
            var searchOption = _settings.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            foreach (var ext in _settings.FileExtensions)
            {
                var pattern = ext.StartsWith('*') ? ext : $"*{ext}";
                var files = Directory.GetFiles(_settings.RootPath, pattern, searchOption);

                foreach (var filePath in files)
                {
                    ct.ThrowIfCancellationRequested();

                    if (IsExcluded(filePath))
                    {
                        _logger.LogDebug("Excluding file: {Path}", filePath);
                        continue;
                    }

                    var fileInfo = new FileInfo(filePath);
                    var relativePath = Path.GetRelativePath(_settings.RootPath, filePath);
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

            _logger.LogInformation("Found {Count} documents in local provider '{Name}'", documents.Count, _settings.Name);
            return Task.FromResult<IReadOnlyList<ProviderDocument>>(documents);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list documents from local provider '{Name}'", _settings.Name);
            throw new InvalidOperationException($"Failed to list documents from local provider '{_settings.Name}'", ex);
        }
    }

    public Task<Stream> DownloadDocumentAsync(string documentId, CancellationToken ct = default)
    {
        try
        {
            var files = Directory.GetFiles(_settings.RootPath, "*", SearchOption.AllDirectories);

            foreach (var filePath in files)
            {
                ct.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(_settings.RootPath, filePath);
                if (GenerateDocumentId(relativePath) == documentId)
                {
                    var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    _logger.LogDebug("Opened file stream for: {Path}", relativePath);
                    return Task.FromResult<Stream>(stream);
                }
            }

            throw new FileNotFoundException($"Document with ID {documentId} not found in local provider '{_settings.Name}'");
        }
        catch (FileNotFoundException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download document {DocumentId} from local provider '{Name}'", documentId, _settings.Name);
            throw new InvalidOperationException($"Failed to download document {documentId} from local provider '{_settings.Name}'", ex);
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
                ["RootPath"] = _settings.RootPath,
                ["Recursive"] = _settings.Recursive.ToString(),
                ["Extensions"] = string.Join(", ", _settings.FileExtensions)
            }
        );

        return Task.FromResult(metadata);
    }

    public async Task<ProviderProbeResult> ProbeAsync(ProviderProbeRequest request, CancellationToken ct = default)
    {
        try
        {
            var documents = await ListDocumentsAsync(ct);
            if (documents.Count == 0)
            {
                return ProviderProbeResult.SuccessResult("No matching files were found, but the provider is reachable.", Array.Empty<ProviderProbeDocument>());
            }

            var sampledDocs = documents.Take(request.MaxDocuments).ToList();
            var probeDocs = new List<ProviderProbeDocument>();

            foreach (var doc in sampledDocs)
            {
                await using var stream = await DownloadDocumentAsync(doc.DocumentId, ct);
                var buffer = new byte[Math.Min(request.MaxPreviewBytes, 4096)];
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                probeDocs.Add(new ProviderProbeDocument(doc.DocumentId, doc.Filename, doc.SizeBytes, doc.MimeType, bytesRead));
            }

            return ProviderProbeResult.SuccessResult("Local provider responded successfully.", probeDocs);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Probe failed for local provider '{Name}'", _settings.Name);
            return ProviderProbeResult.Failure(ex.Message);
        }
    }

    private bool IsExcluded(string filePath)
    {
        if (_settings.ExcludePatterns.Count == 0)
        {
            return false;
        }

        var relativePath = Path.GetRelativePath(_settings.RootPath, filePath);
        var fileName = Path.GetFileName(filePath);

        if (!string.IsNullOrEmpty(fileName) && fileName.StartsWith("~$", StringComparison.Ordinal))
        {
            return true;
        }

        return _settings.ExcludePatterns.Any(pattern => relativePath.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static string GenerateDocumentId(string relativePath)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(relativePath));
        return $"local_{Convert.ToHexString(bytes)[..16].ToLowerInvariant()}";
    }

    private static string GenerateETag(FileInfo fileInfo)
    {
        return $"\"{fileInfo.LastWriteTimeUtc.Ticks}-{fileInfo.Length}\"";
    }
}
