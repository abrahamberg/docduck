using Azure.Identity;
using Indexer.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace Indexer.Providers;

/// <summary>
/// Document provider for Microsoft OneDrive (personal and business).
/// </summary>
public class OneDriveProvider : IDocumentProvider
{
    private readonly GraphServiceClient _client;
    private readonly OneDriveProviderConfig _config;
    private readonly ILogger<OneDriveProvider> _logger;

    public string ProviderType => "onedrive";
    public string ProviderName => _config.Name;
    public bool IsEnabled => _config.Enabled;

    public OneDriveProvider(OneDriveProviderConfig config, ILogger<OneDriveProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        _config = config;
        _logger = logger;

        var credential = CreateCredential();
        _client = new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" });

        _logger.LogInformation("OneDrive provider '{Name}' initialized for {AccountType}",
            _config.Name, _config.AccountType);
    }

    private Azure.Core.TokenCredential CreateCredential()
    {
        if (string.IsNullOrEmpty(_config.TenantId) ||
            string.IsNullOrEmpty(_config.ClientId) ||
            string.IsNullOrEmpty(_config.ClientSecret))
        {
            throw new InvalidOperationException(
                $"TenantId, ClientId, and ClientSecret are required for OneDrive provider '{_config.Name}'");
        }

        return new ClientSecretCredential(
            _config.TenantId,
            _config.ClientId,
            _config.ClientSecret
        );
    }

    public async Task<IReadOnlyList<ProviderDocument>> ListDocumentsAsync(CancellationToken ct = default)
    {
        var documents = new List<ProviderDocument>();

        try
        {
            var driveId = await GetDriveIdAsync(ct);
            _logger.LogInformation("Listing from OneDrive - Drive: {DriveId}, Path: {Path}",
                driveId, _config.FolderPath);

            var rootItem = await _client.Drives[driveId].Root.GetAsync(cancellationToken: ct);
            
            var childrenRequest = _client.Drives[driveId]
                .Items[rootItem?.Id]
                .ItemWithPath(_config.FolderPath)
                .Children;

            var driveItems = await childrenRequest.GetAsync(cancellationToken: ct);
            
            if (driveItems == null)
            {
                _logger.LogWarning("No items found in OneDrive path: {Path}", _config.FolderPath);
                return documents;
            }

            // Process all pages using PageIterator
            var pageIterator = Microsoft.Graph.PageIterator<DriveItem, DriveItemCollectionResponse>
                .CreatePageIterator(
                    _client,
                    driveItems,
                    item => ProcessDriveItem(item, documents),
                    request => request
                );

            await pageIterator.IterateAsync(ct);

            _logger.LogInformation("Found {Count} documents in OneDrive provider '{Name}'",
                documents.Count, _config.Name);

            return documents;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list documents from OneDrive provider '{Name}'", _config.Name);
            throw new InvalidOperationException($"Failed to list documents from OneDrive provider '{_config.Name}'", ex);
        }
    }

    private bool ProcessDriveItem(DriveItem item, List<ProviderDocument> documents)
    {
        if (item.File == null || item.Name == null)
            return true; // Continue iteration

        var ext = Path.GetExtension(item.Name).ToLowerInvariant();
        if (!_config.FileExtensions.Contains(ext))
            return true; // Continue iteration

        documents.Add(new ProviderDocument(
            DocumentId: item.Id!,
            Filename: item.Name,
            ProviderType: ProviderType,
            ProviderName: ProviderName,
            ETag: item.ETag,
            LastModified: item.LastModifiedDateTime,
            SizeBytes: item.Size,
            MimeType: item.File.MimeType,
            RelativePath: _config.FolderPath
        ));

        return true; // Continue iteration
    }

    public async Task<Stream> DownloadDocumentAsync(string documentId, CancellationToken ct = default)
    {
        try
        {
            var driveId = await GetDriveIdAsync(ct);
            var contentStream = await _client.Drives[driveId]
                .Items[documentId].Content
                .GetAsync(cancellationToken: ct);

            if (contentStream == null)
            {
                throw new InvalidOperationException($"Failed to download document {documentId} from OneDrive");
            }

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
            _logger.LogError(ex, "Failed to download document {DocumentId} from OneDrive provider '{Name}'",
                documentId, _config.Name);
            throw new InvalidOperationException($"Failed to download document {documentId} from OneDrive provider '{_config.Name}'", ex);
        }
    }

    private async Task<string> GetDriveIdAsync(CancellationToken ct)
    {
        // If DriveId is explicitly configured, use it (works for both personal and business)
        if (!string.IsNullOrEmpty(_config.DriveId))
        {
            return _config.DriveId;
        }

        // For personal accounts, get drive from /me/drive
        if (_config.AccountType.Equals("personal", StringComparison.OrdinalIgnoreCase))
        {
            var drive = await _client.Me.Drive.GetAsync(cancellationToken: ct);
            return drive?.Id ?? throw new InvalidOperationException("Failed to retrieve personal OneDrive ID");
        }

        // For business accounts, require either DriveId or SiteId
        if (!string.IsNullOrEmpty(_config.SiteId))
        {
            var drive = await _client.Sites[_config.SiteId].Drive.GetAsync(cancellationToken: ct);
            return drive?.Id ?? throw new InvalidOperationException($"Failed to retrieve Drive ID from Site '{_config.SiteId}'");
        }

        throw new InvalidOperationException(
            $"For business accounts, either DriveId or SiteId must be configured for OneDrive provider '{_config.Name}'");
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
                ["AccountType"] = _config.AccountType,
                ["FolderPath"] = _config.FolderPath
            }
        );

        return Task.FromResult(metadata);
    }
}
