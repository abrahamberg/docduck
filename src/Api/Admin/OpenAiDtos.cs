using DocDuck.Providers.Ai;

namespace Api.Admin;

public sealed record OpenAiSettingsDto(OpenAiProviderSettings Settings, DateTimeOffset UpdatedAt);

public sealed record OpenAiSettingsRequest(OpenAiProviderSettings Settings);
