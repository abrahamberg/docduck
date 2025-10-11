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

        _logger.LogInformation("OneDrive provider '{Name}' initialized with {AuthMode} auth for {AccountType}",
            _config.Name, _config.AuthMode, _config.AccountType);
    }

    private Azure.Core.TokenCredential CreateCredential()
    {
        if (_config.AuthMode.Equals("UserPassword", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(_config.Username) || string.IsNullOrEmpty(_config.Password))
            {
                throw new InvalidOperationException(
                    $"Username and Password are required for OneDrive provider '{_config.Name}' with UserPassword auth");
            }

            var tenantId = _config.AccountType.Equals("personal", StringComparison.OrdinalIgnoreCase)
                ? "consumers"
                : _config.TenantId;

            _logger.LogWarning("Username/Password auth is deprecated and doesn't support MFA");

#pragma warning disable AZIDENTITY001
            return new UsernamePasswordCredential(
                _config.Username,
                _config.Password,
                tenantId,
                _config.ClientId
            );
#pragma warning restore AZIDENTITY001
        }
        else
        {
            if (string.IsNullOrEmpty(_config.TenantId) ||
                string.IsNullOrEmpty(_config.ClientId) ||
                string.IsNullOrEmpty(_config.ClientSecret))
            {
                throw new InvalidOperationException(
                    $"TenantId, ClientId, and ClientSecret are required for OneDrive provider '{_config.Name}' with ClientSecret auth");
            }

            return new ClientSecretCredential(
                _config.TenantId,
                _config.ClientId,
                _config.ClientSecret
            );
        }
    }

    public async Task<IReadOnlyList<ProviderDocument>> ListDocumentsAsync(CancellationToken ct = default)
    {
        var documents = new List<ProviderDocument>();

        try
        {
            DriveItemCollectionResponse? driveItems;

            if (_config.AccountType.Equals("personal", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Listing from personal OneDrive: {Path}", _config.FolderPath);
                
                var drive = await _client.Me.Drive.GetAsync(cancellationToken: ct);
                driveItems = await _client.Drives[drive?.Id]
                    .Root
                    .ItemWithPath(_config.FolderPath)
                    .Children
                    .GetAsync(cancellationToken: ct);
            }
            else
            {
                _logger.LogInformation("Listing from business OneDrive - Site: {SiteId}, Drive: {DriveId}, Path: {Path}",
                    _config.SiteId, _config.DriveId, _config.FolderPath);

                if (!string.IsNullOrEmpty(_config.DriveId))
                {
                    var root = await _client.Drives[_config.DriveId].Root.GetAsync(cancellationToken: ct);
                    driveItems = await _client.Drives[_config.DriveId]
                        .Items[root?.Id]
                        .ItemWithPath(_config.FolderPath)
                        .Children
                        .GetAsync(cancellationToken: ct);
                }
                else if (!string.IsNullOrEmpty(_config.SiteId))
                {
                    var drive = await _client.Sites[_config.SiteId].Drive.GetAsync(cancellationToken: ct);
                    driveItems = await _client.Drives[drive?.Id]
                        .Root
                        .ItemWithPath(_config.FolderPath)
                        .Children
                        .GetAsync(cancellationToken: ct);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Either SiteId or DriveId must be configured for business OneDrive provider '{_config.Name}'");
                }
            }

            if (driveItems?.Value == null)
            {
                _logger.LogWarning("No items found in OneDrive path: {Path}", _config.FolderPath);
                return documents;
            }

            foreach (var item in driveItems.Value)
            {
                if (item.File == null || item.Name == null) continue;

                var ext = Path.GetExtension(item.Name).ToLowerInvariant();
                if (!_config.FileExtensions.Contains(ext)) continue;

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
            }

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

    public async Task<Stream> DownloadDocumentAsync(string documentId, CancellationToken ct = default)
    {
        try
        {
            Stream? contentStream;

            if (_config.AccountType.Equals("personal", StringComparison.OrdinalIgnoreCase))
            {
                var drive = await _client.Me.Drive.GetAsync(cancellationToken: ct);
                contentStream = await _client.Drives[drive?.Id].Items[documentId].Content
                    .GetAsync(cancellationToken: ct);
            }
            else
            {
                var driveId = _config.DriveId ?? await GetDriveIdFromSiteAsync(ct);
                contentStream = await _client.Drives[driveId]
                    .Items[documentId].Content
                    .GetAsync(cancellationToken: ct);
            }

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

    private async Task<string> GetDriveIdFromSiteAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_config.SiteId))
        {
            throw new InvalidOperationException($"SiteId must be configured for OneDrive provider '{_config.Name}'");
        }

        var drive = await _client.Sites[_config.SiteId].Drive.GetAsync(cancellationToken: ct);
        return drive?.Id ?? throw new InvalidOperationException("Failed to retrieve Drive ID from Site");
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
                ["FolderPath"] = _config.FolderPath,
                ["AuthMode"] = _config.AuthMode
            }
        );

        return Task.FromResult(metadata);
    }
}
