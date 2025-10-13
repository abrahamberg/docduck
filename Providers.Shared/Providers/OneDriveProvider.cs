using Azure.Identity;
using DocDuck.Providers.Providers.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System.Linq;

namespace DocDuck.Providers.Providers;

/// <summary>
/// Document provider for Microsoft OneDrive (personal and business).
/// </summary>
public sealed class OneDriveProvider : IDocumentProvider
{
    private readonly GraphServiceClient _client;
    private readonly OneDriveProviderSettings _settings;
    private readonly ILogger<OneDriveProvider> _logger;

    public string ProviderType => "onedrive";
    public string ProviderName => _settings.Name;
    public bool IsEnabled => _settings.Enabled;

    public OneDriveProvider(OneDriveProviderSettings settings, ILogger<OneDriveProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _logger = logger;

        var credential = CreateCredential();
        _client = new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" });

        _logger.LogInformation("OneDrive provider '{Name}' initialized for {AccountType}", _settings.Name, _settings.AccountType);
    }

    public async Task<IReadOnlyList<ProviderDocument>> ListDocumentsAsync(CancellationToken ct = default)
    {
        var documents = new List<ProviderDocument>();

        try
        {
            var driveId = await GetDriveIdAsync(ct);
            _logger.LogInformation("Listing from OneDrive - Drive: {DriveId}, Path: {Path}", driveId, _settings.FolderPath);

            var rootItem = await _client.Drives[driveId].Root.GetAsync(cancellationToken: ct);

            var childrenRequest = _client.Drives[driveId]
                .Items[rootItem?.Id]
                .ItemWithPath(_settings.FolderPath)
                .Children;

            var driveItems = await childrenRequest.GetAsync(cancellationToken: ct);

            if (driveItems == null)
            {
                _logger.LogWarning("No items found in OneDrive path: {Path}", _settings.FolderPath);
                return documents;
            }

            var pageIterator = PageIterator<DriveItem, DriveItemCollectionResponse>
                .CreatePageIterator(
                    _client,
                    driveItems,
                    item => ProcessDriveItem(item, documents),
                    request => request
                );

            await pageIterator.IterateAsync(ct);

            _logger.LogInformation("Found {Count} documents in OneDrive provider '{Name}'", documents.Count, _settings.Name);
            return documents;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list documents from OneDrive provider '{Name}'", _settings.Name);
            throw new InvalidOperationException($"Failed to list documents from OneDrive provider '{_settings.Name}'", ex);
        }
    }

    public async Task<Stream> DownloadDocumentAsync(string documentId, CancellationToken ct = default)
    {
        try
        {
            var driveId = await GetDriveIdAsync(ct);
            var contentStream = await _client.Drives[driveId]
                .Items[documentId].Content
                .GetAsync(cancellationToken: ct) ?? throw new InvalidOperationException($"Failed to download document {documentId} from OneDrive");

            return contentStream;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download document {DocumentId} from OneDrive provider '{Name}'", documentId, _settings.Name);
            throw new InvalidOperationException($"Failed to download document {documentId} from OneDrive provider '{_settings.Name}'", ex);
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
                ["AccountType"] = _settings.AccountType,
                ["FolderPath"] = _settings.FolderPath
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
                return ProviderProbeResult.SuccessResult("No matching files were found, but OneDrive is reachable.", Array.Empty<ProviderProbeDocument>());
            }

            var probeDocs = new List<ProviderProbeDocument>();
            foreach (var doc in documents.Take(request.MaxDocuments))
            {
                await using var stream = await DownloadDocumentAsync(doc.DocumentId, ct);
                var buffer = new byte[Math.Min(request.MaxPreviewBytes, 4096)];
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                probeDocs.Add(new ProviderProbeDocument(doc.DocumentId, doc.Filename, doc.SizeBytes, doc.MimeType, bytesRead));
            }

            return ProviderProbeResult.SuccessResult("OneDrive provider responded successfully.", probeDocs);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Probe failed for OneDrive provider '{Name}'", _settings.Name);
            return ProviderProbeResult.Failure(ex.Message);
        }
    }

    private bool ProcessDriveItem(DriveItem item, List<ProviderDocument> documents)
    {
        if (item.File == null || item.Name == null)
        {
            return true;
        }

        var ext = Path.GetExtension(item.Name).ToLowerInvariant();
        if (!_settings.FileExtensions.Contains(ext))
        {
            return true;
        }

        documents.Add(new ProviderDocument(
            DocumentId: item.Id!,
            Filename: item.Name,
            ProviderType: ProviderType,
            ProviderName: ProviderName,
            ETag: item.ETag,
            LastModified: item.LastModifiedDateTime,
            SizeBytes: item.Size,
            MimeType: item.File.MimeType,
            RelativePath: _settings.FolderPath
        ));

        return true;
    }

    private Azure.Core.TokenCredential CreateCredential()
    {
        if (string.IsNullOrEmpty(_settings.TenantId) || string.IsNullOrEmpty(_settings.ClientId) || string.IsNullOrEmpty(_settings.ClientSecret))
        {
            throw new InvalidOperationException(
                $"TenantId, ClientId, and ClientSecret are required for OneDrive provider '{_settings.Name}'");
        }

        return new ClientSecretCredential(_settings.TenantId, _settings.ClientId, _settings.ClientSecret);
    }

    private async Task<string> GetDriveIdAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_settings.DriveId))
        {
            return _settings.DriveId;
        }

        if (_settings.AccountType.Equals("personal", StringComparison.OrdinalIgnoreCase))
        {
            var drive = await _client.Me.Drive.GetAsync(cancellationToken: ct);
            return drive?.Id ?? throw new InvalidOperationException("Failed to retrieve personal OneDrive ID");
        }

        if (!string.IsNullOrEmpty(_settings.SiteId))
        {
            var drive = await _client.Sites[_settings.SiteId].Drive.GetAsync(cancellationToken: ct);
            return drive?.Id ?? throw new InvalidOperationException($"Failed to retrieve Drive ID from Site '{_settings.SiteId}'");
        }

        throw new InvalidOperationException(
            $"For business accounts, either DriveId or SiteId must be configured for OneDrive provider '{_settings.Name}'");
    }
}
