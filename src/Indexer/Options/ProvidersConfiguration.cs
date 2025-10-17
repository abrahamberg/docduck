namespace Indexer.Options;

/// <summary>
/// Top-level configuration for all document providers.
/// Loaded from YAML or environment variables.
/// </summary>
public class ProvidersConfiguration
{
    public const string SectionName = "Providers";

    public OneDriveProviderConfig? OneDrive { get; set; }
    public LocalProviderConfig? Local { get; set; }
    public S3ProviderConfig? S3 { get; set; }
    // Future: Dropbox, RSS, etc.
}

/// <summary>
/// Configuration for OneDrive provider.
/// Supports both personal and business OneDrive accounts using App registration (ClientSecret auth).
/// </summary>
public class OneDriveProviderConfig
{
    public bool Enabled { get; set; } = false;
    public string Name { get; set; } = "OneDrive";
    public string AccountType { get; set; } = "business"; // "personal" or "business"
    
    // App registration credentials (required)
    public string? TenantId { get; set; } // Use "consumers" for personal accounts
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    
    // OneDrive location
    // For personal: only DriveId is needed (or omit to use /me/drive)
    // For business: provide DriveId OR SiteId (if SiteId, the default drive is used)
    public string? SiteId { get; set; }
    public string? DriveId { get; set; }
    public string FolderPath { get; set; } = "/Shared Documents/Docs";
    
    // File filters
    public List<string> FileExtensions { get; set; } = new() { ".docx" };
}

/// <summary>
/// Configuration for local filesystem provider.
/// </summary>
public class LocalProviderConfig
{
    public bool Enabled { get; set; } = false;
    public string Name { get; set; } = "LocalFiles";
    public string RootPath { get; set; } = "/data/documents";
    public List<string> FileExtensions { get; set; } = new() { ".docx", ".pdf", ".txt" };
    public bool Recursive { get; set; } = true;
    public List<string> ExcludePatterns { get; set; } = new();
}

/// <summary>
/// Configuration for AWS S3 provider.
/// </summary>
public class S3ProviderConfig
{
    public bool Enabled { get; set; } = false;
    public string Name { get; set; } = "S3";
    public string BucketName { get; set; } = string.Empty;
    public string? Prefix { get; set; }
    public string Region { get; set; } = "us-east-1";
    
    // Auth options
    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }
    public string? SessionToken { get; set; }
    public bool UseInstanceProfile { get; set; } = false;
    
    // File filters
    public List<string> FileExtensions { get; set; } = new() { ".docx", ".pdf", ".txt" };
}
