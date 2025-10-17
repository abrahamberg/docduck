using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocDuck.Providers.Configuration;

internal static class ConfigurationJson
{
    public static JsonSerializerOptions Default { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}
