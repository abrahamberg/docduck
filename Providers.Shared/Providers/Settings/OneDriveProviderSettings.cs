namespace DocDuck.Providers.Providers.Settings;

/// <summary>
/// Configuration for OneDrive provider. Supports both personal and business variants.
/// </summary>
public sealed class OneDriveProviderSettings : IProviderSettings
{
    public bool Enabled { get; set; }
    public string Name { get; set; } = "OneDrive";
    public string AccountType { get; set; } = "business"; // "personal" or "business"
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? SiteId { get; set; }
    public string? DriveId { get; set; }
    public string FolderPath { get; set; } = "/Shared Documents/Docs";
    public List<string> FileExtensions { get; set; } = new() { ".docx" };

    public string ProviderType => "onedrive";

    public void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(TenantId) || string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException("OneDrive provider requires TenantId, ClientId, and ClientSecret when enabled.");
        }

        if (AccountType.Equals("business", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(DriveId) && string.IsNullOrWhiteSpace(SiteId))
        {
            throw new InvalidOperationException("Business OneDrive provider requires either DriveId or SiteId.");
        }

        if (FileExtensions.Count == 0)
        {
            throw new InvalidOperationException("OneDrive provider requires at least one file extension filter.");
        }
    }
}
