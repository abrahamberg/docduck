namespace DocDuck.Providers.Providers.Settings;

/// <summary>
/// Configuration for AWS S3 provider.
/// </summary>
public sealed class S3ProviderSettings : IProviderSettings
{
    public bool Enabled { get; set; }
    public string Name { get; set; } = "S3";
    public string BucketName { get; set; } = string.Empty;
    public string? Prefix { get; set; }
    public string Region { get; set; } = "us-east-1";
    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }
    public string? SessionToken { get; set; }
    public bool UseInstanceProfile { get; set; }
    public List<string> FileExtensions { get; set; } = new() { ".docx", ".pdf", ".txt" };

    public string ProviderType => "s3";

    public void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(BucketName))
        {
            throw new InvalidOperationException("S3 provider requires a bucket name when enabled.");
        }

        if (!UseInstanceProfile && (string.IsNullOrWhiteSpace(AccessKeyId) || string.IsNullOrWhiteSpace(SecretAccessKey)))
        {
            throw new InvalidOperationException("S3 provider requires AccessKeyId and SecretAccessKey when not using instance profile.");
        }

        if (FileExtensions.Count == 0)
        {
            throw new InvalidOperationException("S3 provider requires at least one file extension filter.");
        }
    }
}
