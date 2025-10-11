namespace Indexer.Providers;

/// <summary>
/// Helper for determining MIME types from file extensions.
/// Centralized to avoid duplication across providers.
/// </summary>
public static class MimeTypeHelper
{
    /// <summary>
    /// Gets the MIME type for a given file extension.
    /// </summary>
    /// <param name="extension">File extension (with or without leading dot)</param>
    /// <returns>MIME type string, or "application/octet-stream" if unknown</returns>
    public static string GetMimeType(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        
        var ext = extension.StartsWith('.') 
            ? extension.ToLowerInvariant() 
            : $".{extension.ToLowerInvariant()}";

        return ext switch
        {
            // Office Documents
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc" => "application/msword",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xls" => "application/vnd.ms-excel",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".ppt" => "application/vnd.ms-powerpoint",
            
            // PDF
            ".pdf" => "application/pdf",
            
            // Text & Markup
            ".txt" => "text/plain",
            ".md" => "text/markdown",
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".yaml" or ".yml" => "application/x-yaml",
            ".html" or ".htm" => "text/html",
            
            // Code & Scripts
            ".sql" => "application/sql",
            ".sh" => "application/x-sh",
            ".bat" => "application/x-bat",
            ".ps1" => "application/x-powershell",
            ".js" => "application/javascript",
            ".ts" => "application/typescript",
            ".cs" => "text/x-csharp",
            ".py" => "text/x-python",
            
            // Default
            _ => "application/octet-stream"
        };
    }
}
