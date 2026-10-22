using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.Api;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Files;
using VibeChat.Infrastructure;
using VibeChat.Infrastructure.Compliance;
using VibeChat.Integrations;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using static VibeChat.Api.Endpoints.FilesEndpointHelpers;
using static VibeChat.Api.Endpoints.IdentityEndpointHelpers;

namespace VibeChat.Api.Endpoints;

internal static class PluginGovernedEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static void MapPluginGovernance(this RouteGroupBuilder v1)
    {
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/plugins/{installedId:guid}/history-grants", GrantHistory)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapDelete("/admin/workspaces/{workspaceId:guid}/plugins/{installedId:guid}/history-grants/{grantId:guid}", RevokeHistory)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/integrations/v1/channels/{channelId:guid}/interactive-messages", PostInteractive)
            .AllowAnonymous()
            .AllowPermissionGateExempt("integration token auth (B-111)");
        v1.MapGet("/integrations/v1/channels/{channelId:guid}/history", ReadHistory)
            .AllowAnonymous()
            .AllowPermissionGateExempt("integration token auth (B-111)");
        v1.MapPost("/integrations/v1/channels/{channelId:guid}/attachments", InitiateAttachment)
            .AllowAnonymous()
            .AllowPermissionGateExempt("integration token auth (B-111)");
        v1.MapPost("/integrations/v1/channels/{channelId:guid}/attachments/{attachmentId:guid}/complete", CompleteAttachment)
            .AllowAnonymous()
            .AllowPermissionGateExempt("integration token auth (B-111)");
        v1.MapPost("/workspaces/{workspaceId:guid}/channels/{channelId:guid}/messages/{messageId:guid}/plugin-actions", InvokeAction)
            .RequirePermission(Permissions.Message.Send);
    }

    private static async Task<IResult> GrantHistory(
        Guid workspaceId,
        Guid installedId,
        GrantPluginHistoryRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IOptions<BotIntegrationOptions> bots,
        IOptions<PluginPlatformOptions> platform,
        IClock clock,
        CancellationToken ct)
    {
        if (!bots.Value.Enabled)
        {
            return IntegrationBotEndpoints.Disabled();
        }

        if (!platform.Value.Enabled)
        {
            return Results.NotFound(new { error = PluginPlatformErrors.Disabled });
        }

        if (!PluginGovernedRules.TryValidateWindow(request.WindowStart, request.WindowEnd, out var windowError))
        {
            return Results.BadRequest(new { error = windowError });
        }

        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var plugin = await LoadPluginAsync(db, workspace.Id, installedId, ct);
        if (plugin is null)
        {
            return Results.NotFound(new { error = PluginManifestRules.NotFound });
        }

        if (!plugin.Capabilities.Contains(PluginManifestRules.CapabilityHistoryRead, StringComparer.Ordinal))
        {
            return Results.Json(new { error = PluginPlatformErrors.CapabilityDenied }, statusCode: StatusCodes.Status403Forbidden);
        }

        var channels = await IntegrationBotEndpoints.LoadGrantChannelsAsync(db, workspace.Id, [request.ChannelId], ct);
        if (channels is null || channels.Length != 1)
        {
            return Results.BadRequest(new { error = "InvalidChannelScope" });
        }

        var inScope = await db.IntegrationBotChannelScopes.AnyAsync(
            scope => scope.BotId == plugin.BotId && scope.ChannelId == channels[0].Id,
            ct);
        if (!inScope)
        {
            return Results.Json(new { error = PluginPlatformErrors.OutOfScope }, statusCode: StatusCodes.Status403Forbidden);
        }

        var now = clock.UtcNow;
        var active = await db.PluginHistoryGrants
            .Where(grant => grant.InstalledPluginId == plugin.Id && grant.ChannelId == channels[0].Id && grant.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var previous in active)
        {
            previous.RevokedAt = now;
            audit.Add(GrantAudit(workspace.TenantId, profile.Id, AuditActions.IntegrationPluginHistoryRevoke, plugin, previous.Id, now));
        }

        var grant = new PluginHistoryGrant
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            InstalledPluginId = plugin.Id,
            ChannelId = channels[0].Id,
            WindowStart = request.WindowStart,
            WindowEnd = request.WindowEnd,
            GrantedBy = profile.Id,
            GrantedAt = now
        };
        db.PluginHistoryGrants.Add(grant);
        audit.Add(GrantAudit(workspace.TenantId, profile.Id, AuditActions.IntegrationPluginHistoryGrant, plugin, grant.Id, now));
        await db.SaveChangesAsync(ct);
        return Results.Created(
            $"/api/v1/admin/workspaces/{workspace.Id.Value}/plugins/{plugin.Id}/history-grants/{grant.Id}",
            new PluginHistoryGrantResponse(grant.Id, grant.ChannelId.Value, grant.WindowStart, grant.WindowEnd, null));
    }

    private static async Task<IResult> RevokeHistory(
        Guid workspaceId,
        Guid installedId,
        Guid grantId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IOptions<BotIntegrationOptions> bots,
        IClock clock,
        CancellationToken ct)
    {
        if (!bots.Value.Enabled)
        {
            return IntegrationBotEndpoints.Disabled();
        }

        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var plugin = await LoadPluginAsync(db, workspace.Id, installedId, ct);
        if (plugin is null)
        {
            return Results.NotFound(new { error = PluginManifestRules.NotFound });
        }

        var grant = await db.PluginHistoryGrants.FirstOrDefaultAsync(
            item => item.Id == grantId && item.InstalledPluginId == plugin.Id,
            ct);
        if (grant is null)
        {
            return Results.NotFound(new { error = PluginPlatformErrors.HistoryGrantNotFound });
        }

        if (grant.RevokedAt is null)
        {
            var now = clock.UtcNow;
            grant.RevokedAt = now;
            audit.Add(GrantAudit(workspace.TenantId, profile.Id, AuditActions.IntegrationPluginHistoryRevoke, plugin, grant.Id, now));
            await db.SaveChangesAsync(ct);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> PostInteractive(
        Guid channelId,
        PostPluginInteractiveRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IMessageWriter messages,
        IAuditWriter audit,
        IOptions<BotIntegrationOptions> bots,
        IOptions<PluginPlatformOptions> platform,
        IClock clock,
        CancellationToken ct)
    {
        var gate = await OpenIntegrationAsync(http, db, tenant, bots, platform, clock, PluginManifestRules.CapabilityMessagesInteract, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var key = (request.IdempotencyKey ?? string.Empty).Trim();
        if (key.Length is < 1 or > 80)
        {
            return Results.BadRequest(new { error = "IdempotencyKeyRequired" });
        }

        var names = (request.Actions ?? []).Select(item => (item ?? string.Empty).Trim().ToLowerInvariant()).Where(item => item.Length > 0).Distinct().ToArray();
        if (!PluginManifestRules.TryParse(gate.Plugin!.ManifestJson, out var manifest, out _) || manifest is null)
        {
            return Results.Json(new { error = PluginPlatformErrors.CapabilityDenied }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (names.Length is 0 or > PluginGovernedRules.MaxActions
            || names.Any(name => manifest.Actions.All(action => action.Name != name)))
        {
            return Results.BadRequest(new { error = PluginPlatformErrors.ActionNotFound });
        }

        var channel = await RequireScopedChannelAsync(db, gate.Session!, channelId, ct);
        if (channel is null)
        {
            return Results.Json(new { error = "ChannelOutOfScope" }, statusCode: StatusCodes.Status403Forbidden);
        }

        var hash = Hash($"{request.Body}|{string.Join(',', names)}");
        var existing = await db.PluginInteractiveOffers.FirstOrDefaultAsync(
            offer => offer.InstalledPluginId == gate.Plugin.Id && offer.IdempotencyKey == key,
            ct);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
            {
                return Results.Conflict(new { error = "IdempotencyConflict" });
            }

            var message = await db.Messages.AsNoTracking().FirstAsync(item => item.Id == existing.MessageId, ct);
            return Results.Accepted(
                $"/api/v1/channels/{channel.Id.Value}/messages?after={Math.Max(0, message.Sequence - 1)}",
                new PluginInteractiveResponse(message.Id.Value, channel.Id.Value, message.Sequence, existing.ExpiresAt, true));
        }

        var ttl = request.ExpiresInSeconds ?? PluginGovernedRules.DefaultActionTtlSeconds;
        if (ttl is < PluginGovernedRules.MinActionTtlSeconds or > PluginGovernedRules.MaxActionTtlSeconds)
        {
            return Results.BadRequest(new { error = PluginPlatformErrors.ActionExpired });
        }

        if (!await TryConsumeAsync(db, gate.Plugin, PluginManifestRules.CapabilityMessagesInteract, clock.UtcNow, ct))
        {
            return Results.Json(new { error = PluginPlatformErrors.QuotaExceeded }, statusCode: StatusCodes.Status429TooManyRequests);
        }

        var qualified = names.Select(name => PluginManifestRules.QualifiedCommand(gate.Plugin.PluginId, name)).ToArray();
        var expiresAt = clock.UtcNow.AddSeconds(ttl);
        var messageId = MessageId.New();
        db.PluginInteractiveOffers.Add(new PluginInteractiveOffer
        {
            Id = Guid.NewGuid(),
            TenantId = gate.Plugin.TenantId,
            WorkspaceId = gate.Plugin.WorkspaceId,
            InstalledPluginId = gate.Plugin.Id,
            ChannelId = channel.Id,
            MessageId = messageId,
            PluginSlug = gate.Plugin.PluginId,
            PluginName = gate.Plugin.Name,
            Actions = qualified,
            ExpiresAt = expiresAt,
            IdempotencyKey = key,
            RequestHash = hash,
            CreatedAt = clock.UtcNow
        });
        try
        {
            var sent = await messages.SendAsync(
                new SendMessageCommand(
                    gate.Session!.Bot.TenantId,
                    gate.Session.Bot.UserId,
                    channel.Id,
                    messageId,
                    $"plugin-interact:{gate.Plugin.Id:N}:{key}",
                    (request.Body ?? string.Empty).Trim(),
                    null,
                    null,
                    null,
                    AuthorIsBot: true,
                    PluginCard: new MessagePluginCard(gate.Plugin.PluginId, gate.Plugin.Name, expiresAt, qualified)),
                ct);
            if (!sent.Idempotent)
            {
                gate.Plugin.LastUsedAt = clock.UtcNow;
                audit.Add(new AuditEvent
                {
                    TenantId = gate.Plugin.TenantId,
                    ActorUserId = gate.Session.Bot.UserId,
                    Action = AuditActions.IntegrationPluginInteractive,
                    EntityType = "InstalledPlugin",
                    EntityId = gate.Plugin.Id.ToString(),
                    MetadataJson = JsonSerializer.Serialize(new { channelId = channel.Id.Value, messageId = sent.MessageId.Value }),
                    OccurredAt = clock.UtcNow
                });
                await db.SaveChangesAsync(ct);
            }

            return Results.Accepted(
                $"/api/v1/channels/{channel.Id.Value}/messages?after={Math.Max(0, sent.Sequence - 1)}",
                new PluginInteractiveResponse(sent.MessageId.Value, channel.Id.Value, sent.Sequence, expiresAt, sent.Idempotent));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> ReadHistory(
        Guid channelId,
        DateTimeOffset from,
        DateTimeOffset to,
        string idempotencyKey,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IIdempotencyStore idempotency,
        IAuditWriter audit,
        IOptions<BotIntegrationOptions> bots,
        IOptions<PluginPlatformOptions> platform,
        IClock clock,
        CancellationToken ct)
    {
        var gate = await OpenIntegrationAsync(http, db, tenant, bots, platform, clock, PluginManifestRules.CapabilityHistoryRead, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var key = (idempotencyKey ?? string.Empty).Trim();
        if (key.Length is < 1 or > 80 || !PluginGovernedRules.WindowCovers(from, to, from, to))
        {
            return Results.BadRequest(new { error = key.Length is < 1 or > 80 ? "IdempotencyKeyRequired" : PluginGovernedRules.InvalidWindow });
        }

        var storeKey = $"plugin-history:{gate.Plugin!.Id:N}:{key}";
        var hash = Hash($"{channelId:N}|{from:O}|{to:O}");
        var replay = await idempotency.FindAsync(gate.Plugin.TenantId, storeKey, ct);
        if (replay is not null)
        {
            if (!string.Equals(replay.RequestHash, hash, StringComparison.Ordinal))
            {
                return Results.Conflict(new { error = "IdempotencyConflict" });
            }

            var cached = JsonSerializer.Deserialize<PluginHistoryResponse>(replay.ResultJson, Json);
            return cached is null
                ? Results.Conflict(new { error = "IdempotencyConflict" })
                : Results.Ok(cached);
        }

        var grant = await db.PluginHistoryGrants.AsNoTracking().FirstOrDefaultAsync(
            item => item.InstalledPluginId == gate.Plugin.Id
                && item.ChannelId == new ChannelId(channelId)
                && item.RevokedAt == null,
            ct);
        if (grant is null)
        {
            return Results.NotFound(new { error = PluginPlatformErrors.HistoryGrantNotFound });
        }

        if (!PluginGovernedRules.WindowCovers(grant.WindowStart, grant.WindowEnd, from, to))
        {
            return Results.Json(new { error = PluginPlatformErrors.HistoryWindowDenied }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (!await TryConsumeAsync(db, gate.Plugin, PluginManifestRules.CapabilityHistoryRead, clock.UtcNow, ct))
        {
            return Results.Json(new { error = PluginPlatformErrors.QuotaExceeded }, statusCode: StatusCodes.Status429TooManyRequests);
        }

        var botUserId = gate.Session!.Bot.UserId;
        var rows = await db.Messages.AsNoTracking()
            .Where(message => message.ConversationId == grant.ChannelId
                && message.DeletedAt == null
                && message.CreatedAt >= from
                && message.CreatedAt <= to)
            .OrderBy(message => message.Sequence)
            .Take(PluginGovernedRules.MaxHistoryMessages)
            .Select(message => new PluginHistoryItem(
                message.Id.Value,
                message.Sequence,
                message.Body,
                message.CreatedAt,
                message.AuthorId.Value,
                message.AuthorId == botUserId))
            .ToArrayAsync(ct);
        var body = new PluginHistoryResponse(rows);
        gate.Plugin.LastUsedAt = clock.UtcNow;
        audit.Add(new AuditEvent
        {
            TenantId = gate.Plugin.TenantId,
            ActorUserId = botUserId,
            Action = AuditActions.IntegrationPluginHistoryRead,
            EntityType = "PluginHistoryGrant",
            EntityId = grant.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new { channelId, count = rows.Length }),
            OccurredAt = clock.UtcNow
        });
        await idempotency.StoreAsync(new IdempotencyRecord(gate.Plugin.TenantId, storeKey, hash, JsonSerializer.Serialize(body, Json), clock.UtcNow), ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(body);
    }

    private static async Task<IResult> InitiateAttachment(
        Guid channelId,
        PluginAttachmentRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IObjectStorage storage,
        FilesSettingsResolver filesSettings,
        DelegatedAdminService quotas,
        IIdempotencyStore idempotency,
        IAuditWriter audit,
        IOptions<BotIntegrationOptions> bots,
        IOptions<PluginPlatformOptions> platform,
        IClock clock,
        CancellationToken ct)
    {
        var gate = await OpenIntegrationAsync(http, db, tenant, bots, platform, clock, PluginManifestRules.CapabilityAttachmentsSend, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var key = (request.IdempotencyKey ?? string.Empty).Trim();
        if (key.Length is < 1 or > 80)
        {
            return Results.BadRequest(new { error = "IdempotencyKeyRequired" });
        }

        var channel = await RequireScopedChannelAsync(db, gate.Session!, channelId, ct);
        if (channel is null)
        {
            return Results.Json(new { error = "ChannelOutOfScope" }, statusCode: StatusCodes.Status403Forbidden);
        }

        var storeKey = $"plugin-file:{gate.Plugin!.Id:N}:{key}";
        var hash = Hash($"{channel.Id.Value:N}|{request.FileName}|{request.ContentType}|{request.SizeBytes}");
        var replay = await idempotency.FindAsync(gate.Plugin.TenantId, storeKey, ct);
        if (replay is not null)
        {
            if (!string.Equals(replay.RequestHash, hash, StringComparison.Ordinal))
            {
                return Results.Conflict(new { error = "IdempotencyConflict" });
            }

            return Results.Content(replay.ResultJson, "application/json");
        }

        if (string.IsNullOrWhiteSpace(request.FileName)
            || string.IsNullOrWhiteSpace(request.ContentType)
            || request.SizeBytes <= 0
            || PluginGovernedRules.IsBlockedAttachmentName(request.FileName)
            || PluginGovernedRules.IsBlockedAttachmentType(request.ContentType))
        {
            return Results.BadRequest(new { error = PluginPlatformErrors.AttachmentRejected });
        }

        var files = await filesSettings.ResolveAsync(channel.TenantId, ct);
        if (!AttachmentPolicies.IsAllowedContentType(request.ContentType, files.AllowedContentTypes)
            || request.SizeBytes > files.MaxSizeBytes)
        {
            return Results.BadRequest(new { error = PluginPlatformErrors.AttachmentRejected });
        }

        if (!await TryConsumeAsync(db, gate.Plugin, PluginManifestRules.CapabilityAttachmentsSend, clock.UtcNow, ct))
        {
            return Results.Json(new { error = PluginPlatformErrors.QuotaExceeded }, statusCode: StatusCodes.Status429TooManyRequests);
        }

        var storageQuota = await quotas.TryConsumeAsync(
            channel.TenantId,
            channel.WorkspaceId,
            DelegatedResources.Storage,
            request.SizeBytes,
            ct);
        if (!storageQuota.Allowed)
        {
            return Results.Json(new { error = storageQuota.Error }, statusCode: StatusCodes.Status409Conflict);
        }

        var attachmentId = Guid.NewGuid();
        var safeName = AttachmentPolicies.SanitizeFileName(request.FileName);
        var now = clock.UtcNow;
        db.Attachments.Add(new Attachment
        {
            Id = attachmentId,
            TenantId = channel.TenantId,
            ChannelId = channel.Id,
            UploadedBy = gate.Session!.Bot.UserId,
            FileName = safeName,
            ContentType = request.ContentType.Trim(),
            SizeBytes = request.SizeBytes,
            StorageKey = AttachmentPolicies.BuildStorageKey(channel.TenantId, channel.Id, attachmentId, safeName),
            Status = AttachmentStatus.PendingUpload,
            ReferenceCount = 1,
            Kind = AttachmentKind.File,
            CreatedAt = now
        });
        gate.Plugin.LastUsedAt = now;
        audit.Add(new AuditEvent
        {
            TenantId = channel.TenantId,
            ActorUserId = gate.Session.Bot.UserId,
            Action = AuditActions.IntegrationPluginAttachment,
            EntityType = "Attachment",
            EntityId = attachmentId.ToString(),
            MetadataJson = JsonSerializer.Serialize(new { channelId = channel.Id.Value, contentType = request.ContentType.Trim(), request.SizeBytes }),
            OccurredAt = now
        });
        await db.SaveChangesAsync(ct);
        var upload = await storage.CreateUploadUrlAsync(
            AttachmentPolicies.BuildStorageKey(channel.TenantId, channel.Id, attachmentId, safeName),
            request.ContentType.Trim(),
            TimeSpan.FromSeconds(files.PresignUploadTtlSeconds),
            ct);
        var response = new AttachmentUploadResponse(
            attachmentId,
            upload.Url.ToString(),
            upload.ExpiresAt,
            upload.RequiredHeaders,
            files.MaxSizeBytes,
            safeName,
            request.ContentType.Trim());
        var json = JsonSerializer.Serialize(response, Json);
        await idempotency.StoreAsync(new IdempotencyRecord(gate.Plugin.TenantId, storeKey, hash, json, now), ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(response);
    }

    private static async Task<IResult> CompleteAttachment(
        Guid channelId,
        Guid attachmentId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IObjectStorage storage,
        FilesSettingsResolver filesSettings,
        IOutboxWriter outbox,
        IOptions<MalwareScanOptions> scanOptions,
        IMalwareScanner scanner,
        IOptions<BotIntegrationOptions> bots,
        IOptions<PluginPlatformOptions> platform,
        IAuditWriter audit,
        IClock clock,
        CancellationToken ct)
    {
        var gate = await OpenIntegrationAsync(http, db, tenant, bots, platform, clock, PluginManifestRules.CapabilityAttachmentsSend, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var channel = await RequireScopedChannelAsync(db, gate.Session!, channelId, ct);
        if (channel is null)
        {
            return Results.Json(new { error = "ChannelOutOfScope" }, statusCode: StatusCodes.Status403Forbidden);
        }

        var attachment = await db.Attachments.FirstOrDefaultAsync(item => item.Id == attachmentId && item.ChannelId == channel.Id, ct);
        if (attachment is null || attachment.UploadedBy != gate.Session!.Bot.UserId)
        {
            return Results.NotFound();
        }

        return await CompleteOwnedUploadAsync(channel, attachment, gate.Session.Bot.UserId, db, storage, filesSettings, outbox, scanOptions, scanner, audit, clock, ct);
    }

    private static async Task<IResult> InvokeAction(
        Guid workspaceId,
        Guid channelId,
        Guid messageId,
        InvokePluginActionRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPluginCallbackDispatcher callbacks,
        IAuditWriter audit,
        IOptions<BotIntegrationOptions> bots,
        IOptions<PluginPlatformOptions> platform,
        IClock clock,
        CancellationToken ct)
    {
        if (!bots.Value.Enabled)
        {
            return IntegrationBotEndpoints.Disabled();
        }

        if (!platform.Value.Enabled)
        {
            return Results.NotFound(new { error = PluginPlatformErrors.Disabled });
        }

        var key = (request.IdempotencyKey ?? string.Empty).Trim();
        var action = (request.Action ?? string.Empty).Trim().ToLowerInvariant();
        if (key.Length is < 1 or > 80 || !PluginManifestRules.TrySplitCommand(action, out var pluginSlug, out var actionName))
        {
            return Results.BadRequest(new { error = key.Length is < 1 or > 80 ? "IdempotencyKeyRequired" : PluginPlatformErrors.ActionNotFound });
        }

        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        var channel = await RequestAuth.ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
        if (channel is null || channel.WorkspaceId.Value != workspaceId)
        {
            return Results.Forbid();
        }

        var offer = await db.PluginInteractiveOffers.FirstOrDefaultAsync(
            item => item.MessageId == new MessageId(messageId) && item.ChannelId == channel.Id,
            ct);
        if (offer is null || !offer.Actions.Contains(action, StringComparer.Ordinal))
        {
            return Results.NotFound(new { error = PluginPlatformErrors.ActionNotFound });
        }

        if (offer.ExpiresAt <= clock.UtcNow)
        {
            return Results.Json(new { error = PluginPlatformErrors.ActionExpired }, statusCode: StatusCodes.Status409Conflict);
        }

        var plugin = await db.InstalledPlugins.FirstOrDefaultAsync(item => item.Id == offer.InstalledPluginId, ct);
        if (plugin is null || !plugin.Enabled || plugin.PluginId != pluginSlug)
        {
            return Results.Json(new { error = PluginPlatformErrors.PluginDisabled }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (!plugin.Capabilities.Contains(PluginManifestRules.CapabilityMessagesInteract, StringComparer.Ordinal)
            || !PluginManifestRules.TryParse(plugin.ManifestJson, out var manifest, out _)
            || manifest is null
            || manifest.Actions.All(item => item.Name != actionName))
        {
            return Results.Json(new { error = PluginPlatformErrors.CapabilityDenied }, statusCode: StatusCodes.Status403Forbidden);
        }

        var inScope = await db.IntegrationBotChannelScopes.AnyAsync(
            scope => scope.BotId == plugin.BotId && scope.ChannelId == channel.Id,
            ct);
        if (!inScope)
        {
            return Results.Json(new { error = PluginPlatformErrors.OutOfScope }, statusCode: StatusCodes.Status403Forbidden);
        }

        var hash = Hash(action);
        var receipt = await db.PluginActionReceipts.FirstOrDefaultAsync(
            item => item.InstalledPluginId == plugin.Id && item.IdempotencyKey == key,
            ct);
        if (receipt is not null && !string.Equals(receipt.RequestHash, hash, StringComparison.Ordinal))
        {
            return Results.Conflict(new { error = "IdempotencyConflict" });
        }

        if (receipt?.Status == PluginActionStatus.Accepted)
        {
            return Results.Json(new PluginActionResponse(action, receipt.Status, true), statusCode: StatusCodes.Status202Accepted);
        }

        if (receipt is null)
        {
            if (!await TryConsumeAsync(db, plugin, PluginManifestRules.CapabilityMessagesInteract, clock.UtcNow, ct))
            {
                return Results.Json(new { error = PluginPlatformErrors.QuotaExceeded }, statusCode: StatusCodes.Status429TooManyRequests);
            }

            receipt = new PluginActionReceipt
            {
                Id = Guid.NewGuid(),
                TenantId = plugin.TenantId,
                InstalledPluginId = plugin.Id,
                IdempotencyKey = key,
                RequestHash = hash,
                Action = action,
                MessageId = offer.MessageId,
                ChannelId = channel.Id,
                ActorUserId = profile.Id,
                Status = PluginActionStatus.Pending,
                CreatedAt = clock.UtcNow
            };
            db.PluginActionReceipts.Add(receipt);
            await db.SaveChangesAsync(ct);
        }

        var delivered = true;
        if (!PluginManifestRules.IsProductAction(manifest, actionName))
        {
            var payload = JsonSerializer.Serialize(new
            {
                type = PluginCallbackDispatcher.ActionEvent,
                pluginId = plugin.PluginId,
                action,
                channelId = channel.Id.Value,
                messageId,
                actorUserId = profile.Id.Value,
                idempotencyKey = key,
                deliveryId = receipt.Id
            }, Json);
            delivered = await callbacks.TryDispatchActionAsync(plugin, payload, receipt.Id, ct);
        }

        receipt.Status = delivered ? PluginActionStatus.Accepted : PluginActionStatus.Failed;
        if (delivered)
        {
            plugin.LastUsedAt = clock.UtcNow;
            audit.Add(new AuditEvent
            {
                TenantId = plugin.TenantId,
                ActorUserId = profile.Id,
                Action = AuditActions.IntegrationPluginAction,
                EntityType = "PluginActionReceipt",
                EntityId = receipt.Id.ToString(),
                MetadataJson = JsonSerializer.Serialize(new { pluginId = plugin.PluginId, action, channelId, messageId }),
                OccurredAt = clock.UtcNow
            });
        }

        await db.SaveChangesAsync(ct);
        return delivered
            ? Results.Json(new PluginActionResponse(action, receipt.Status, false), statusCode: StatusCodes.Status202Accepted)
            : Results.Json(new { error = PluginPlatformErrors.CallbackFailed }, statusCode: StatusCodes.Status502BadGateway);
    }

    private static async Task<IntegrationGate> OpenIntegrationAsync(
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<BotIntegrationOptions> bots,
        IOptions<PluginPlatformOptions> platform,
        IClock clock,
        string capability,
        CancellationToken ct)
    {
        if (!bots.Value.Enabled)
        {
            return new IntegrationGate(null, null, IntegrationBotEndpoints.Disabled());
        }

        if (!platform.Value.Enabled)
        {
            return new IntegrationGate(null, null, Results.NotFound(new { error = PluginPlatformErrors.Disabled }));
        }

        var session = await IntegrationBotEndpoints.AuthenticateAsync(http, db, tenant, clock, ct);
        if (session is null)
        {
            return new IntegrationGate(null, null, Results.Unauthorized());
        }

        if (!session.Bot.Enabled)
        {
            return new IntegrationGate(null, null, Results.Json(new { error = "BotDisabled" }, statusCode: StatusCodes.Status403Forbidden));
        }

        var plugin = await db.InstalledPlugins.FirstOrDefaultAsync(item => item.BotId == session.Bot.Id, ct);
        if (plugin is null || !plugin.Enabled)
        {
            return new IntegrationGate(null, null, Results.Json(new { error = PluginPlatformErrors.PluginDisabled }, statusCode: StatusCodes.Status403Forbidden));
        }

        if (!plugin.Capabilities.Contains(capability, StringComparer.Ordinal))
        {
            return new IntegrationGate(null, null, Results.Json(new { error = PluginPlatformErrors.CapabilityDenied }, statusCode: StatusCodes.Status403Forbidden));
        }

        return new IntegrationGate(session, plugin, null);
    }

    private static async Task<Channel?> RequireScopedChannelAsync(
        VibeChatDbContext db,
        IntegrationBotEndpoints.BotSession session,
        Guid channelId,
        CancellationToken ct)
    {
        var channel = await db.Channels.FirstOrDefaultAsync(
            item => item.Id == new ChannelId(channelId) && item.TenantId == session.Bot.TenantId,
            ct);
        if (channel is null || channel.Type is ChannelType.Direct or ChannelType.GroupDm)
        {
            return null;
        }

        var inScope = await db.IntegrationBotChannelScopes.AnyAsync(
            scope => scope.BotId == session.Bot.Id && scope.ChannelId == channel.Id,
            ct);
        return inScope ? channel : null;
    }

    private static async Task<bool> TryConsumeAsync(
        VibeChatDbContext db,
        InstalledPlugin plugin,
        string capability,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var limit = PluginGovernedRules.DailyLimit(capability);
        if (limit <= 0)
        {
            return false;
        }

        var day = DateOnly.FromDateTime(now.UtcDateTime);
        var row = await db.PluginQuotaCounters.FirstOrDefaultAsync(
            counter => counter.InstalledPluginId == plugin.Id && counter.Capability == capability && counter.Day == day,
            ct);
        if (row is null)
        {
            db.PluginQuotaCounters.Add(new PluginQuotaCounter
            {
                Id = Guid.NewGuid(),
                TenantId = plugin.TenantId,
                InstalledPluginId = plugin.Id,
                Capability = capability,
                Day = day,
                Count = 1
            });
            return true;
        }

        if (row.Count >= limit)
        {
            return false;
        }

        row.Count += 1;
        return true;
    }

    private static Task<InstalledPlugin?> LoadPluginAsync(VibeChatDbContext db, WorkspaceId workspaceId, Guid installedId, CancellationToken ct) =>
        db.InstalledPlugins.FirstOrDefaultAsync(plugin => plugin.Id == installedId && plugin.WorkspaceId == workspaceId, ct);

    private static AuditEvent GrantAudit(TenantId tenantId, UserId actorId, string action, InstalledPlugin plugin, Guid grantId, DateTimeOffset now) =>
        new()
        {
            TenantId = tenantId,
            ActorUserId = actorId,
            Action = action,
            EntityType = "PluginHistoryGrant",
            EntityId = grantId.ToString(),
            MetadataJson = JsonSerializer.Serialize(new { pluginId = plugin.PluginId }),
            OccurredAt = now
        };

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record IntegrationGate(IntegrationBotEndpoints.BotSession? Session, InstalledPlugin? Plugin, IResult? Error);
}
