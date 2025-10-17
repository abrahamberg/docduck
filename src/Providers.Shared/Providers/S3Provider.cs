using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DocDuck.Providers.Providers.Settings;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace DocDuck.Providers.Providers;

/// <summary>
/// Document provider for AWS S3 buckets.
/// </summary>
public sealed class S3Provider : IDocumentProvider, IDisposable
{
    private readonly IAmazonS3 _s3Client;
    private readonly S3ProviderSettings _settings;
    private readonly ILogger<S3Provider> _logger;

    public string ProviderType => "s3";
    public string ProviderName => _settings.Name;
    public bool IsEnabled => _settings.Enabled;

    public S3Provider(S3ProviderSettings settings, ILogger<S3Provider> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _logger = logger;

        var s3Config = new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(_settings.Region)
        };

        if (_settings.UseInstanceProfile)
        {
            _logger.LogInformation("Using instance profile / IAM role for S3 authentication");
            _s3Client = new AmazonS3Client(s3Config);
        }
        else
        {
            if (string.IsNullOrEmpty(_settings.AccessKeyId) || string.IsNullOrEmpty(_settings.SecretAccessKey))
            {
                throw new InvalidOperationException(
                    $"AccessKeyId and SecretAccessKey are required for S3 provider '{_settings.Name}' when not using instance profile");
            }

            _logger.LogInformation("Using access key for S3 authentication");

            AWSCredentials credentials = string.IsNullOrEmpty(_settings.SessionToken)
                ? new BasicAWSCredentials(_settings.AccessKeyId, _settings.SecretAccessKey)
                : new SessionAWSCredentials(_settings.AccessKeyId, _settings.SecretAccessKey, _settings.SessionToken);

            _s3Client = new AmazonS3Client(credentials, s3Config);
        }

        _logger.LogInformation("S3 provider '{Name}' initialized for bucket: {Bucket}, prefix: {Prefix}",
            _settings.Name, _settings.BucketName, _settings.Prefix ?? "(root)");
    }

    public async Task<IReadOnlyList<ProviderDocument>> ListDocumentsAsync(CancellationToken ct = default)
    {
        var documents = new List<ProviderDocument>();

        try
        {
            var request = new ListObjectsV2Request
            {
                BucketName = _settings.BucketName,
                Prefix = _settings.Prefix
            };

            ListObjectsV2Response? response;

            do
            {
                response = await _s3Client.ListObjectsV2Async(request, ct);

                foreach (var s3Object in response.S3Objects)
                {
                    ct.ThrowIfCancellationRequested();

                    if (s3Object.Key.EndsWith('/'))
                    {
                        continue;
                    }

                    var ext = Path.GetExtension(s3Object.Key).ToLowerInvariant();
                    if (!_settings.FileExtensions.Contains(ext))
                    {
                        continue;
                    }

                    var filename = Path.GetFileName(s3Object.Key);
                    var relativePath = string.IsNullOrEmpty(_settings.Prefix)
                        ? s3Object.Key
                        : s3Object.Key.Substring(_settings.Prefix.Length).TrimStart('/');

                    documents.Add(new ProviderDocument(
                        DocumentId: s3Object.Key,
                        Filename: filename,
                        ProviderType: ProviderType,
                        ProviderName: ProviderName,
                        ETag: s3Object.ETag?.Trim('"'),
                        LastModified: s3Object.LastModified,
                        SizeBytes: s3Object.Size,
                        MimeType: MimeTypeHelper.GetMimeType(ext),
                        RelativePath: relativePath
                    ));
                }

                request.ContinuationToken = response.NextContinuationToken;

            } while (response.IsTruncated);

            _logger.LogInformation("Found {Count} documents in S3 provider '{Name}'", documents.Count, _settings.Name);
            return documents;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "S3 error listing documents from bucket {Bucket}: {Message}", _settings.BucketName, ex.Message);
            throw new InvalidOperationException($"S3 error listing documents from bucket {_settings.BucketName}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list documents from S3 provider '{Name}'", _settings.Name);
            throw new InvalidOperationException($"Failed to list documents from S3 provider '{_settings.Name}'", ex);
        }
    }

    public async Task<Stream> DownloadDocumentAsync(string documentId, CancellationToken ct = default)
    {
        try
        {
            var request = new GetObjectRequest
            {
                BucketName = _settings.BucketName,
                Key = documentId
            };

            var response = await _s3Client.GetObjectAsync(request, ct);
            _logger.LogDebug("Downloaded document from S3: {Key}", documentId);
            return response.ResponseStream;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "S3 error downloading document {Key} from bucket {Bucket}: {Message}", documentId, _settings.BucketName, ex.Message);
            throw new InvalidOperationException($"S3 error downloading document {documentId} from bucket {_settings.BucketName}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download document {DocumentId} from S3 provider '{Name}'", documentId, _settings.Name);
            throw new InvalidOperationException($"Failed to download document {documentId} from S3 provider '{_settings.Name}'", ex);
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
                ["BucketName"] = _settings.BucketName,
                ["Prefix"] = _settings.Prefix ?? "(root)",
                ["Region"] = _settings.Region,
                ["AuthMode"] = _settings.UseInstanceProfile ? "InstanceProfile" : "AccessKey"
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
                return ProviderProbeResult.SuccessResult("No matching files were found, but the bucket is reachable.", Array.Empty<ProviderProbeDocument>());
            }

            var probeDocs = new List<ProviderProbeDocument>();
            foreach (var doc in documents.Take(request.MaxDocuments))
            {
                await using var stream = await DownloadDocumentAsync(doc.DocumentId, ct);
                var buffer = new byte[Math.Min(request.MaxPreviewBytes, 4096)];
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                probeDocs.Add(new ProviderProbeDocument(doc.DocumentId, doc.Filename, doc.SizeBytes, doc.MimeType, bytesRead));
            }

            return ProviderProbeResult.SuccessResult("S3 provider responded successfully.", probeDocs);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Probe failed for S3 provider '{Name}'", _settings.Name);
            return ProviderProbeResult.Failure(ex.Message);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            _s3Client?.Dispose();
        }
    }
}
