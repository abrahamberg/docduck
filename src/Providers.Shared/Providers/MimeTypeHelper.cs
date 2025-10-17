namespace DocDuck.Providers.Providers;

/// <summary>
/// Lightweight MIME type lookup used by multiple providers.
/// </summary>
public static class MimeTypeHelper
{
    private static readonly Dictionary<string, string> KnownMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".doc"] = "application/msword",
        [".rtf"] = "application/rtf"
    };

    public static string? GetMimeType(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        return KnownMimeTypes.TryGetValue(extension.StartsWith('.') ? extension : $".{extension}", out var mime)
            ? mime
            : null;
    }
}
