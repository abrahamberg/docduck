using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Runtime;
using Indexer.Options;
using Microsoft.Extensions.Logging;

namespace Indexer.Providers;

/// <summary>
/// Document provider for AWS S3 buckets.
/// </summary>
public class S3Provider : IDocumentProvider, IDisposable
{
    private readonly IAmazonS3 _s3Client;
    private readonly S3ProviderConfig _config;
    private readonly ILogger<S3Provider> _logger;

    public string ProviderType => "s3";
    public string ProviderName => _config.Name;
    public bool IsEnabled => _config.Enabled;

    public S3Provider(S3ProviderConfig config, ILogger<S3Provider> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        _config = config;
        _logger = logger;

        var s3Config = new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(_config.Region)
        };

        if (_config.UseInstanceProfile)
        {
            _logger.LogInformation("Using instance profile / IAM role for S3 authentication");
            _s3Client = new AmazonS3Client(s3Config);
        }
        else
        {
            if (string.IsNullOrEmpty(_config.AccessKeyId) || string.IsNullOrEmpty(_config.SecretAccessKey))
            {
                throw new InvalidOperationException(
                    $"AccessKeyId and SecretAccessKey are required for S3 provider '{_config.Name}' when not using instance profile");
            }

            _logger.LogInformation("Using access key for S3 authentication");
            
            AWSCredentials credentials = string.IsNullOrEmpty(_config.SessionToken)
                ? new BasicAWSCredentials(_config.AccessKeyId, _config.SecretAccessKey)
                : new SessionAWSCredentials(_config.AccessKeyId, _config.SecretAccessKey, _config.SessionToken);

            _s3Client = new AmazonS3Client(credentials, s3Config);
        }

        _logger.LogInformation("S3 provider '{Name}' initialized for bucket: {Bucket}, prefix: {Prefix}",
            _config.Name, _config.BucketName, _config.Prefix ?? "(root)");
    }

    public async Task<IReadOnlyList<ProviderDocument>> ListDocumentsAsync(CancellationToken ct = default)
    {
        var documents = new List<ProviderDocument>();

        try
        {
            var request = new ListObjectsV2Request
            {
                BucketName = _config.BucketName,
                Prefix = _config.Prefix
            };

            ListObjectsV2Response? response;
            
            do
            {
                response = await _s3Client.ListObjectsV2Async(request, ct);

                foreach (var s3Object in response.S3Objects)
                {
                    ct.ThrowIfCancellationRequested();

                    // Skip folders
                    if (s3Object.Key.EndsWith('/')) continue;

                    var ext = Path.GetExtension(s3Object.Key).ToLowerInvariant();
                    if (!_config.FileExtensions.Contains(ext)) continue;

                    var filename = Path.GetFileName(s3Object.Key);
                    var relativePath = string.IsNullOrEmpty(_config.Prefix)
                        ? s3Object.Key
                        : s3Object.Key.Substring(_config.Prefix.Length).TrimStart('/');

                    documents.Add(new ProviderDocument(
                        DocumentId: s3Object.Key, // Use S3 key as document ID
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

                // Continue pagination if needed
                request.ContinuationToken = response.NextContinuationToken;

            } while (response.IsTruncated);

            _logger.LogInformation("Found {Count} documents in S3 provider '{Name}'",
                documents.Count, _config.Name);

            return documents;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "S3 error listing documents from bucket {Bucket}: {Message}",
                _config.BucketName, ex.Message);
            throw new InvalidOperationException($"S3 error listing documents from bucket {_config.BucketName}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list documents from S3 provider '{Name}'", _config.Name);
            throw new InvalidOperationException($"Failed to list documents from S3 provider '{_config.Name}'", ex);
        }
    }

    public async Task<Stream> DownloadDocumentAsync(string documentId, CancellationToken ct = default)
    {
        try
        {
            // documentId is the S3 key
            var request = new GetObjectRequest
            {
                BucketName = _config.BucketName,
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
            _logger.LogError(ex, "S3 error downloading document {Key} from bucket {Bucket}: {Message}",
                documentId, _config.BucketName, ex.Message);
            throw new InvalidOperationException($"S3 error downloading document {documentId} from bucket {_config.BucketName}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download document {DocumentId} from S3 provider '{Name}'",
                documentId, _config.Name);
            throw new InvalidOperationException($"Failed to download document {documentId} from S3 provider '{_config.Name}'", ex);
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
                ["BucketName"] = _config.BucketName,
                ["Prefix"] = _config.Prefix ?? "(root)",
                ["Region"] = _config.Region,
                ["AuthMode"] = _config.UseInstanceProfile ? "InstanceProfile" : "AccessKey"
            }
        );

        return Task.FromResult(metadata);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _s3Client?.Dispose();
        }
    }
}
