using Azure.Identity;
using Indexer.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace Indexer.Services;

/// <summary>
/// Wraps Microsoft Graph API access for OneDrive file enumeration and download.
/// Supports both client secret (app-only) and username/password (delegated) authentication.
/// </summary>
public class GraphClient
{
    private readonly GraphServiceClient _client;
    private readonly GraphOptions _options;
    private readonly ILogger<GraphClient> _logger;

    public GraphClient(IOptions<GraphOptions> options, ILogger<GraphClient> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;

        // Create appropriate credential based on auth mode
        var credential = CreateCredential();
        _client = new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" });

        _logger.LogInformation("Graph client initialized with {AuthMode} auth for {AccountType} account",
            _options.AuthMode, _options.AccountType);
    }

    private Azure.Core.TokenCredential CreateCredential()
    {
        if (_options.AuthMode.Equals("UserPassword", StringComparison.OrdinalIgnoreCase))
        {
            // Username/Password authentication (for personal OneDrive or delegated access)
            if (string.IsNullOrEmpty(_options.Username) || string.IsNullOrEmpty(_options.Password))
            {
                throw new InvalidOperationException(
                    "GRAPH_USERNAME and GRAPH_PASSWORD are required when AuthMode is UserPassword");
            }

            // Use "consumers" tenant for personal accounts, actual tenant ID for business
            var tenantId = _options.AccountType.Equals("personal", StringComparison.OrdinalIgnoreCase)
                ? "consumers"
                : _options.TenantId;

            _logger.LogInformation("Using username/password authentication for tenant: {TenantId}", tenantId);
            _logger.LogWarning("Username/Password credential is deprecated and doesn't support MFA. Consider using device code flow for production.");

#pragma warning disable AZIDENTITY001 // UsernamePasswordCredential is deprecated
            return new UsernamePasswordCredential(
                _options.Username,
                _options.Password,
                tenantId,
                _options.ClientId
            );
#pragma warning restore AZIDENTITY001
        }
        else
        {
            // Client Secret authentication (app-only for business accounts)
            if (string.IsNullOrEmpty(_options.TenantId) ||
                string.IsNullOrEmpty(_options.ClientId) ||
                string.IsNullOrEmpty(_options.ClientSecret))
            {
                throw new InvalidOperationException(
                    "GRAPH_TENANT_ID, GRAPH_CLIENT_ID, and GRAPH_CLIENT_SECRET are required when AuthMode is ClientSecret");
            }

            _logger.LogInformation("Using client secret authentication");

            return new ClientSecretCredential(
                _options.TenantId,
                _options.ClientId,
                _options.ClientSecret
            );
        }
    }

    /// <summary>
    /// Lists all .docx files in the configured OneDrive folder.
    /// </summary>
    public async Task<IReadOnlyList<DocItem>> ListDocxItemsAsync(CancellationToken ct = default)
    {
        var items = new List<DocItem>();

        try
        {
            DriveItemCollectionResponse? driveItems;

            // Handle personal vs. business account endpoints
            if (_options.AccountType.Equals("personal", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Listing items from personal OneDrive, Path: {Path}", _options.FolderPath);

                // Personal accounts use /me/drive/root:/path:/children
                var drive = await _client.Me.Drive.GetAsync(cancellationToken: ct);
                driveItems = await _client.Drives[drive?.Id]
                    .Root
                    .ItemWithPath(_options.FolderPath)
                    .Children
                    .GetAsync(cancellationToken: ct);
            }
            else
            {
                // Business accounts use /drives/{id} or /sites/{id}/drive
                if (!string.IsNullOrEmpty(_options.DriveId))
                {
                    _logger.LogInformation("Listing items from Drive ID: {DriveId}, Path: {Path}",
                        _options.DriveId, _options.FolderPath);

                    var root = await _client.Drives[_options.DriveId].Root.GetAsync(cancellationToken: ct);
                    driveItems = await _client.Drives[_options.DriveId]
                        .Items[root?.Id]
                        .ItemWithPath(_options.FolderPath)
                        .Children
                        .GetAsync(cancellationToken: ct);
                }
                else if (!string.IsNullOrEmpty(_options.SiteId))
                {
                    _logger.LogInformation("Listing items from Site ID: {SiteId}, Path: {Path}",
                        _options.SiteId, _options.FolderPath);

                    var drive = await _client.Sites[_options.SiteId].Drive.GetAsync(cancellationToken: ct);
                    driveItems = await _client.Drives[drive?.Id]
                        .Root
                        .ItemWithPath(_options.FolderPath)
                        .Children
                        .GetAsync(cancellationToken: ct);
                }
                else
                {
                    throw new InvalidOperationException("Either DriveId or SiteId must be configured for business accounts.");
                }
            }

            if (driveItems?.Value == null)
            {
                _logger.LogWarning("No items found in the specified path.");
                return items;
            }

            foreach (var item in driveItems.Value)
            {
                if (item.File != null && item.Name?.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) == true)
                {
                    items.Add(new DocItem(
                        item.Id ?? string.Empty,
                        item.Name,
                        item.ETag,
                        item.LastModifiedDateTime
                    ));
                }
            }

            _logger.LogInformation("Found {Count} .docx files", items.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list OneDrive items");
            throw;
        }

        return items;
    }

    /// <summary>
    /// Downloads a file stream by item ID.
    /// </summary>
    public async Task<Stream> DownloadStreamAsync(string itemId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(itemId);

        try
        {
            Stream? stream;

            if (_options.AccountType.Equals("personal", StringComparison.OrdinalIgnoreCase))
            {
                // Personal account: get drive ID first, then use /drives/{id}/items/{itemId}
                var drive = await _client.Me.Drive.GetAsync(cancellationToken: ct);
                stream = await _client.Drives[drive?.Id].Items[itemId].Content.GetAsync(cancellationToken: ct);
            }
            else
            {
                // Business account: use /drives/{id}
                var driveId = _options.DriveId ?? await GetDriveIdFromSiteAsync(ct);
                stream = await _client.Drives[driveId].Items[itemId].Content.GetAsync(cancellationToken: ct);
            }

            if (stream == null)
            {
                throw new InvalidOperationException($"Failed to download file stream for item {itemId}");
            }

            _logger.LogDebug("Downloaded file stream for item {ItemId}", itemId);
            return stream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download item {ItemId}", itemId);
            throw;
        }
    }

    private async Task<string> GetDriveIdFromSiteAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_options.SiteId))
        {
            throw new InvalidOperationException("SiteId must be configured when DriveId is not provided.");
        }

        var drive = await _client.Sites[_options.SiteId].Drive.GetAsync(cancellationToken: ct);
        return drive?.Id ?? throw new InvalidOperationException("Failed to retrieve Drive ID from Site.");
    }
}
