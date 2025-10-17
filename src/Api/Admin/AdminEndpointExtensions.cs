using System.Linq;
using System.Text.Json;
using DocDuck.Providers.Ai;
using DocDuck.Providers.Configuration;
using DocDuck.Providers.Providers;
using DocDuck.Providers.Providers.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Npgsql;

namespace Api.Admin;

public static class AdminEndpointExtensions
{
    public static RouteGroupBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").WithTags("Admin");

        var auth = admin.MapGroup("/auth");
        auth.MapPost("/login", async (
            AdminLoginRequest request,
            AdminUserStore userStore,
            AdminAuthService authService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new { error = "Username and password are required." });
            }

            var user = await userStore.ValidateCredentialsAsync(request.Username, request.Password, ct);
            if (user is null || !user.IsAdmin)
            {
                return Results.Unauthorized();
            }

            var token = authService.IssueToken(user.Id);
            return Results.Ok(new AdminLoginResponse(token, ToDto(user)));
        });

        auth.MapGet("/profile", (HttpContext context) => Results.Ok(GetAuthenticatedUser(context)))
            .AddEndpointFilter<AdminAuthFilter>();

        var secure = admin.MapGroup(string.Empty).AddEndpointFilter<AdminAuthFilter>();

        secure.MapGet("/providers", async (ProviderSettingsStore store, CancellationToken ct) =>
        {
            var records = await store.GetAllAsync(ct);
            var list = new List<ProviderSettingsDto>(records.Count);

            foreach (var record in records)
            {
                var payload = record.Payload;
                var clone = payload.RootElement.Clone();
                var enabled = clone.TryGetProperty("enabled", out var enabledProp) && enabledProp.ValueKind == JsonValueKind.True;
                list.Add(new ProviderSettingsDto(record.ProviderType, record.ProviderName, enabled, record.UpdatedAt, clone));
                payload.Dispose();
            }

            return Results.Ok(new { providers = list, count = list.Count });
        });

        secure.MapGet("/providers/{providerType}/{providerName}", async (string providerType, string providerName, ProviderSettingsStore store, CancellationToken ct) =>
        {
            var record = await store.GetAsync(providerType, providerName, ct);
            if (record is null)
            {
                return Results.NotFound();
            }

            var payload = record.Payload;
            var clone = payload.RootElement.Clone();
            var enabled = clone.TryGetProperty("enabled", out var enabledProp) && enabledProp.ValueKind == JsonValueKind.True;
            payload.Dispose();

            return Results.Ok(new ProviderSettingsDto(record.ProviderType, record.ProviderName, enabled, record.UpdatedAt, clone));
        });

        secure.MapPut("/providers/{providerType}/{providerName}", async (
            string providerType,
            string providerName,
            ProviderSettingsRequest request,
            ProviderSettingsStore store,
            ProviderFactory factory,
            ProviderConfigurationService configuration,
            CancellationToken ct) =>
        {
            if (request.Settings.ValueKind == JsonValueKind.Undefined || request.Settings.ValueKind == JsonValueKind.Null)
            {
                return Results.BadRequest(new { error = "Settings payload is required." });
            }

            using var doc = JsonDocument.Parse(request.Settings.GetRawText());
            var record = new ProviderSettingsRecord
            {
                ProviderType = providerType,
                ProviderName = providerName,
                Payload = doc,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            IProviderSettings settings;
            try
            {
                if (!factory.TryCreateSettings(record, out settings))
                {
                    return Results.BadRequest(new { error = $"Unsupported provider type '{providerType}'." });
                }
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            if (!string.Equals(settings.Name, providerName, StringComparison.Ordinal))
            {
                return Results.BadRequest(new { error = "Provider name in settings must match request path." });
            }

            await store.UpsertAsync(settings, ct);
            await configuration.ReloadAsync(ct);

            return Results.Ok(new { providerType, providerName });
        });

        secure.MapDelete("/providers/{providerType}/{providerName}", async (
            string providerType,
            string providerName,
            ProviderSettingsStore store,
            ProviderConfigurationService configuration,
            CancellationToken ct) =>
        {
            await store.DeleteAsync(providerType, providerName, ct);
            await configuration.ReloadAsync(ct);
            return Results.NoContent();
        });

        secure.MapPost("/providers/probe", async (
            ProviderProbeRequestDto request,
            ProviderFactory factory,
            CancellationToken ct) =>
        {
            if (request.Settings.ValueKind == JsonValueKind.Undefined || request.Settings.ValueKind == JsonValueKind.Null)
            {
                return Results.BadRequest(new { error = "Settings payload is required." });
            }

            using var doc = JsonDocument.Parse(request.Settings.GetRawText());
            var record = new ProviderSettingsRecord
            {
                ProviderType = request.ProviderType,
                ProviderName = doc.RootElement.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                    ? nameProp.GetString() ?? string.Empty
                    : string.Empty,
                Payload = doc,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            IProviderSettings settings;
            try
            {
                if (!factory.TryCreateSettings(record, out settings))
                {
                    return Results.BadRequest(new { error = $"Unsupported provider type '{request.ProviderType}'." });
                }
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var provider = factory.CreateProvider(settings);
            var probeRequest = new ProviderProbeRequest(
                request.MaxDocuments ?? ProviderProbeRequest.Default.MaxDocuments,
                request.MaxPreviewBytes ?? ProviderProbeRequest.Default.MaxPreviewBytes);

            try
            {
                var result = await provider.ProbeAsync(probeRequest, ct);
                return Results.Ok(new ProviderProbeResponse(result.Success, result.Message, result.Documents));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Ok(new ProviderProbeResponse(false, ex.Message, Array.Empty<ProviderProbeDocument>()));
            }
            finally
            {
                switch (provider)
                {
                    case IAsyncDisposable asyncDisposable:
                        await asyncDisposable.DisposeAsync();
                        break;
                    case IDisposable disposable:
                        disposable.Dispose();
                        break;
                }
            }
        });

        secure.MapGet("/ai/openai", async (AiProviderSettingsStore store, AiConfigurationService configuration, CancellationToken ct) =>
        {
            var settings = await store.GetOpenAiAsync(ct);
            if (settings is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new OpenAiSettingsDto(settings, configuration.LoadedAt));
        });

        secure.MapPut("/ai/openai", async (
            OpenAiSettingsRequest request,
            AiProviderSettingsStore store,
            AiConfigurationService configuration,
            CancellationToken ct) =>
        {
            if (request.Settings is null)
            {
                return Results.BadRequest(new { error = "Settings payload is required." });
            }

            request.Settings.Validate();
            await store.UpsertAsync(request.Settings, ct);
            await configuration.ReloadAsync(ct);

            return Results.Ok(new OpenAiSettingsDto(request.Settings, configuration.LoadedAt));
        });

        secure.MapPost("/ai/openai/probe", async (
            AiProviderSettingsStore store,
            AiConfigurationService configuration,
            CancellationToken ct) =>
        {
            var settings = await configuration.GetOpenAiAsync(ct) ?? await store.GetOpenAiAsync(ct);
            if (settings is null)
            {
                return Results.BadRequest(new { error = "OpenAI provider is not configured." });
            }

            settings.Validate();

            try
            {
                using var http = new HttpClient { BaseAddress = new Uri(settings.BaseUrl, UriKind.Absolute) };
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiKey);
                using var response = await http.GetAsync("models", ct);
                if (!response.IsSuccessStatusCode)
                {
                    return Results.Ok(new { success = false, message = $"OpenAI probe failed with status {(int)response.StatusCode}" });
                }

                return Results.Ok(new { success = true, message = "OpenAI connectivity check succeeded." });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Ok(new { success = false, message = ex.Message });
            }
        });

        var users = secure.MapGroup("/users");

        users.MapGet(string.Empty, async (AdminUserStore userStore, CancellationToken ct) =>
        {
            var admins = await userStore.GetUsersAsync(ct);
            var dtos = admins.Select(ToDto).ToList();
            return Results.Ok(new { users = dtos });
        });

        users.MapPost(string.Empty, async (
            AdminCreateUserRequest request,
            AdminUserStore userStore,
            CancellationToken ct) =>
        {
            var username = request.Username?.Trim();
            if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
            {
                return Results.BadRequest(new { error = "Username must be at least 3 characters." });
            }

            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            {
                return Results.BadRequest(new { error = "Password must be at least 8 characters." });
            }

            try
            {
                var created = await userStore.CreateUserAsync(username, request.Password, request.IsAdmin, ct);
                return Results.Ok(ToDto(created));
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return Results.Conflict(new { error = "Username already exists." });
            }
        });

        users.MapPost("/{userId:guid}/password", async (
            Guid userId,
            AdminChangePasswordRequest request,
            AdminUserStore userStore,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            {
                return Results.BadRequest(new { error = "Password must be at least 8 characters." });
            }

            var updated = await userStore.TrySetPasswordAsync(userId, request.Password, ct);
            if (!updated)
            {
                return Results.NotFound();
            }

            return Results.NoContent();
        });

        users.MapPost("/{userId:guid}/admin", async (
            Guid userId,
            AdminSetAdminRequest request,
            AdminUserStore userStore,
            CancellationToken ct) =>
        {
            var user = await userStore.GetByIdAsync(userId, ct);
            if (user is null)
            {
                return Results.NotFound();
            }

            if (!request.IsAdmin && user.IsAdmin)
            {
                var adminCount = await userStore.CountAdminsAsync(ct);
                if (adminCount <= 1)
                {
                    return Results.BadRequest(new { error = "Cannot remove admin rights from the last admin user." });
                }
            }

            var updated = await userStore.TrySetAdminAsync(userId, request.IsAdmin, ct);
            if (!updated)
            {
                return Results.NotFound();
            }

            return Results.NoContent();
        });

        return admin;
    }

    private static AdminUserDto GetAuthenticatedUser(HttpContext context)
    {
        if (context.Items.TryGetValue(AdminAuthFilter.ContextItemKey, out var value) && value is AdminUser user)
        {
            return ToDto(user);
        }

        throw new InvalidOperationException("Admin user context missing.");
    }

    private static AdminUserDto ToDto(AdminUser user) => new(user.Id, user.Username, user.IsAdmin, user.CreatedAt, user.UpdatedAt);
}
