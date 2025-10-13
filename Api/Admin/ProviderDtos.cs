using System.Text.Json;
using DocDuck.Providers.Providers;

namespace Api.Admin;

public sealed record ProviderSettingsDto(
    string ProviderType,
    string ProviderName,
    bool Enabled,
    DateTimeOffset UpdatedAt,
    JsonElement Settings);

public sealed record ProviderSettingsRequest(JsonElement Settings);

public sealed record ProviderProbeRequestDto(
    string ProviderType,
    JsonElement Settings,
    int? MaxDocuments,
    int? MaxPreviewBytes);

public sealed record ProviderProbeResponse(
    bool Success,
    string Message,
    IReadOnlyList<ProviderProbeDocument> Documents);
