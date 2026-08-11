using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.AI;
using VibeChat.BuildingBlocks;
using VibeChat.Files;
using VibeChat.Integrations;
using VibeChat.Notifications;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

public sealed record EffectiveAiRuntime(
    bool ProcessEnabled,
    bool WorkspaceEnabled,
    string Provider,
    string? ApiKey,
    string ApiKeySource,
    bool ApiKeyConfigured,
    string? ApiKeyMask,
    int? ApiKeyKeyVersion,
    DateTimeOffset? ApiKeyRotatedAt);

public sealed record EffectiveFilesSettings(
    long MaxSizeBytes,
    int MaxAttachmentsPerMessage,
    int PresignUploadTtlSeconds,
    int PresignDownloadTtlSeconds,
    IReadOnlyList<string> AllowedContentTypes,
    long AudioMaxSizeBytes,
    int AudioMaxDurationMs,
    string Source);

public sealed record EffectiveRateLimitSettings(
    int SendPerMinute,
    int HubPerMinute,
    string Source);

public sealed record EffectiveWebhookEndpoint(
    bool Enabled,
    string Url,
    string? SigningSecret,
    string SecretSource,
    bool SecretConfigured,
    string? SecretMask,
    int? SecretKeyVersion,
    DateTimeOffset? SecretRotatedAt);

public interface IRuntimeSettingsCacheInvalidator
{
    void InvalidateTenant(TenantId tenantId);
    void InvalidateWorkspace(TenantId tenantId, WorkspaceId workspaceId);
}

public sealed class RuntimeSettingsCacheInvalidator(IMemoryCache cache) : IRuntimeSettingsCacheInvalidator
{
    public void InvalidateTenant(TenantId tenantId)
    {
        cache.Remove(FilesKey(tenantId));
        cache.Remove(RateKey(tenantId));
    }

    public void InvalidateWorkspace(TenantId tenantId, WorkspaceId workspaceId) =>
        cache.Remove(AiKey(tenantId, workspaceId));

    internal static string FilesKey(TenantId tenantId) => $"runtime:files:{tenantId.Value:D}";
    internal static string RateKey(TenantId tenantId) => $"runtime:rate:{tenantId.Value:D}";
    internal static string AiKey(TenantId tenantId, WorkspaceId workspaceId) =>
        $"runtime:ai:{tenantId.Value:D}:{workspaceId.Value:D}";
}

public sealed class AiSettingsResolver(
    VibeChatDbContext dbContext,
    IConfiguration configuration,
    IOptions<RuntimeSettingsOptions> runtimeOptions,
    RuntimeSecretProtector protector,
    ILogger<AiSettingsResolver> logger)
{
    private readonly RuntimeSettingsOptions _runtime = runtimeOptions.Value;

    public async Task<EffectiveAiRuntime> ResolveAsync(
        TenantId tenantId,
        WorkspaceId workspaceId,
        CancellationToken cancellationToken)
    {
        var processEnabled = configuration.GetValue("Ai:Enabled", false);
        var envProvider = configuration["Ai:Provider"] ?? "Mock";
        var envKey = configuration["Ai:OpenRouter:ApiKey"] ?? configuration["OPENROUTER_API_KEY"];

        var row = await dbContext.AiSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId, cancellationToken);

        var workspaceEnabled = row?.Enabled ?? false;
        var provider = row?.Provider ?? envProvider;
        if (!string.Equals(provider, "OpenRouter", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(provider, "Mock", StringComparison.OrdinalIgnoreCase))
        {
            provider = "Mock";
        }
        else if (string.Equals(provider, "OpenRouter", StringComparison.OrdinalIgnoreCase))
        {
            provider = "OpenRouter";
        }
        else
        {
            provider = "Mock";
        }

        string? apiKey = null;
        var source = "none";
        int? keyVersion = null;
        DateTimeOffset? rotatedAt = null;
        string? mask = null;

        if (row?.OpenRouterApiKey.IsPresent == true)
        {
            try
            {
                apiKey = protector.Unprotect(
                    row.OpenRouterApiKey,
                    RuntimeSecretKinds.OpenRouterApiKey,
                    tenantId,
                    workspaceId,
                    workspaceId.Value.ToString("D"));
                source = "database";
                keyVersion = row.OpenRouterApiKey.KeyVersion;
                rotatedAt = row.OpenRouterApiKey.RotatedAt;
                mask = SecretMasking.MaskFromSuffix(row.OpenRouterApiKey.MaskSuffix);
            }
            catch (CryptographicException ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to decrypt OpenRouter API key for workspace {WorkspaceId}",
                    workspaceId.Value);
                source = "unavailable";
            }
        }
        else if (SecretMasking.IsConfigured(envKey))
        {
            apiKey = envKey!.Trim();
            source = "env";
            mask = SecretMasking.Mask(apiKey);
        }

        return new EffectiveAiRuntime(
            processEnabled,
            workspaceEnabled,
            provider,
            apiKey,
            source,
            SecretMasking.IsConfigured(apiKey) || source == "unavailable" && row?.OpenRouterApiKey.IsPresent == true,
            mask ?? (row?.OpenRouterApiKey.IsPresent == true
                ? SecretMasking.MaskFromSuffix(row.OpenRouterApiKey.MaskSuffix)
                : null),
            keyVersion,
            rotatedAt);
    }

    public bool DatabaseOverridesEnabled => _runtime.DatabaseOverridesEnabled;
}

