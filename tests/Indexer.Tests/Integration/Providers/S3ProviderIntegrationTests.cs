using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using DocDuck.Providers.Providers;
using DocDuck.Providers.Providers.Settings;
using Microsoft.Extensions.Logging;

namespace Indexer.Tests.Integration.Providers;

/// <summary>
/// Integration tests for S3Provider.
/// Requires AWS credentials and an S3 bucket to be configured.
/// Set environment variables: AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, AWS_TEST_BUCKET, AWS_REGION
/// </summary>
[Collection("S3Integration")]
public class S3ProviderIntegrationTests : BaseProviderIntegrationTest
{
    private readonly string? _bucketName;
    private readonly string? _accessKeyId;
    private readonly string? _secretAccessKey;
    private readonly string _region;
    private readonly string _testPrefix;
    private readonly S3Provider? _provider;
    private readonly IAmazonS3? _s3Client;

    public S3ProviderIntegrationTests()
    {
        _bucketName = Environment.GetEnvironmentVariable("AWS_TEST_BUCKET");
        _accessKeyId = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
        _secretAccessKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
        _region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1";
        _testPrefix = $"docduck-integration-test/{Guid.NewGuid():N}";

        // Skip tests if credentials not available
        if (string.IsNullOrEmpty(_bucketName) || string.IsNullOrEmpty(_accessKeyId) || string.IsNullOrEmpty(_secretAccessKey))
        {
            return;
        }

    var config = new S3ProviderSettings
        {
            Enabled = true,
            Name = "TestS3",
            BucketName = _bucketName,
            Prefix = _testPrefix,
            Region = _region,
            AccessKeyId = _accessKeyId,
            SecretAccessKey = _secretAccessKey,
            UseInstanceProfile = false,
            FileExtensions = [".txt", ".docx", ".pdf"]
        };

        var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<S3Provider>();
        _provider = new S3Provider(config, logger);

        // Create S3 client for test setup/cleanup
        var s3Config = new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(_region)
        };
        _s3Client = new AmazonS3Client(_accessKeyId, _secretAccessKey, s3Config);
    }

    [SkippableFact]
    public async Task ListDocuments_ShouldReturnAllValidFiles()
    {
        Skip.If(_provider == null, "S3 credentials not configured");
        
        // Arrange - Upload test files
        await UploadTestFiles();

        try
        {
            // Act & Assert
            await TestListDocumentsWithMetadata(_provider!);
            
            // Additional S3-specific assertions
            var documents = await _provider!.ListDocumentsAsync();
            
            // Should find our test files
            documents.Count.Should().BeGreaterOrEqualTo(3);
            
            // Verify S3-specific properties
            documents.Should().AllSatisfy(d => 
            {
                d.DocumentId.Should().StartWith(_testPrefix);
                d.ETag.Should().NotBeNullOrEmpty();
                d.SizeBytes.Should().BeGreaterThan(0);
                d.LastModified.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
            });
        }
        finally
        {
            await CleanupTestFiles();
        }
    }

    [SkippableFact]
    public async Task ListDocuments_WithManyFiles_ShouldHandlePaging()
    {
        Skip.If(_provider == null, "S3 credentials not configured");
        
        // Arrange - Upload many test files to test pagination
        const int fileCount = 15; // More than typical page size
        await UploadManyTestFiles(fileCount);

        try
        {
            // Act
            var documents = await _provider!.ListDocumentsAsync();
            
            // Assert - Should find all files despite paging
            documents.Count.Should().Be(fileCount);
            
            // Verify all files are unique
            var documentIds = documents.Select(d => d.DocumentId);
            documentIds.Should().OnlyHaveUniqueItems();
            
            // Verify all files have the correct prefix
            documents.Should().AllSatisfy(d => d.DocumentId.Should().StartWith(_testPrefix));
        }
        finally
        {
            await CleanupTestFiles();
        }
    }

    [SkippableFact]
    public async Task ListDocuments_WithPrefix_ShouldOnlyReturnPrefixedFiles()
    {
        Skip.If(_provider == null, "S3 credentials not configured");
        
        // Arrange - Upload files with and without our prefix
        await UploadTestFiles();
        
        // Upload a file outside our prefix
        var outsidePrefix = $"other-prefix/{Guid.NewGuid():N}/outside.txt";
        await _s3Client!.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = outsidePrefix,
            ContentBody = "Outside prefix content"
        });

        try
        {
            // Act
            var documents = await _provider!.ListDocumentsAsync();
            
            // Assert - Should only find files with our prefix
            documents.Should().AllSatisfy(d => d.DocumentId.Should().StartWith(_testPrefix));
            documents.Should().NotContain(d => d.DocumentId.Contains("other-prefix"));
        }
        finally
        {
            await CleanupTestFiles();
            
            // Clean up the outside-prefix file
            await _s3Client!.DeleteObjectAsync(_bucketName!, outsidePrefix);
        }
    }

    [SkippableFact]
    public async Task DownloadDocument_WithValidKey_ShouldReturnObjectStream()
    {
        Skip.If(_provider == null, "S3 credentials not configured");
        
        // Arrange
        await UploadTestFiles();

        try
        {
            // Act & Assert
            await TestDownloadDocument(_provider!);
            
            // Additional S3-specific test - verify content matches
            var documents = await _provider!.ListDocumentsAsync();
            var testDoc = documents.First(d => d.Filename == "test1.txt");
            
            using var stream = await _provider!.DownloadDocumentAsync(testDoc.DocumentId);
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();
            
            content.Should().Be("Test document 1 content");
        }
        finally
        {
            await CleanupTestFiles();
        }
    }

    [SkippableFact]
    public async Task DownloadDocument_WithInvalidKey_ShouldThrowException()
    {
        Skip.If(_provider == null, "S3 credentials not configured");
        
        // Act & Assert
        await TestDownloadNonExistentDocument(_provider!);
    }

    [SkippableFact]
    public async Task GetMetadata_ShouldReturnExpectedInformation()
    {
        Skip.If(_provider == null, "S3 credentials not configured");
        
        // Arrange
        var expectedAdditionalInfo = new Dictionary<string, string>
        {
            ["BucketName"] = _bucketName!,
            ["Prefix"] = _testPrefix,
            ["Region"] = _region,
            ["AuthMode"] = "AccessKey"
        };

        // Act & Assert
        await TestGetMetadata(_provider!, expectedAdditionalInfo);
    }

    [SkippableFact]
    public async Task ListDocuments_ShouldRespectFileExtensionFilter()
    {
        Skip.If(_provider == null, "S3 credentials not configured");
        
        // Arrange - Upload files with various extensions
        await UploadTestFiles();
        
        // Upload files with unsupported extensions
        await _s3Client!.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = $"{_testPrefix}/image.jpg",
            ContentBody = "Not a document"
        });

        try
        {
            // Act
            var documents = await _provider!.ListDocumentsAsync();
            
            // Assert - Should only contain supported extensions
            var extensions = documents.Select(d => Path.GetExtension(d.Filename).ToLowerInvariant()).Distinct();
            extensions.Should().BeSubsetOf([".txt", ".docx", ".pdf"]);
            extensions.Should().NotContain(".jpg");
        }
        finally
        {
            await CleanupTestFiles();
        }
    }

    [SkippableFact]
    public async Task Provider_Properties_ShouldMatchConfiguration()
    {
        Skip.If(_provider == null, "S3 credentials not configured");
        
        // Assert
        _provider!.ProviderType.Should().Be("s3");
        _provider.ProviderName.Should().Be("TestS3");
        _provider.IsEnabled.Should().BeTrue();
    }

    private async Task UploadTestFiles()
    {
        var testFiles = new[]
        {
            ("test1.txt", "Test document 1 content"),
            ("test2.docx", "Test docx content"),
            ("test3.pdf", "Test pdf content"),
            ("subdir/nested.txt", "Nested document content")
        };

        foreach (var (key, content) in testFiles)
        {
            await _s3Client!.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = $"{_testPrefix}/{key}",
                ContentBody = content
            });
        }
    }

    private async Task UploadManyTestFiles(int count)
    {
        var tasks = new List<Task>();
        
        for (int i = 0; i < count; i++)
        {
            var task = _s3Client!.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = $"{_testPrefix}/file{i:D3}.txt",
                ContentBody = $"Test file {i} content"
            });
            tasks.Add(task);
        }
        
        await Task.WhenAll(tasks);
    }

    private async Task CleanupTestFiles()
    {
        if (_s3Client == null || string.IsNullOrEmpty(_bucketName))
            return;

        try
        {
            // List all objects with our test prefix
            var listRequest = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = _testPrefix
            };

            ListObjectsV2Response response;
            do
            {
                response = await _s3Client.ListObjectsV2Async(listRequest);
                
                if (response.S3Objects.Count > 0)
                {
                    // Delete objects in batches
                    var deleteRequest = new DeleteObjectsRequest
                    {
                        BucketName = _bucketName,
                        Objects = response.S3Objects.Select(obj => new KeyVersion { Key = obj.Key }).ToList()
                    };
                    
                    await _s3Client.DeleteObjectsAsync(deleteRequest);
                }
                
                listRequest.ContinuationToken = response.NextContinuationToken;
                
            } while (response.IsTruncated);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to cleanup S3 test files: {ex.Message}");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!DisposedValue && disposing)
        {
            _provider?.Dispose();
            _s3Client?.Dispose();
        }
        base.Dispose(disposing);
    }
}