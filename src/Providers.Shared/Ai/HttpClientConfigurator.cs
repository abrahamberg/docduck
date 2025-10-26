using System.Net.Http.Headers;

namespace DocDuck.Providers.Ai;

/// <summary>
/// Configures HttpClient with headers from AI model configuration.
/// Reduces complexity by centralizing header configuration logic.
/// </summary>
internal static class HttpClientConfigurator
{
    private const string AuthorizationHeader = "Authorization";
    private const string ContentTypeHeader = "Content-Type";

    public static void ConfigureHeaders(HttpClient httpClient, Dictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(headers);

        foreach (var (key, value) in headers)
        {
            if (IsAuthorizationHeader(key))
            {
                SetAuthorizationHeader(httpClient, value);
            }
            else if (!IsContentTypeHeader(key))
            {
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
            }
        }
    }

    private static bool IsAuthorizationHeader(string key)
    {
        return key.Equals(AuthorizationHeader, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsContentTypeHeader(string key)
    {
        return key.Equals(ContentTypeHeader, StringComparison.OrdinalIgnoreCase);
    }

    private static void SetAuthorizationHeader(HttpClient httpClient, string value)
    {
        var parts = value.Split(' ', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(parts[0], parts[1]);
        }
    }
}