public sealed class FilesSettingsResolver(
    VibeChatDbContext dbContext,
    IConfiguration configuration,
    IOptions<RuntimeSettingsOptions> runtimeOptions,
    IMemoryCache cache)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public async Task<EffectiveFilesSettings> ResolveAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        var ceiling = ReadCeiling();
        if (!runtimeOptions.Value.DatabaseOverridesEnabled)
        {
            return ceiling with { Source = "env" };
        }

        var cacheKey = RuntimeSettingsCacheInvalidator.FilesKey(tenantId);
        if (cache.TryGetValue(cacheKey, out EffectiveFilesSettings? cached) && cached is not null)
        {
            return cached;
        }

        var row = await dbContext.TenantFilesSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);

        EffectiveFilesSettings effective;
        if (row is null)
        {
            effective = ceiling with { Source = "env" };
        }
        else
        {
            var allowed = ClampAllowedTypes(row.AllowedContentTypes, ceiling.AllowedContentTypes);
            effective = new EffectiveFilesSettings(
                Math.Clamp(row.MaxSizeBytes, 1, ceiling.MaxSizeBytes),
                Math.Clamp(row.MaxAttachmentsPerMessage, 1, ceiling.MaxAttachmentsPerMessage),
                Math.Clamp(row.PresignUploadTtlSeconds, 60, ceiling.PresignUploadTtlSeconds),
                Math.Clamp(row.PresignDownloadTtlSeconds, 30, ceiling.PresignDownloadTtlSeconds),
                allowed,
                Math.Clamp(row.AudioMaxSizeBytes, 1, ceiling.AudioMaxSizeBytes),
                Math.Clamp(row.AudioMaxDurationMs, 1_000, ceiling.AudioMaxDurationMs),
                "database");
        }

        cache.Set(cacheKey, effective, CacheTtl);
        return effective;
    }

    public EffectiveFilesSettings ReadCeiling()
    {
        var allowed = configuration.GetSection("Files:AllowedContentTypes").Get<string[]>()
            ?? AttachmentPolicies.DefaultAllowedContentTypes.ToArray();
        return new EffectiveFilesSettings(
            configuration.GetValue("Files:MaxSizeBytes", AttachmentPolicies.DefaultMaxSizeBytes),
            configuration.GetValue("Files:MaxAttachmentsPerMessage", AttachmentPolicies.DefaultMaxAttachmentsPerMessage),
            configuration.GetValue("Files:PresignUploadTtlSeconds", AttachmentPolicies.DefaultUploadTtlSeconds),
            configuration.GetValue("Files:PresignDownloadTtlSeconds", AttachmentPolicies.DefaultDownloadTtlSeconds),
            allowed,
            configuration.GetValue("Files:Audio:MaxSizeBytes", AttachmentPolicies.DefaultAudioMaxSizeBytes),
            configuration.GetValue("Files:Audio:MaxDurationMs", AttachmentPolicies.DefaultAudioMaxDurationMs),
            "env");
    }

    private static IReadOnlyList<string> ClampAllowedTypes(string[]? tenantTypes, IReadOnlyList<string> ceiling)
    {
        if (tenantTypes is null || tenantTypes.Length == 0)
        {
            return ceiling;
        }

        var set = ceiling.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filtered = tenantTypes
            .Where(t => !string.IsNullOrWhiteSpace(t) && set.Contains(t.Trim()))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return filtered.Length == 0 ? ceiling : filtered;
    }
}

