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
using VibeChat.Messaging;
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
    DateTimeOffset? ApiKeyRotatedAt,
    string ProcessSource = ProcessSettingsDefaults.SourceDefault,
    string OpenRouterBaseUrl = ProcessSettingsDefaults.OpenRouterBaseUrl);

public sealed record EffectiveFilesSettings(
    long MaxSizeBytes,
    int MaxAttachmentsPerMessage,
    int PresignUploadTtlSeconds,
    int PresignDownloadTtlSeconds,
    IReadOnlyList<string> AllowedContentTypes,
    long AudioMaxSizeBytes,
    int AudioMaxDurationMs,
    long VideoMaxSizeBytes,
    int VideoMaxDurationMs,
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
    void InvalidateProcess();
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

    public void InvalidateProcess() => cache.Remove(ProcessKey());

    internal static string FilesKey(TenantId tenantId) => $"runtime:files:{tenantId.Value:D}";
    internal static string RateKey(TenantId tenantId) => $"runtime:rate:{tenantId.Value:D}";
    internal static string AiKey(TenantId tenantId, WorkspaceId workspaceId) =>
        $"runtime:ai:{tenantId.Value:D}:{workspaceId.Value:D}";
    internal static string ProcessKey() => "runtime:process";
}

public sealed record EffectiveProcessSettings(
    bool AiEnabled,
    bool EmailEnabled,
    bool MessageRetentionEnabled,
    bool PushEnabled,
    bool LinkPreviewEnabled,
    string OpenRouterBaseUrl,
    int RetentionDefaultDays,
    int RetentionBatchSize,
    int RetentionIntervalMinutes,
    int LinkPreviewTimeoutMs,
    string? VapidPublicKey,
    string? VapidPrivateKey,
    string VapidSubject,
    bool VapidConfigured,
    string? VapidMask,
    string VapidSource,
    int? VapidKeyVersion,
    DateTimeOffset? VapidRotatedAt,
    string Source)
{
    public static EffectiveProcessSettings CodeDefaults() => new(
        false,
        false,
        false,
        false,
        true,
        ProcessSettingsDefaults.OpenRouterBaseUrl,
        ProcessSettingsDefaults.RetentionDefaultDays,
        ProcessSettingsDefaults.RetentionBatchSize,
        ProcessSettingsDefaults.RetentionIntervalMinutes,
        ProcessSettingsDefaults.LinkPreviewTimeoutMs,
        null,
        null,
        ProcessSettingsDefaults.VapidSubject,
        false,
        null,
        "none",
        null,
        null,
        ProcessSettingsDefaults.SourceDefault);
}

public sealed class ProcessSettingsResolver(
    VibeChatDbContext dbContext,
    IOptions<RuntimeSettingsOptions> runtimeOptions,
    RuntimeSecretProtector protector,
    IMemoryCache cache,
    ILogger<ProcessSettingsResolver> logger,
    IConfiguration configuration)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public bool DatabaseOverridesEnabled => runtimeOptions.Value.DatabaseOverridesEnabled;

    public async Task<EffectiveProcessSettings> ResolveAsync(CancellationToken cancellationToken)
    {
        if (!DatabaseOverridesEnabled)
        {
            return EffectiveProcessSettings.CodeDefaults();
        }

        if (cache.TryGetValue(RuntimeSettingsCacheInvalidator.ProcessKey(), out EffectiveProcessSettings? cached)
            && cached is not null)
        {
            return cached;
        }

        var row = await dbContext.ProcessSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == ProcessSettings.SingletonId, cancellationToken);
        var effective = row is null ? EffectiveProcessSettings.CodeDefaults() : FromRow(row);
        cache.Set(RuntimeSettingsCacheInvalidator.ProcessKey(), effective, CacheTtl);
        return effective;
    }

    private EffectiveProcessSettings FromRow(ProcessSettings row)
    {
        string? privateKey = null;
        var vapidSource = "none";
        int? keyVersion = null;
        DateTimeOffset? rotatedAt = null;
        string? mask = null;
        var publicKey = string.IsNullOrWhiteSpace(row.VapidPublicKey) ? null : row.VapidPublicKey.Trim();

        if (row.VapidPrivateKey.IsPresent)
        {
            try
            {
                privateKey = protector.Unprotect(
                    row.VapidPrivateKey,
                    RuntimeSecretKinds.VapidPrivateKey,
                    ProcessSecretScope.TenantId,
                    workspaceId: null,
                    ProcessSecretScope.EntityId);
                vapidSource = ProcessSettingsDefaults.SourceDatabase;
                keyVersion = row.VapidPrivateKey.KeyVersion;
                rotatedAt = row.VapidPrivateKey.RotatedAt;
                mask = SecretMasking.MaskFromSuffix(row.VapidPrivateKey.MaskSuffix);
            }
            catch (CryptographicException ex)
            {
                logger.LogWarning(ex, "Failed to decrypt instance VAPID private key");
                vapidSource = "unavailable";
                mask = SecretMasking.MaskFromSuffix(row.VapidPrivateKey.MaskSuffix);
                keyVersion = row.VapidPrivateKey.KeyVersion;
                rotatedAt = row.VapidPrivateKey.RotatedAt;
            }
        }
        else
        {
            var envPublic = configuration["Push:Vapid:PublicKey"];
            var envPrivate = configuration["Push:Vapid:PrivateKey"];
            if (SecretMasking.IsConfigured(envPublic) && SecretMasking.IsConfigured(envPrivate))
            {
                publicKey = envPublic!.Trim();
                privateKey = envPrivate!.Trim();
                vapidSource = "env";
                mask = SecretMasking.Mask(privateKey);
            }
        }

        var subject = string.IsNullOrWhiteSpace(row.VapidSubject)
            ? ProcessSettingsDefaults.VapidSubject
            : row.VapidSubject.Trim();
        var baseUrl = string.IsNullOrWhiteSpace(row.OpenRouterBaseUrl)
            ? ProcessSettingsDefaults.OpenRouterBaseUrl
            : OpenRouterBaseUrlPolicies.Normalize(row.OpenRouterBaseUrl);

        return new EffectiveProcessSettings(
            row.AiEnabled,
            row.EmailEnabled,
            row.MessageRetentionEnabled,
            row.PushEnabled,
            row.LinkPreviewEnabled,
            baseUrl,
            Math.Clamp(row.RetentionDefaultDays, MessageRetentionSettings.MinRetentionDays, MessageRetentionSettings.MaxRetentionDays),
            Math.Clamp(row.RetentionBatchSize <= 0 ? ProcessSettingsDefaults.RetentionBatchSize : row.RetentionBatchSize, 1, 2000),
            Math.Clamp(row.RetentionIntervalMinutes <= 0 ? ProcessSettingsDefaults.RetentionIntervalMinutes : row.RetentionIntervalMinutes, 1, 24 * 60),
            Math.Clamp(row.LinkPreviewTimeoutMs <= 0 ? ProcessSettingsDefaults.LinkPreviewTimeoutMs : row.LinkPreviewTimeoutMs, 500, 15_000),
            publicKey,
            privateKey,
            subject,
            SecretMasking.IsConfigured(publicKey) && (SecretMasking.IsConfigured(privateKey) || vapidSource == "unavailable"),
            mask,
            vapidSource,
            keyVersion,
            rotatedAt,
            ProcessSettingsDefaults.SourceDatabase);
    }
}

