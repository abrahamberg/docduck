namespace DocDuck.Providers.Providers.Settings;

/// <summary>
/// Configuration for local filesystem provider.
/// </summary>
public sealed class LocalProviderSettings : IProviderSettings
{
    public bool Enabled { get; set; }
    public string Name { get; set; } = "LocalFiles";
    public string RootPath { get; set; } = "/data/documents";
    public List<string> FileExtensions { get; set; } = new() { ".docx", ".pdf", ".txt" };
    public bool Recursive { get; set; } = true;
    public List<string> ExcludePatterns { get; set; } = new();

    public string ProviderType => "local";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RootPath))
        {
            throw new InvalidOperationException("Local provider requires a non-empty root path.");
        }

        if (FileExtensions.Count == 0)
        {
            throw new InvalidOperationException("Local provider requires at least one file extension filter.");
        }
    }
}