public sealed class RateLimitSettingsResolver(
    VibeChatDbContext dbContext,
    IConfiguration configuration,
    IOptions<RuntimeSettingsOptions> runtimeOptions,
    IMemoryCache cache)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public async Task<EffectiveRateLimitSettings> ResolveAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        var ceilingSend = configuration.GetValue("RateLimit:SendPerMinute", RateLimitPolicies.DefaultSendPerMinute);
        var ceilingHub = configuration.GetValue("RateLimit:HubPerMinute", RateLimitPolicies.DefaultHubPerMinute);
        ceilingSend = Math.Clamp(ceilingSend, RateLimitPolicies.MinPerMinute, RateLimitPolicies.MaxPerMinute);
        ceilingHub = Math.Clamp(ceilingHub, RateLimitPolicies.MinPerMinute, RateLimitPolicies.MaxPerMinute);

        if (!runtimeOptions.Value.DatabaseOverridesEnabled)
        {
            return new EffectiveRateLimitSettings(ceilingSend, ceilingHub, "env");
        }

        var cacheKey = RuntimeSettingsCacheInvalidator.RateKey(tenantId);
        if (cache.TryGetValue(cacheKey, out EffectiveRateLimitSettings? cached) && cached is not null)
        {
            return cached;
        }

        var row = await dbContext.TenantRateLimitSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);

        EffectiveRateLimitSettings effective;
        if (row is null)
        {
            effective = new EffectiveRateLimitSettings(ceilingSend, ceilingHub, "env");
        }
        else
        {
            effective = new EffectiveRateLimitSettings(
                Math.Clamp(Math.Min(row.SendPerMinute, ceilingSend), RateLimitPolicies.MinPerMinute, RateLimitPolicies.MaxPerMinute),
                Math.Clamp(Math.Min(row.HubPerMinute, ceilingHub), RateLimitPolicies.MinPerMinute, RateLimitPolicies.MaxPerMinute),
                "database");
        }

        cache.Set(cacheKey, effective, CacheTtl);
        return effective;
    }
}

public sealed class WebhookEndpointResolver(
    VibeChatDbContext dbContext,
    RuntimeSecretProtector protector,
    ILogger<WebhookEndpointResolver> logger)
{
    public async Task<EffectiveWebhookEndpoint?> ResolveAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        var row = await dbContext.OutboundWebhookEndpoints.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        string? secret = null;
        var source = "none";
        int? keyVersion = null;
        DateTimeOffset? rotatedAt = null;
        string? mask = null;

        if (row.SigningSecret.IsPresent)
        {
            try
            {
                secret = protector.Unprotect(
                    row.SigningSecret,
                    RuntimeSecretKinds.WebhookSigningSecret,
                    tenantId,
                    workspaceId: null,
                    tenantId.Value.ToString("D"));
                source = "database";
                keyVersion = row.SigningSecret.KeyVersion;
                rotatedAt = row.SigningSecret.RotatedAt;
                mask = SecretMasking.MaskFromSuffix(row.SigningSecret.MaskSuffix);
            }
            catch (CryptographicException ex)
            {
                logger.LogWarning(ex, "Failed to decrypt webhook signing secret for tenant {TenantId}", tenantId.Value);
                source = "unavailable";
                mask = SecretMasking.MaskFromSuffix(row.SigningSecret.MaskSuffix);
            }
        }
        else if (SecretMasking.IsConfigured(row.Secret))
        {
            secret = row.Secret!.Trim();
            source = "legacy";
            mask = SecretMasking.Mask(secret);
        }

        return new EffectiveWebhookEndpoint(
            row.Enabled,
            row.Url ?? string.Empty,
            secret,
            source,
            SecretMasking.IsConfigured(secret) || source == "unavailable",
            mask,
            keyVersion,
            rotatedAt);
    }
}
