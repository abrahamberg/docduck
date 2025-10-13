using DocDuck.Providers.Providers.Settings;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace DocDuck.Providers.Configuration;

/// <summary>
/// Seeds initial provider settings from environment variables when the database table is empty.
/// </summary>
public sealed class ProviderSettingsSeeder
{
    private readonly ProviderSettingsStore _store;
    private readonly ILogger<ProviderSettingsSeeder> _logger;

    public ProviderSettingsSeeder(ProviderSettingsStore store, ILogger<ProviderSettingsSeeder> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task SeedFromEnvironmentAsync(CancellationToken ct = default)
    {
        var seeders = new Func<CancellationToken, Task>[]
        {
            SeedOneDriveAsync,
            SeedLocalAsync,
            SeedS3Async
        };

        foreach (var seeder in seeders)
        {
            try
            {
                await seeder(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to seed provider settings from environment");
            }
        }
    }

    private async Task SeedOneDriveAsync(CancellationToken ct)
    {
        var enabled = GetBool("ONEDRIVE_ENABLED");
        if (!enabled)
        {
            return;
        }

        var name = Environment.GetEnvironmentVariable("ONEDRIVE_NAME") ?? "OneDrive";
        var existing = await _store.GetAsync("onedrive", name, ct);
        if (existing != null)
        {
            existing.Payload.Dispose();
            return;
        }

        var settings = new OneDriveProviderSettings
        {
            Enabled = true,
            Name = name,
            AccountType = Environment.GetEnvironmentVariable("ONEDRIVE_ACCOUNT_TYPE") ?? "business",
            TenantId = Environment.GetEnvironmentVariable("ONEDRIVE_TENANT_ID"),
            ClientId = Environment.GetEnvironmentVariable("ONEDRIVE_CLIENT_ID"),
            ClientSecret = Environment.GetEnvironmentVariable("ONEDRIVE_CLIENT_SECRET"),
            SiteId = Environment.GetEnvironmentVariable("ONEDRIVE_SITE_ID"),
            DriveId = Environment.GetEnvironmentVariable("ONEDRIVE_DRIVE_ID"),
            FolderPath = Environment.GetEnvironmentVariable("ONEDRIVE_FOLDER_PATH") ?? "/Shared Documents/Docs"
        };

        settings.Validate();
        await _store.UpsertAsync(settings, ct);
        _logger.LogInformation("Seeded OneDrive provider settings from environment");
    }

    private async Task SeedLocalAsync(CancellationToken ct)
    {
        var enabled = GetBool("LOCAL_PROVIDER_ENABLED");
        if (!enabled)
        {
            return;
        }

        var name = Environment.GetEnvironmentVariable("LOCAL_PROVIDER_NAME") ?? "LocalFiles";
        var existing = await _store.GetAsync("local", name, ct);
        if (existing != null)
        {
            existing.Payload.Dispose();
            return;
        }

        var settings = new LocalProviderSettings
        {
            Enabled = true,
            Name = name,
            RootPath = Environment.GetEnvironmentVariable("LOCAL_PROVIDER_ROOT_PATH") ?? "/data/documents"
        };

        var extensions = Environment.GetEnvironmentVariable("LOCAL_PROVIDER_EXTENSIONS");
        if (!string.IsNullOrWhiteSpace(extensions))
        {
            settings.FileExtensions = extensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        settings.Validate();
        await _store.UpsertAsync(settings, ct);
        _logger.LogInformation("Seeded local provider settings from environment");
    }

    private async Task SeedS3Async(CancellationToken ct)
    {
        var enabled = GetBool("S3_ENABLED");
        if (!enabled)
        {
            return;
        }

        var name = Environment.GetEnvironmentVariable("S3_NAME") ?? "S3";
        var existing = await _store.GetAsync("s3", name, ct);
        if (existing != null)
        {
            existing.Payload.Dispose();
            return;
        }

        var settings = new S3ProviderSettings
        {
            Enabled = true,
            Name = name,
            BucketName = Environment.GetEnvironmentVariable("S3_BUCKET_NAME") ?? string.Empty,
            Prefix = Environment.GetEnvironmentVariable("S3_PREFIX"),
            Region = Environment.GetEnvironmentVariable("S3_REGION") ?? "us-east-1",
            AccessKeyId = Environment.GetEnvironmentVariable("S3_ACCESS_KEY_ID"),
            SecretAccessKey = Environment.GetEnvironmentVariable("S3_SECRET_ACCESS_KEY"),
            SessionToken = Environment.GetEnvironmentVariable("S3_SESSION_TOKEN"),
            UseInstanceProfile = GetBool("S3_USE_INSTANCE_PROFILE")
        };

        var extensions = Environment.GetEnvironmentVariable("S3_FILE_EXTENSIONS");
        if (!string.IsNullOrWhiteSpace(extensions))
        {
            settings.FileExtensions = extensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        settings.Validate();
        await _store.UpsertAsync(settings, ct);
        _logger.LogInformation("Seeded S3 provider settings from environment");
    }

    private static bool GetBool(string envVar)
    {
        return bool.TryParse(Environment.GetEnvironmentVariable(envVar), out var value) && value;
    }
}
