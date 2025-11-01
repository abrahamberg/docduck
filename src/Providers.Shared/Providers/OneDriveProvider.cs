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
    private static readonly string[] DefaultGraphScopes = ["https://graph.microsoft.com/.default"];

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
        _client = new GraphServiceClient(credential, DefaultGraphScopes);

        _logger.LogInformation("OneDrive provider '{Name}' initialized for {AccountType}", _settings.Name, _settings.AccountType);
    }

    public async Task<IReadOnlyList<ProviderDocument>> ListDocumentsAsync(CancellationToken ct = default)
    {
        var documents = new List<ProviderDocument>();

        try
        {
            var driveId = await GetDriveIdAsync(ct);
            var normalizedPath = NormalizeFolderPath(_settings.FolderPath);
            _logger.LogInformation("Listing from OneDrive - Drive: {DriveId}, Path: {Path} (recursive), Allowed extensions: [{Extensions}]",
                driveId, normalizedPath, string.Join(", ", _settings.FileExtensions));

            // Get the starting folder item ID
            string startingFolderId;
            if (string.IsNullOrEmpty(normalizedPath))
            {
                // Start from root
                var rootItem = await _client.Drives[driveId].Root.GetAsync(cancellationToken: ct);
                startingFolderId = rootItem!.Id!;
            }
            else
            {
                // Start from specific folder
                var folderItem = await _client.Drives[driveId]
                    .Root
                    .ItemWithPath(normalizedPath)
                    .GetAsync(cancellationToken: ct);
                startingFolderId = folderItem!.Id!;
            }

            // Recursively scan all folders
            await ScanFolderRecursivelyAsync(driveId, startingFolderId, normalizedPath, documents, ct);

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
            // For probe, do a quick search for just a few files (not full recursive scan)
            var driveId = await GetDriveIdAsync(ct);
            var normalizedPath = NormalizeFolderPath(_settings.FolderPath);
            _logger.LogInformation("Probing OneDrive - Drive: {DriveId}, Path: {Path}, looking for {MaxDocs} sample files",
                driveId, normalizedPath, request.MaxDocuments);

            // Get the starting folder item ID
            string startingFolderId;
            if (string.IsNullOrEmpty(normalizedPath))
            {
                var rootItem = await _client.Drives[driveId].Root.GetAsync(cancellationToken: ct);
                startingFolderId = rootItem!.Id!;
            }
            else
            {
                var folderItem = await _client.Drives[driveId]
                    .Root
                    .ItemWithPath(normalizedPath)
                    .GetAsync(cancellationToken: ct);
                startingFolderId = folderItem!.Id!;
            }

            // Quick search for sample files (stops after finding maxDocuments)
            var sampleDocs = new List<ProviderDocument>();
            await QuickScanForSamplesAsync(driveId, startingFolderId, normalizedPath, sampleDocs, request.MaxDocuments, ct);

            if (sampleDocs.Count == 0)
            {
                return ProviderProbeResult.SuccessResult("No matching files were found, but OneDrive is reachable.", Array.Empty<ProviderProbeDocument>());
            }

            // Download preview bytes from found samples
            var probeDocs = new List<ProviderProbeDocument>();
            foreach (var doc in sampleDocs)
            {
                await using var stream = await DownloadDocumentAsync(doc.DocumentId, ct);
                var buffer = new byte[Math.Min(request.MaxPreviewBytes, 4096)];
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                probeDocs.Add(new ProviderProbeDocument(doc.DocumentId, doc.Filename, doc.SizeBytes, doc.MimeType, bytesRead));
            }

            return ProviderProbeResult.SuccessResult($"Found {probeDocs.Count} sample file(s). OneDrive is accessible.", probeDocs);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Probe failed for OneDrive provider '{Name}'", _settings.Name);
            return ProviderProbeResult.Failure(ex.Message);
        }
    }

    private async Task ScanFolderRecursivelyAsync(
        string driveId,
        string folderId,
        string folderPath,
        List<ProviderDocument> documents,
        CancellationToken ct)
    {
        var displayPath = string.IsNullOrEmpty(folderPath) ? "/" : $"/{folderPath}";
        _logger.LogInformation("🔍 Scanning folder: {FolderPath} (ID: {FolderId})", displayPath, folderId);
        Console.WriteLine($"🔍 Scanning folder: {displayPath} (ID: {folderId})");

        var driveItems = await _client.Drives[driveId]
            .Items[folderId]
            .Children
            .GetAsync(cancellationToken: ct);

        if (driveItems?.Value == null)
        {
            _logger.LogInformation("⚠️ No items found in folder: {FolderPath}", displayPath);
            Console.WriteLine($"⚠️ No items found in folder: {displayPath}");
            return;
        }

        _logger.LogInformation("📂 Found {Count} items in folder: {FolderPath}", driveItems.Value.Count, displayPath);
        Console.WriteLine($"📂 Found {driveItems.Value.Count} items in folder: {displayPath}");

        var foldersToScan = new List<(string Id, string Path)>();
        var (filesProcessed, filesSkipped, foldersFound) = await ProcessDriveItemsAsync(
            driveItems, folderPath, documents, foldersToScan, ct);

        LogScanResults(displayPath, filesProcessed, filesSkipped, foldersFound);

        // Recursively scan all discovered folders
        await ScanSubfoldersAsync(driveId, foldersToScan, documents, displayPath, ct);
    }

    private async Task<(int FilesProcessed, int FilesSkipped, int FoldersFound)> ProcessDriveItemsAsync(
        DriveItemCollectionResponse driveItems,
        string folderPath,
        List<ProviderDocument> documents,
        List<(string Id, string Path)> foldersToScan,
        CancellationToken ct)
    {
        var filesProcessed = 0;
        var filesSkipped = 0;
        var foldersFound = 0;

        var pageIterator = PageIterator<DriveItem, DriveItemCollectionResponse>
            .CreatePageIterator(
                _client,
                driveItems,
                item =>
                {
                    if (item.Folder != null)
                    {
                        ProcessFolder(item, folderPath, foldersToScan, ref foldersFound);
                    }
                    else if (item.File != null && item.Name != null)
                    {
                        ProcessFile(item, folderPath, documents, ref filesProcessed, ref filesSkipped);
                    }
                    return true;
                },
                request => request
            );

        await pageIterator.IterateAsync(ct);
        return (filesProcessed, filesSkipped, foldersFound);
    }

    private void ProcessFolder(
        DriveItem item,
        string folderPath,
        List<(string Id, string Path)> foldersToScan,
        ref int foldersFound)
    {
        var subFolderPath = string.IsNullOrEmpty(folderPath)
            ? item.Name
            : $"{folderPath}/{item.Name}";
        foldersToScan.Add((item.Id!, subFolderPath!));
        foldersFound++;
    }

    private void ProcessFile(
        DriveItem item,
        string folderPath,
        List<ProviderDocument> documents,
        ref int filesProcessed,
        ref int filesSkipped)
    {
        var ext = Path.GetExtension(item.Name!).ToLowerInvariant();
        if (_settings.FileExtensions.Contains(ext))
        {
            var relativePath = string.IsNullOrEmpty(folderPath) ? "/" : $"/{folderPath}";
            documents.Add(new ProviderDocument(
                DocumentId: item.Id!,
                Filename: item.Name!,
                ProviderType: ProviderType,
                ProviderName: ProviderName,
                ETag: item.ETag,
                LastModified: item.LastModifiedDateTime,
                SizeBytes: item.Size,
                MimeType: item.File!.MimeType,
                RelativePath: relativePath
            ));
            filesProcessed++;
            _logger.LogInformation("Added file: {FileName} (ext: {Ext}) from {Path}", item.Name, ext, folderPath);
        }
        else
        {
            filesSkipped++;
            _logger.LogInformation("Skipped file: {FileName} (ext: {Ext}) - not in allowed extensions [{AllowedExts}]",
                item.Name, ext, string.Join(", ", _settings.FileExtensions));
        }
    }

    private void LogScanResults(string displayPath, int filesProcessed, int filesSkipped, int foldersFound)
    {
        if (filesProcessed > 0 || filesSkipped > 0 || foldersFound > 0)
        {
            _logger.LogInformation("✅ Scanned folder '{Path}': {FileCount} files added, {SkippedCount} files skipped, {FolderCount} subfolders",
                displayPath, filesProcessed, filesSkipped, foldersFound);
            Console.WriteLine($"✅ Scanned '{displayPath}': {filesProcessed} files added, {filesSkipped} skipped, {foldersFound} subfolders");
        }
    }

    private async Task ScanSubfoldersAsync(
        string driveId,
        List<(string Id, string Path)> foldersToScan,
        List<ProviderDocument> documents,
        string displayPath,
        CancellationToken ct)
    {
        Console.WriteLine($"🔄 Recursively scanning {foldersToScan.Count} subfolders from {displayPath}");
        foreach (var (subfolderId, subfolderPath) in foldersToScan)
        {
            await ScanFolderRecursivelyAsync(driveId, subfolderId, subfolderPath, documents, ct);
        }
    }

    /// <summary>
    /// Quick scan for probe - stops after finding maxDocuments sample files (breadth-first search)
    /// </summary>
    private async Task QuickScanForSamplesAsync(
        string driveId,
        string folderId,
        string folderPath,
        List<ProviderDocument> samples,
        int maxDocuments,
        CancellationToken ct)
    {
        if (samples.Count >= maxDocuments)
        {
            return; // Found enough samples
        }

        var displayPath = string.IsNullOrEmpty(folderPath) ? "/" : $"/{folderPath}";
        _logger.LogInformation("🔎 Quick scanning: {Path}", displayPath);

        var driveItems = await _client.Drives[driveId]
            .Items[folderId]
            .Children
            .GetAsync(cancellationToken: ct);

        if (driveItems?.Value == null)
        {
            return;
        }

        var foldersToCheck = ProcessItemsForQuickScan(
            driveItems.Value, folderPath, samples, maxDocuments, displayPath);

        // If we still need more samples, check subfolders (breadth-first)
        await ScanSubfoldersForSamplesAsync(
            driveId, foldersToCheck, samples, maxDocuments, ct);
    }

    private List<(string Id, string Path)> ProcessItemsForQuickScan(
        IEnumerable<DriveItem> items,
        string folderPath,
        List<ProviderDocument> samples,
        int maxDocuments,
        string displayPath)
    {
        var foldersToCheck = new List<(string Id, string Path)>();

        foreach (var item in items)
        {
            if (samples.Count >= maxDocuments)
            {
                break;
            }

            if (item.Folder != null)
            {
                var subFolderPath = string.IsNullOrEmpty(folderPath)
                    ? item.Name
                    : $"{folderPath}/{item.Name}";
                foldersToCheck.Add((item.Id!, subFolderPath!));
            }
            else if (item.File != null && item.Name != null)
            {
                AddSampleIfAllowed(item, folderPath, samples, displayPath);
            }
        }

        return foldersToCheck;
    }

    private void AddSampleIfAllowed(
        DriveItem item,
        string folderPath,
        List<ProviderDocument> samples,
        string displayPath)
    {
        var ext = Path.GetExtension(item.Name!).ToLowerInvariant();
        if (_settings.FileExtensions.Contains(ext))
        {
            var relativePath = string.IsNullOrEmpty(folderPath) ? "/" : $"/{folderPath}";
            samples.Add(new ProviderDocument(
                DocumentId: item.Id!,
                Filename: item.Name!,
                ProviderType: ProviderType,
                ProviderName: ProviderName,
                ETag: item.ETag,
                LastModified: item.LastModifiedDateTime,
                SizeBytes: item.Size,
                MimeType: item.File!.MimeType,
                RelativePath: relativePath
            ));
            _logger.LogInformation("✅ Found sample: {FileName} from {Path}", item.Name, displayPath);
        }
    }

    private async Task ScanSubfoldersForSamplesAsync(
        string driveId,
        List<(string Id, string Path)> foldersToCheck,
        List<ProviderDocument> samples,
        int maxDocuments,
        CancellationToken ct)
    {
        foreach (var (subfolderId, subfolderPath) in foldersToCheck)
        {
            if (samples.Count >= maxDocuments)
            {
                break;
            }
            await QuickScanForSamplesAsync(driveId, subfolderId, subfolderPath, samples, maxDocuments, ct);
        }
    }

    private static string NormalizeFolderPath(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || folderPath == "/" || folderPath == "*")
        {
            return string.Empty;
        }

        // Remove leading and trailing slashes
        return folderPath.Trim('/');
    }

    private ClientSecretCredential CreateCredential()
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
