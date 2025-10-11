namespace Indexer.Options;

/// <summary>
/// Configuration for Microsoft Graph authentication and OneDrive access.
/// </summary>
public class GraphOptions
{
    public const string SectionName = "Graph";

    /// <summary>
    /// Authentication mode: "ClientSecret" (app-only) or "UserPassword" (delegated).
    /// </summary>
    public string AuthMode { get; set; } = "ClientSecret";

    // Client Secret Auth (Business OneDrive - app-only)
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    // Username/Password Auth (Personal OneDrive or delegated access)
    public string? Username { get; set; }
    public string? Password { get; set; }

    // OneDrive location
    public string? SiteId { get; set; }
    public string? DriveId { get; set; }
    public string FolderPath { get; set; } = "/Shared Documents/Docs";

    /// <summary>
    /// Account type: "personal" or "business". 
    /// Used to determine API endpoints and auth tenant.
    /// </summary>
    public string AccountType { get; set; } = "business";
}