public sealed class AiSettingsResolver(
    VibeChatDbContext dbContext,
    IConfiguration configuration,
    IOptions<RuntimeSettingsOptions> runtimeOptions,
    RuntimeSecretProtector protector,
    ProcessSettingsResolver processSettings,
    ILogger<AiSettingsResolver> logger)
{
    private readonly RuntimeSettingsOptions _runtime = runtimeOptions.Value;

    public async Task<EffectiveAiRuntime> ResolveAsync(
        TenantId tenantId,
        WorkspaceId workspaceId,
        CancellationToken cancellationToken)
    {
        var process = await processSettings.ResolveAsync(cancellationToken);
        var envProvider = "Mock";
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
            process.AiEnabled,
            workspaceEnabled,
            provider,
            apiKey,
            source,
            SecretMasking.IsConfigured(apiKey) || source == "unavailable" && row?.OpenRouterApiKey.IsPresent == true,
            mask ?? (row?.OpenRouterApiKey.IsPresent == true
                ? SecretMasking.MaskFromSuffix(row.OpenRouterApiKey.MaskSuffix)
                : null),
            keyVersion,
            rotatedAt,
            process.Source,
            process.OpenRouterBaseUrl);
    }

    public bool DatabaseOverridesEnabled => _runtime.DatabaseOverridesEnabled;
}

public sealed class FilesSettingsResolver(
    VibeChatDbContext dbContext,
    IOptions<RuntimeSettingsOptions> runtimeOptions,
    IMemoryCache cache)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public async Task<EffectiveFilesSettings> ResolveAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        var ceiling = ReadCeiling();
        if (!runtimeOptions.Value.DatabaseOverridesEnabled)
        {
            return ceiling;
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
            effective = ceiling;
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
                ceiling.VideoMaxSizeBytes,
                ceiling.VideoMaxDurationMs,
                ProcessSettingsDefaults.SourceDatabase);
        }

        cache.Set(cacheKey, effective, CacheTtl);
        return effective;
    }

    public EffectiveFilesSettings ReadCeiling() =>
        new(
            AttachmentPolicies.DefaultMaxSizeBytes,
            AttachmentPolicies.DefaultMaxAttachmentsPerMessage,
            AttachmentPolicies.DefaultUploadTtlSeconds,
            AttachmentPolicies.DefaultDownloadTtlSeconds,
            AttachmentPolicies.DefaultAllowedContentTypes.ToArray(),
            AttachmentPolicies.DefaultAudioMaxSizeBytes,
            AttachmentPolicies.DefaultAudioMaxDurationMs,
            AttachmentPolicies.DefaultVideoMaxSizeBytes,
            AttachmentPolicies.DefaultVideoMaxDurationMs,
            ProcessSettingsDefaults.SourceDefault);

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
    IOptions<RuntimeSettingsOptions> runtimeOptions,
    IMemoryCache cache)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public async Task<EffectiveRateLimitSettings> ResolveAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        var ceilingSend = RateLimitPolicies.MaxPerMinute;
        var ceilingHub = RateLimitPolicies.MaxPerMinute;

        if (!runtimeOptions.Value.DatabaseOverridesEnabled)
        {
            return new EffectiveRateLimitSettings(
                RateLimitPolicies.DefaultSendPerMinute,
                RateLimitPolicies.DefaultHubPerMinute,
                ProcessSettingsDefaults.SourceDefault);
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
            effective = new EffectiveRateLimitSettings(
                RateLimitPolicies.DefaultSendPerMinute,
                RateLimitPolicies.DefaultHubPerMinute,
                ProcessSettingsDefaults.SourceDefault);
        }
        else
        {
            effective = new EffectiveRateLimitSettings(
                Math.Clamp(Math.Min(row.SendPerMinute, ceilingSend), RateLimitPolicies.MinPerMinute, RateLimitPolicies.MaxPerMinute),
                Math.Clamp(Math.Min(row.HubPerMinute, ceilingHub), RateLimitPolicies.MinPerMinute, RateLimitPolicies.MaxPerMinute),
                ProcessSettingsDefaults.SourceDatabase);
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
