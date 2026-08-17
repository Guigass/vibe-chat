using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.AI;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Files;
using VibeChat.Integrations;
using VibeChat.Messaging;
using VibeChat.Notifications;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Infrastructure;

public sealed record CredentialRotateResult(
    bool Ok,
    int StatusCode,
    string? Error,
    string? Message,
    bool Configured,
    string? Mask,
    int? KeyVersion,
    DateTimeOffset? RotatedAt);

public sealed class RuntimeSettingsAdminService(
    VibeChatDbContext db,
    EmailSettingsResolver emailSettings,
    AiSettingsResolver aiSettings,
    FilesSettingsResolver filesSettings,
    RateLimitSettingsResolver rateLimits,
    WebhookEndpointResolver webhooks,
    RuntimeSecretProtector protector,
    IOptions<RuntimeSettingsOptions> runtimeOptions,
    IRuntimeSettingsCacheInvalidator cacheInvalidator,
    IAuditWriter audit,
    IClock clock,
    ProcessSettingsResolver processSettings)
{
    private readonly RuntimeSettingsOptions _runtime = runtimeOptions.Value;

    public bool DatabaseOverridesEnabled => _runtime.DatabaseOverridesEnabled;

    public async Task<object> BuildResponseAsync(Workspace workspace, CancellationToken ct)
    {
        var ai = await aiSettings.ResolveAsync(workspace.TenantId, workspace.Id, ct);
        var smtp = await emailSettings.ResolveAsync(workspace.TenantId, ct);
        var webhook = await webhooks.ResolveAsync(workspace.TenantId, ct);
        var files = await filesSettings.ResolveAsync(workspace.TenantId, ct);
        var rate = await rateLimits.ResolveAsync(workspace.TenantId, ct);
        var process = await processSettings.ResolveAsync(ct);
        var filesCeiling = filesSettings.ReadCeiling();

        var processRetentionEnabled = process.MessageRetentionEnabled;
        var defaultRetentionDays = process.RetentionDefaultDays;

        var retention = await db.MessageRetentionSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId, ct);
        var retentionEnabled = retention?.Enabled ?? false;
        var retentionDays = retention?.RetentionDays > 0 ? retention.RetentionDays : defaultRetentionDays;
        var retentionMessage = !processRetentionEnabled
            ? "Purge desligado no processo. Política do tenant é gravável, mas o worker não hard-deleta."
            : retentionEnabled
                ? $"Purge ativo: soft-deletes com mais de {retentionDays} dias serão hard-deleted pelo worker."
                : "Purge do tenant desligado — soft-deletes permanecem até habilitar.";

        var webhookUrl = webhook?.Url ?? string.Empty;
        var webhookUrlConfigured = SecretMasking.IsConfigured(webhookUrl);
        var webhookSecretConfigured = webhook?.SecretConfigured ?? false;
        var webhookEnabled = webhook?.Enabled ?? false;
        var webhookStatus = WebhooksSettingsStatus.Resolve(webhookEnabled, webhookUrlConfigured, webhookSecretConfigured);

        var credentialsOnActive = 0;
        var activeVersion = protector.IsEncryptionAvailable ? protector.ActiveKeyVersion : (int?)null;
        if (activeVersion is int av)
        {
            if (ai.ApiKeyKeyVersion == av)
            {
                credentialsOnActive++;
            }

            if (smtp.PasswordKeyVersion == av)
            {
                credentialsOnActive++;
            }

            if (webhook?.SecretKeyVersion == av)
            {
                credentialsOnActive++;
            }

            if (process.VapidKeyVersion == av)
            {
                credentialsOnActive++;
            }
        }

        var linkPreviewTenantEnabled = (await db.TenantLinkPreviewSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId, ct))?.Enabled ?? true;

        return new
        {
            workspaceId = workspace.Id.Value,
            ai = new
            {
                processEnabled = process.AiEnabled,
                processSource = process.Source,
                workspaceEnabled = ai.WorkspaceEnabled,
                provider = ai.Provider,
                openRouterBaseUrl = process.OpenRouterBaseUrl,
                apiKeyConfigured = ai.ApiKeyConfigured,
                apiKeyMask = ai.ApiKeyMask,
                apiKeySource = ai.ApiKeySource,
                apiKeyKeyVersion = ai.ApiKeyKeyVersion,
                apiKeyRotatedAt = ai.ApiKeyRotatedAt,
                secretsWritable = DatabaseOverridesEnabled && protector.IsEncryptionAvailable
            },
            email = new
            {
                processEnabled = process.EmailEnabled,
                processSource = process.Source,
                enabled = smtp.Enabled,
                source = smtp.Source,
                smtpHost = smtp.Host,
                smtpPort = smtp.Port,
                smtpUsername = smtp.Username,
                smtpUsernameConfigured = SecretMasking.IsConfigured(smtp.Username),
                smtpPasswordConfigured = smtp.PasswordSource is "database" or "env" or "unavailable",
                smtpPasswordMask = smtp.PasswordMask,
                smtpPasswordSource = smtp.PasswordSource,
                smtpPasswordKeyVersion = smtp.PasswordKeyVersion,
                smtpPasswordRotatedAt = smtp.PasswordRotatedAt,
                smtpFrom = smtp.From,
                useStartTls = smtp.UseStartTls,
                secretsWritable = DatabaseOverridesEnabled && protector.IsEncryptionAvailable
            },
            webhooks = new
            {
                status = webhookStatus,
                enabled = webhookEnabled,
                url = webhookUrlConfigured ? webhookUrl.Trim() : string.Empty,
                urlConfigured = webhookUrlConfigured,
                secretConfigured = webhookSecretConfigured,
                secretMask = webhook?.SecretMask,
                secretSource = webhook?.SecretSource ?? "none",
                secretKeyVersion = webhook?.SecretKeyVersion,
                secretRotatedAt = webhook?.SecretRotatedAt,
                secretsWritable = DatabaseOverridesEnabled && protector.IsEncryptionAvailable,
                message = WebhooksSettingsStatus.MessageFor(webhookStatus)
            },
            retention = new
            {
                processEnabled = processRetentionEnabled,
                processSource = process.Source,
                enabled = retentionEnabled,
                retentionDays,
                defaultRetentionDays,
                batchSize = process.RetentionBatchSize,
                intervalMinutes = process.RetentionIntervalMinutes,
                message = retentionMessage
            },
            linkPreview = new
            {
                processEnabled = process.LinkPreviewEnabled,
                processSource = process.Source,
                enabled = linkPreviewTenantEnabled,
                timeoutMs = process.LinkPreviewTimeoutMs,
                message = process.LinkPreviewEnabled
                    ? "Worker busca Open Graph da primeira URL (guarda SSRF)."
                    : "Link preview desligado no processo."
            },
            push = new
            {
                processEnabled = process.PushEnabled,
                processSource = process.Source,
                vapidPublicKey = process.VapidConfigured ? process.VapidPublicKey : null,
                vapidConfigured = process.VapidConfigured,
                vapidMask = process.VapidMask,
                vapidSource = process.VapidSource,
                vapidKeyVersion = process.VapidKeyVersion,
                vapidRotatedAt = process.VapidRotatedAt,
                vapidSubject = process.VapidSubject,
                secretsWritable = DatabaseOverridesEnabled && protector.IsEncryptionAvailable
            },
            files = new
            {
                source = files.Source,
                maxSizeBytes = files.MaxSizeBytes,
                maxAttachmentsPerMessage = files.MaxAttachmentsPerMessage,
                presignUploadTtlSeconds = files.PresignUploadTtlSeconds,
                presignDownloadTtlSeconds = files.PresignDownloadTtlSeconds,
                allowedContentTypes = files.AllowedContentTypes,
                audioMaxSizeBytes = files.AudioMaxSizeBytes,
                audioMaxDurationMs = files.AudioMaxDurationMs,
                ceilingMaxSizeBytes = filesCeiling.MaxSizeBytes,
                ceilingMaxAttachmentsPerMessage = filesCeiling.MaxAttachmentsPerMessage
            },
            rateLimit = new
            {
                source = rate.Source,
                sendPerMinute = rate.SendPerMinute,
                hubPerMinute = rate.HubPerMinute,
                ceilingSendPerMinute = RateLimitPolicies.MaxPerMinute,
                ceilingHubPerMinute = RateLimitPolicies.MaxPerMinute
            },
            encryption = new
            {
                databaseOverridesEnabled = DatabaseOverridesEnabled,
                encryptionAvailable = protector.IsEncryptionAvailable,
                activeKeyVersion = activeVersion,
                credentialsUsingActiveKey = credentialsOnActive
            }
        };
    }

    public async Task<CredentialRotateResult> RotateAsync(
        Workspace workspace,
        UserId actorUserId,
        string kind,
        string value,
        CancellationToken ct)
    {
        if (!DatabaseOverridesEnabled)
        {
            return new CredentialRotateResult(false, 503, "RuntimeSettingsDisabled",
                "RuntimeSettings:DatabaseOverridesEnabled is false.", false, null, null, null);
        }

        if (!protector.IsEncryptionAvailable)
        {
            return new CredentialRotateResult(false, 503, "RuntimeSecretEncryptionUnavailable",
                "Encryption keyring is not configured.", false, null, null, null);
        }

        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < 8)
        {
            return new CredentialRotateResult(false, 400, "InvalidSecret",
                "Secret must be at least 8 characters.", false, null, null, null);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 512)
        {
            return new CredentialRotateResult(false, 400, "InvalidSecret",
                "Secret exceeds 512 characters.", false, null, null, null);
        }

        var now = clock.UtcNow;
        EncryptedSecretEnvelope envelope;
        string entityType;
        string entityId;

        try
        {
            switch (kind)
            {
                case RuntimeSecretKinds.OpenRouterApiKey:
                    {
                        var row = await db.AiSettings
                            .FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId && x.WorkspaceId == workspace.Id, ct);
                        if (row is null)
                        {
                            row = new AiSettings
                            {
                                WorkspaceId = workspace.Id,
                                TenantId = workspace.TenantId,
                                Enabled = false,
                                Provider = "Mock"
                            };
                            db.AiSettings.Add(row);
                        }

                        envelope = protector.Protect(
                            trimmed,
                            RuntimeSecretKinds.OpenRouterApiKey,
                            workspace.TenantId,
                            workspace.Id,
                            workspace.Id.Value.ToString("D"),
                            now);
                        row.OpenRouterApiKey.CopyFrom(envelope);
                        entityType = "AiSettings";
                        entityId = workspace.Id.Value.ToString("D");
                        break;
                    }
                case RuntimeSecretKinds.SmtpPassword:
                    {
                        var row = await db.TenantEmailSettings
                            .FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId, ct);
                        if (row is null)
                        {
                            var baseline = await emailSettings.ResolveAsync(workspace.TenantId, ct);
                            row = new TenantEmailSettings
                            {
                                TenantId = workspace.TenantId,
                                Enabled = baseline.Enabled,
                                Host = baseline.Host,
                                Port = baseline.Port,
                                Username = baseline.Username,
                                From = baseline.From,
                                UseStartTls = baseline.UseStartTls,
                                UpdatedAt = now
                            };
                            db.TenantEmailSettings.Add(row);
                        }

                        envelope = protector.Protect(
                            trimmed,
                            RuntimeSecretKinds.SmtpPassword,
                            workspace.TenantId,
                            workspaceId: null,
                            workspace.TenantId.Value.ToString("D"),
                            now);
                        row.SmtpPassword.CopyFrom(envelope);
                        row.UpdatedAt = now;
                        entityType = "TenantEmailSettings";
                        entityId = workspace.TenantId.Value.ToString("D");
                        break;
                    }
                case RuntimeSecretKinds.WebhookSigningSecret:
                    {
                        var row = await db.OutboundWebhookEndpoints
                            .FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId, ct);
                        if (row is null)
                        {
                            row = new OutboundWebhookEndpoint
                            {
                                TenantId = workspace.TenantId,
                                Enabled = false,
                                Url = string.Empty,
                                Secret = null,
                                UpdatedAt = now
                            };
                            db.OutboundWebhookEndpoints.Add(row);
                        }

                        envelope = protector.Protect(
                            trimmed,
                            RuntimeSecretKinds.WebhookSigningSecret,
                            workspace.TenantId,
                            workspaceId: null,
                            workspace.TenantId.Value.ToString("D"),
                            now);
                        row.SigningSecret.CopyFrom(envelope);
                        row.Secret = null;
                        row.UpdatedAt = now;
                        entityType = "OutboundWebhookEndpoint";
                        entityId = workspace.TenantId.Value.ToString("D");
                        break;
                    }
                default:
                    return new CredentialRotateResult(false, 400, "InvalidCredentialKind",
                        "Unknown credential kind.", false, null, null, null);
            }
        }
        catch (CryptographicException)
        {
            return new CredentialRotateResult(false, 503, "RuntimeSecretEncryptionUnavailable",
                "Encryption keyring is not configured.", false, null, null, null);
        }

        audit.Add(new AuditEvent
        {
            TenantId = workspace.TenantId,
            ActorUserId = actorUserId,
            Action = AuditActions.SettingsCredentialRotate,
            EntityType = entityType,
            EntityId = entityId,
            MetadataJson = JsonSerializer.Serialize(new
            {
                workspaceId = workspace.Id.Value,
                kind,
                keyVersion = envelope.KeyVersion
            })
        });
        await db.SaveChangesAsync(ct);
        cacheInvalidator.InvalidateTenant(workspace.TenantId);
        cacheInvalidator.InvalidateWorkspace(workspace.TenantId, workspace.Id);

        return new CredentialRotateResult(
            true,
            200,
            null,
            null,
            true,
            SecretMasking.MaskFromSuffix(envelope.MaskSuffix),
            envelope.KeyVersion,
            envelope.RotatedAt);
    }

    public async Task<(bool Ok, int Status, string? Error, string? Message, int Reencrypted)> ReencryptAsync(
        Workspace workspace,
        UserId actorUserId,
        CancellationToken ct)
    {
        if (!DatabaseOverridesEnabled)
        {
            return (false, 503, "RuntimeSettingsDisabled", "RuntimeSettings:DatabaseOverridesEnabled is false.", 0);
        }

        if (!protector.IsEncryptionAvailable)
        {
            return (false, 503, "RuntimeSecretEncryptionUnavailable", "Encryption keyring is not configured.", 0);
        }

        var now = clock.UtcNow;
        var count = 0;
        var previousVersions = new List<int>();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var ai = await db.AiSettings
                .FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId && x.WorkspaceId == workspace.Id, ct);
            if (ai?.OpenRouterApiKey.IsPresent == true)
            {
                previousVersions.Add(ai.OpenRouterApiKey.KeyVersion!.Value);
                ai.OpenRouterApiKey.CopyFrom(protector.Reencrypt(
                    ai.OpenRouterApiKey,
                    RuntimeSecretKinds.OpenRouterApiKey,
                    workspace.TenantId,
                    workspace.Id,
                    workspace.Id.Value.ToString("D"),
                    now));
                count++;
            }

            var email = await db.TenantEmailSettings
                .FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId, ct);
            if (email?.SmtpPassword.IsPresent == true)
            {
                previousVersions.Add(email.SmtpPassword.KeyVersion!.Value);
                email.SmtpPassword.CopyFrom(protector.Reencrypt(
                    email.SmtpPassword,
                    RuntimeSecretKinds.SmtpPassword,
                    workspace.TenantId,
                    workspaceId: null,
                    workspace.TenantId.Value.ToString("D"),
                    now));
                email.UpdatedAt = now;
                count++;
            }

            var hook = await db.OutboundWebhookEndpoints
                .FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId, ct);
            if (hook is not null)
            {
                if (hook.SigningSecret.IsPresent)
                {
                    previousVersions.Add(hook.SigningSecret.KeyVersion!.Value);
                    hook.SigningSecret.CopyFrom(protector.Reencrypt(
                        hook.SigningSecret,
                        RuntimeSecretKinds.WebhookSigningSecret,
                        workspace.TenantId,
                        workspaceId: null,
                        workspace.TenantId.Value.ToString("D"),
                        now));
                    hook.Secret = null;
                    hook.UpdatedAt = now;
                    count++;
                }
                else if (SecretMasking.IsConfigured(hook.Secret))
                {
                    hook.SigningSecret.CopyFrom(protector.Protect(
                        hook.Secret!.Trim(),
                        RuntimeSecretKinds.WebhookSigningSecret,
                        workspace.TenantId,
                        workspaceId: null,
                        workspace.TenantId.Value.ToString("D"),
                        now));
                    hook.Secret = null;
                    hook.UpdatedAt = now;
                    count++;
                    audit.Add(new AuditEvent
                    {
                        TenantId = workspace.TenantId,
                        ActorUserId = actorUserId,
                        Action = AuditActions.SettingsLegacySecretMigrate,
                        EntityType = "OutboundWebhookEndpoint",
                        EntityId = workspace.TenantId.Value.ToString("D"),
                        MetadataJson = JsonSerializer.Serialize(new { workspaceId = workspace.Id.Value })
                    });
                }
            }

            var processRow = await db.ProcessSettings
                .FirstOrDefaultAsync(x => x.Id == ProcessSettings.SingletonId, ct);
            if (processRow?.VapidPrivateKey.IsPresent == true)
            {
                previousVersions.Add(processRow.VapidPrivateKey.KeyVersion!.Value);
                processRow.VapidPrivateKey.CopyFrom(protector.Reencrypt(
                    processRow.VapidPrivateKey,
                    RuntimeSecretKinds.VapidPrivateKey,
                    ProcessSecretScope.TenantId,
                    workspaceId: null,
                    ProcessSecretScope.EntityId,
                    now));
                processRow.UpdatedAt = now;
                count++;
            }

            audit.Add(new AuditEvent
            {
                TenantId = workspace.TenantId,
                ActorUserId = actorUserId,
                Action = AuditActions.SettingsEncryptionReencrypt,
                EntityType = "SensitiveSettings",
                EntityId = workspace.Id.Value.ToString("D"),
                MetadataJson = JsonSerializer.Serialize(new
                {
                    workspaceId = workspace.Id.Value,
                    reencrypted = count,
                    activeKeyVersion = protector.ActiveKeyVersion,
                    previousVersions = previousVersions.Distinct().ToArray()
                })
            });

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (CryptographicException)
        {
            await tx.RollbackAsync(ct);
            return (false, 503, "RuntimeSecretEncryptionUnavailable", "Re-encryption failed.", 0);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        cacheInvalidator.InvalidateTenant(workspace.TenantId);
        cacheInvalidator.InvalidateWorkspace(workspace.TenantId, workspace.Id);
        cacheInvalidator.InvalidateProcess();
        return (true, 200, null, null, count);
    }

    public async Task ApplyFilesAsync(Workspace workspace, UpdateFilesSettingsRequest? request, List<string> changes, CancellationToken ct)
    {
        if (request is null || !DatabaseOverridesEnabled)
        {
            return;
        }

        var ceiling = filesSettings.ReadCeiling();
        var row = await db.TenantFilesSettings.FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId, ct);
        if (row is null)
        {
            row = new TenantFilesSettings
            {
                TenantId = workspace.TenantId,
                MaxSizeBytes = ceiling.MaxSizeBytes,
                MaxAttachmentsPerMessage = ceiling.MaxAttachmentsPerMessage,
                PresignUploadTtlSeconds = ceiling.PresignUploadTtlSeconds,
                PresignDownloadTtlSeconds = ceiling.PresignDownloadTtlSeconds,
                AllowedContentTypes = ceiling.AllowedContentTypes.ToArray(),
                AudioMaxSizeBytes = ceiling.AudioMaxSizeBytes,
                AudioMaxDurationMs = ceiling.AudioMaxDurationMs,
                UpdatedAt = clock.UtcNow
            };
            db.TenantFilesSettings.Add(row);
            changes.Add("files.created");
        }

        if (request.MaxSizeBytes is { } maxSize)
        {
            var clamped = Math.Clamp(maxSize, 1, ceiling.MaxSizeBytes);
            if (row.MaxSizeBytes != clamped)
            {
                row.MaxSizeBytes = clamped;
                changes.Add("files.maxSizeBytes");
            }
        }

        if (request.MaxAttachmentsPerMessage is { } maxAtt)
        {
            var clamped = Math.Clamp(maxAtt, 1, ceiling.MaxAttachmentsPerMessage);
            if (row.MaxAttachmentsPerMessage != clamped)
            {
                row.MaxAttachmentsPerMessage = clamped;
                changes.Add("files.maxAttachmentsPerMessage");
            }
        }

        if (request.PresignUploadTtlSeconds is { } uploadTtl)
        {
            var clamped = Math.Clamp(uploadTtl, 60, ceiling.PresignUploadTtlSeconds);
            if (row.PresignUploadTtlSeconds != clamped)
            {
                row.PresignUploadTtlSeconds = clamped;
                changes.Add("files.presignUploadTtlSeconds");
            }
        }

        if (request.PresignDownloadTtlSeconds is { } downloadTtl)
        {
            var clamped = Math.Clamp(downloadTtl, 30, ceiling.PresignDownloadTtlSeconds);
            if (row.PresignDownloadTtlSeconds != clamped)
            {
                row.PresignDownloadTtlSeconds = clamped;
                changes.Add("files.presignDownloadTtlSeconds");
            }
        }

        if (request.AudioMaxSizeBytes is { } audioSize)
        {
            var clamped = Math.Clamp(audioSize, 1, ceiling.AudioMaxSizeBytes);
            if (row.AudioMaxSizeBytes != clamped)
            {
                row.AudioMaxSizeBytes = clamped;
                changes.Add("files.audioMaxSizeBytes");
            }
        }

        if (request.AudioMaxDurationMs is { } audioDur)
        {
            var clamped = Math.Clamp(audioDur, 1_000, ceiling.AudioMaxDurationMs);
            if (row.AudioMaxDurationMs != clamped)
            {
                row.AudioMaxDurationMs = clamped;
                changes.Add("files.audioMaxDurationMs");
            }
        }

        if (request.AllowedContentTypes is { } types)
        {
            var ceilingSet = ceiling.AllowedContentTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var filtered = types
                .Where(t => !string.IsNullOrWhiteSpace(t) && ceilingSet.Contains(t.Trim()))
                .Select(t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (filtered.Length == 0)
            {
                filtered = ceiling.AllowedContentTypes.ToArray();
            }

            if (!row.AllowedContentTypes.SequenceEqual(filtered, StringComparer.OrdinalIgnoreCase))
            {
                row.AllowedContentTypes = filtered;
                changes.Add("files.allowedContentTypes");
            }
        }

        if (changes.Any(c => c.StartsWith("files.", StringComparison.Ordinal)))
        {
            row.UpdatedAt = clock.UtcNow;
        }
    }

    public async Task ApplyRateLimitAsync(Workspace workspace, UpdateRateLimitSettingsRequest? request, List<string> changes, CancellationToken ct)
    {
        if (request is null || !DatabaseOverridesEnabled)
        {
            return;
        }

        var ceilingSend = RateLimitPolicies.MaxPerMinute;
        var ceilingHub = RateLimitPolicies.MaxPerMinute;

        var row = await db.TenantRateLimitSettings.FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId, ct);
        if (row is null)
        {
            row = new TenantRateLimitSettings
            {
                TenantId = workspace.TenantId,
                SendPerMinute = ceilingSend,
                HubPerMinute = ceilingHub,
                UpdatedAt = clock.UtcNow
            };
            db.TenantRateLimitSettings.Add(row);
            changes.Add("rateLimit.created");
        }

        if (request.SendPerMinute is { } send)
        {
            var clamped = Math.Clamp(Math.Min(send, ceilingSend), RateLimitPolicies.MinPerMinute, RateLimitPolicies.MaxPerMinute);
            if (row.SendPerMinute != clamped)
            {
                row.SendPerMinute = clamped;
                changes.Add("rateLimit.sendPerMinute");
            }
        }

        if (request.HubPerMinute is { } hub)
        {
            var clamped = Math.Clamp(Math.Min(hub, ceilingHub), RateLimitPolicies.MinPerMinute, RateLimitPolicies.MaxPerMinute);
            if (row.HubPerMinute != clamped)
            {
                row.HubPerMinute = clamped;
                changes.Add("rateLimit.hubPerMinute");
            }
        }

        if (changes.Any(c => c.StartsWith("rateLimit.", StringComparison.Ordinal)))
        {
            row.UpdatedAt = clock.UtcNow;
        }
    }

    public async Task<(bool Ok, int Status, string? Error, string? Message)> ApplyProcessAsync(
        UpdateSensitiveProcessRequest? request,
        List<string> changes,
        CancellationToken ct)
    {
        if (request is null || !DatabaseOverridesEnabled)
        {
            return (true, 200, null, null);
        }

        if (request.OpenRouterBaseUrl is { } rawBase && rawBase.Trim().Length > 0
            && !OpenRouterBaseUrlPolicies.IsValid(rawBase))
        {
            return (false, 400, "InvalidOpenRouterBaseUrl", "OpenRouter base URL must be https with a public host.");
        }

        var row = await EnsureProcessRowAsync(ct);

        if (request.AiProcessEnabled is { } aiEnabled && row.AiEnabled != aiEnabled)
        {
            row.AiEnabled = aiEnabled;
            changes.Add("ai.processEnabled");
        }

        if (request.EmailProcessEnabled is { } emailEnabled && row.EmailEnabled != emailEnabled)
        {
            row.EmailEnabled = emailEnabled;
            changes.Add("email.processEnabled");
        }

        if (request.RetentionProcessEnabled is { } retentionEnabled && row.MessageRetentionEnabled != retentionEnabled)
        {
            row.MessageRetentionEnabled = retentionEnabled;
            changes.Add("retention.processEnabled");
        }

        if (request.PushProcessEnabled is { } pushEnabled && row.PushEnabled != pushEnabled)
        {
            row.PushEnabled = pushEnabled;
            changes.Add("push.processEnabled");
        }

        if (request.LinkPreviewProcessEnabled is { } previewEnabled && row.LinkPreviewEnabled != previewEnabled)
        {
            row.LinkPreviewEnabled = previewEnabled;
            changes.Add("linkPreview.processEnabled");
        }

        if (request.OpenRouterBaseUrl is not null)
        {
            var normalized = request.OpenRouterBaseUrl.Trim().Length == 0
                ? ProcessSettingsDefaults.OpenRouterBaseUrl
                : OpenRouterBaseUrlPolicies.Normalize(request.OpenRouterBaseUrl);
            if (!string.Equals(row.OpenRouterBaseUrl, normalized, StringComparison.Ordinal))
            {
                row.OpenRouterBaseUrl = normalized;
                changes.Add("ai.openRouterBaseUrl");
            }
        }

        if (request.RetentionDefaultDays is { } days)
        {
            var clamped = Math.Clamp(days, MessageRetentionSettings.MinRetentionDays, MessageRetentionSettings.MaxRetentionDays);
            if (row.RetentionDefaultDays != clamped)
            {
                row.RetentionDefaultDays = clamped;
                changes.Add("retention.defaultRetentionDays");
            }
        }

        if (request.RetentionBatchSize is { } batch)
        {
            var clamped = Math.Clamp(batch, 1, 2000);
            if (row.RetentionBatchSize != clamped)
            {
                row.RetentionBatchSize = clamped;
                changes.Add("retention.batchSize");
            }
        }

        if (request.RetentionIntervalMinutes is { } interval)
        {
            var clamped = Math.Clamp(interval, 1, 24 * 60);
            if (row.RetentionIntervalMinutes != clamped)
            {
                row.RetentionIntervalMinutes = clamped;
                changes.Add("retention.intervalMinutes");
            }
        }

        if (request.LinkPreviewTimeoutMs is { } timeout)
        {
            var clamped = Math.Clamp(timeout, 500, 15_000);
            if (row.LinkPreviewTimeoutMs != clamped)
            {
                row.LinkPreviewTimeoutMs = clamped;
                changes.Add("linkPreview.timeoutMs");
            }
        }

        if (changes.Any(c => c.StartsWith("ai.", StringComparison.Ordinal)
            || c.StartsWith("email.process", StringComparison.Ordinal)
            || c.StartsWith("retention.", StringComparison.Ordinal)
            || c.StartsWith("push.", StringComparison.Ordinal)
            || c.StartsWith("linkPreview.process", StringComparison.Ordinal)
            || c.StartsWith("linkPreview.timeout", StringComparison.Ordinal)))
        {
            row.UpdatedAt = clock.UtcNow;
        }

        return (true, 200, null, null);
    }

    public async Task<CredentialRotateResult> RotateVapidAsync(
        Workspace workspace,
        UserId actorUserId,
        string publicKey,
        string privateKey,
        string? subject,
        CancellationToken ct)
    {
        if (!DatabaseOverridesEnabled)
        {
            return new CredentialRotateResult(false, 503, "RuntimeSettingsDisabled",
                "RuntimeSettings:DatabaseOverridesEnabled is false.", false, null, null, null);
        }

        if (!protector.IsEncryptionAvailable)
        {
            return new CredentialRotateResult(false, 503, "RuntimeSecretEncryptionUnavailable",
                "Encryption keyring is not configured.", false, null, null, null);
        }

        var trimmedPublic = publicKey.Trim();
        var trimmedPrivate = privateKey.Trim();
        if (trimmedPublic.Length is < 16 or > 256 || trimmedPrivate.Length is < 16 or > 256)
        {
            return new CredentialRotateResult(false, 400, "InvalidVapidKeys",
                "VAPID public and private keys are required.", false, null, null, null);
        }

        var trimmedSubject = string.IsNullOrWhiteSpace(subject)
            ? ProcessSettingsDefaults.VapidSubject
            : subject.Trim();
        if (trimmedSubject.Length > 256
            || !(trimmedSubject.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                || trimmedSubject.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            return new CredentialRotateResult(false, 400, "InvalidVapidSubject",
                "VAPID subject must be a mailto: or https:// URI.", false, null, null, null);
        }

        var now = clock.UtcNow;
        EncryptedSecretEnvelope envelope;
        try
        {
            envelope = protector.Protect(
                trimmedPrivate,
                RuntimeSecretKinds.VapidPrivateKey,
                ProcessSecretScope.TenantId,
                workspaceId: null,
                ProcessSecretScope.EntityId,
                now);
        }
        catch (CryptographicException)
        {
            return new CredentialRotateResult(false, 503, "RuntimeSecretEncryptionUnavailable",
                "Encryption keyring is not configured.", false, null, null, null);
        }

        var row = await EnsureProcessRowAsync(ct);
        row.VapidPublicKey = trimmedPublic;
        row.VapidPrivateKey.CopyFrom(envelope);
        row.VapidSubject = trimmedSubject;
        row.UpdatedAt = now;

        audit.Add(new AuditEvent
        {
            TenantId = workspace.TenantId,
            ActorUserId = actorUserId,
            Action = AuditActions.SettingsCredentialRotate,
            EntityType = "ProcessSettings",
            EntityId = ProcessSecretScope.EntityId,
            MetadataJson = JsonSerializer.Serialize(new
            {
                workspaceId = workspace.Id.Value,
                kind = RuntimeSecretKinds.VapidPrivateKey,
                keyVersion = envelope.KeyVersion
            })
        });
        await db.SaveChangesAsync(ct);
        cacheInvalidator.InvalidateProcess();

        return new CredentialRotateResult(
            true,
            200,
            null,
            null,
            true,
            SecretMasking.MaskFromSuffix(envelope.MaskSuffix),
            envelope.KeyVersion,
            envelope.RotatedAt);
    }

    private async Task<ProcessSettings> EnsureProcessRowAsync(CancellationToken ct)
    {
        var row = await db.ProcessSettings.FirstOrDefaultAsync(x => x.Id == ProcessSettings.SingletonId, ct);
        if (row is not null)
        {
            return row;
        }

        row = new ProcessSettings
        {
            Id = ProcessSettings.SingletonId,
            UpdatedAt = clock.UtcNow
        };
        db.ProcessSettings.Add(row);
        return row;
    }
}

public sealed record UpdateSensitiveProcessRequest(
    bool? AiProcessEnabled = null,
    bool? EmailProcessEnabled = null,
    bool? RetentionProcessEnabled = null,
    bool? PushProcessEnabled = null,
    bool? LinkPreviewProcessEnabled = null,
    string? OpenRouterBaseUrl = null,
    int? RetentionDefaultDays = null,
    int? RetentionBatchSize = null,
    int? RetentionIntervalMinutes = null,
    int? LinkPreviewTimeoutMs = null);

public sealed record UpdateFilesSettingsRequest(
    long? MaxSizeBytes = null,
    int? MaxAttachmentsPerMessage = null,
    int? PresignUploadTtlSeconds = null,
    int? PresignDownloadTtlSeconds = null,
    string[]? AllowedContentTypes = null,
    long? AudioMaxSizeBytes = null,
    int? AudioMaxDurationMs = null);

public sealed record UpdateRateLimitSettingsRequest(
    int? SendPerMinute = null,
    int? HubPerMinute = null);
