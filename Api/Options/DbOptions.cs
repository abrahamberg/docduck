namespace Api.Options;

/// <summary>
/// Configuration for PostgreSQL database connection.
/// </summary>
public class DbOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = string.Empty;
}
