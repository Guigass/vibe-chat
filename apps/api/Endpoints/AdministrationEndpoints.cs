using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using VibeChat.Administration;
using VibeChat.AI;
using VibeChat.Api;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.Messaging;
using VibeChat.Notifications;
using VibeChat.Realtime;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using static VibeChat.Api.Endpoints.AdministrationEndpointHelpers;
using static VibeChat.Api.Endpoints.ConversationsEndpointHelpers;
using static VibeChat.Api.Endpoints.IdentityEndpointHelpers;
using static VibeChat.Api.Endpoints.MessagingEndpointHelpers;

namespace VibeChat.Api.Endpoints;

internal static class AdministrationEndpoints
{
    internal static void MapAdministration(this RouteGroupBuilder v1)
    {
        v1.MapGet("/admin/dashboard", async (HttpContext http, VibeChatDbContext db, ITenantContext tenant, IDashboardQuery dashboard, IPresenceService presence, HealthCheckService health, IConfiguration config, IClock clock, IPermissionChecker permissions, CancellationToken ct) =>
        {
            var access = await ResolveAdminDashboardAccessAsync(http, db, tenant, permissions, clock, ct);
            if (access is null)
            {
                return Results.Forbid();
            }

            var (_, membershipTenant) = access.Value;
            var stats = await dashboard.GetStatsAsync(ct);
            var online = await presence.CountOnlineAsync(membershipTenant, ct);
            var failures = await db.OutboxMessages.IgnoreQueryFilters().CountAsync(x => x.ProcessedAt == null && x.Attempts > 0, ct);
            var report = await health.CheckHealthAsync(ct);
            string MapHealth(string name) => report.Entries.TryGetValue(name, out var entry)
                ? entry.Status switch
                {
                    HealthStatus.Healthy => "up",
                    HealthStatus.Degraded => "degraded",
                    _ => "down"
                }
                : "down";

            return Results.Ok(new AdminDashboardResponse(
                stats.UserCount,
                online,
                stats.WorkspaceCount,
                stats.ChannelCount,
                stats.MessageCount,
                (int)Math.Max(0, VibeChatMetrics.RealtimeConnectionsGauge),
                stats.PendingOutboxCount,
                failures,
                new AdminHealthResponse(MapHealth("postgres"), MapHealth("redis"), MapHealth("minio")),
                typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.1.0",
                config["Observability:GrafanaUrl"] ?? "http://localhost:3000"));
        }).RequirePermission(Permissions.Admin.Dashboard);

        v1.MapGet("/admin/audit-events", async (
            int? limit,
            string? action,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IPermissionChecker permissions,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            await BeginRlsUserAsync(db, tenant, profile.Id, ct);
            var membership = await db.WorkspaceMembers.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.UserId == profile.Id)
                .OrderBy(x => x.JoinedAt)
                .FirstOrDefaultAsync(ct);
            if (membership is null
                || !await permissions.HasPermissionAsync(membership.TenantId, profile.Id, Permissions.Admin.Dashboard, ct))
            {
                return Results.Forbid();
            }

            tenant.SetTenant(membership.TenantId);
            await RlsSession.EnsureAppliedAsync(db, tenant, ct);
            var take = Math.Clamp(limit ?? 50, 1, 200);
            var query = db.AuditEvents.AsNoTracking().Where(x => x.TenantId == membership.TenantId);
            if (!string.IsNullOrWhiteSpace(action))
            {
                query = query.Where(x => x.Action == action);
            }

            var rows = await query
                .OrderByDescending(x => x.OccurredAt)
                .Take(take)
                .ToListAsync(ct);

            var items = rows.Select(x => new AuditEventResponse(
                x.Id,
                x.Action,
                x.EntityType,
                x.EntityId,
                x.ActorUserId?.Value,
                x.OccurredAt,
                x.MetadataJson)).ToArray();

            return Results.Ok(new AuditEventsResponse(items));
        }).RequirePermission(Permissions.Admin.Dashboard);

        // B-067: conversation audit viewer — admin.dashboard; bypass channel membership within tenant.
        v1.MapGet("/admin/conversations", async (
            Guid? workspaceId,
            int? limit,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IPermissionChecker permissions,
            IClock clock,
            CancellationToken ct) =>
        {
            var access = await ResolveAdminDashboardAccessAsync(http, db, tenant, permissions, clock, ct);
            if (access is null)
            {
                return Results.Forbid();
            }

            var (profile, tenantId) = access.Value;
            var take = Math.Clamp(limit ?? 100, 1, 200);
            var query = db.Channels.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenantId);
            if (workspaceId is { } wsId && wsId != Guid.Empty)
            {
                var workspaceKey = new WorkspaceId(wsId);
                var workspaceOk = await db.Workspaces.IgnoreQueryFilters()
                    .AnyAsync(x => x.Id == workspaceKey && x.TenantId == tenantId, ct);
                if (!workspaceOk)
                {
                    return Results.Forbid();
                }

                query = query.Where(x => x.WorkspaceId == workspaceKey);
            }

            var channels = await query
                .OrderBy(x => x.Type == ChannelType.Direct || x.Type == ChannelType.GroupDm ? 1 : 0)
                .ThenBy(x => x.Name)
                .Take(take)
                .ToListAsync(ct);

            var peerByChannel = await ResolveDirectPeersAsync(channels, profile.Id, db, ct);
            var groupByChannel = await GroupDmEndpoints.ResolveInfosAsync(channels, profile.Id, db, ct);
            var items = channels.Select(x =>
            {
                peerByChannel.TryGetValue(x.Id, out var peer);
                groupByChannel.TryGetValue(x.Id, out var group);
                var displayName = x.Type == ChannelType.Direct && peer is not null
                    ? peer.DisplayName
                    : group.Names is { Length: > 0 }
                        ? group.DisplayName
                        : x.Name;
                return new AdminConversationResponse(
                    x.Id.Value,
                    x.WorkspaceId.Value,
                    displayName,
                    x.Type.ToString(),
                    x.SpaceId,
                    peer?.UserId.Value,
                    peer?.DisplayName);
            }).ToArray();

            return Results.Ok(new AdminConversationsResponse(items));
        }).RequirePermission(Permissions.Admin.Dashboard);

        v1.MapGet("/admin/conversations/{channelId:guid}/messages", async (
            Guid channelId,
            long? after,
            int? limit,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IPermissionChecker permissions,
            IClock clock,
            CancellationToken ct) =>
        {
            var access = await ResolveAdminDashboardAccessAsync(http, db, tenant, permissions, clock, ct);
            if (access is null)
            {
                return Results.Forbid();
            }

            var (profile, tenantId) = access.Value;
            var channelKey = new ChannelId(channelId);
            var channel = await db.Channels.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == channelKey && x.TenantId == tenantId, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var take = Math.Clamp(limit ?? 50, 1, 200);
            var rows = await (
                from m in db.Messages.IgnoreQueryFilters().AsNoTracking()
                where m.TenantId == tenantId && m.ConversationId == channel.Id && m.Sequence > (after ?? 0)
                join u in db.UserProfiles.IgnoreQueryFilters().AsNoTracking() on m.AuthorId equals u.Id into authors
                from u in authors.DefaultIfEmpty()
                join d in db.UserProfiles.IgnoreQueryFilters().AsNoTracking() on m.DeletedBy equals d.Id into deleters
                from d in deleters.DefaultIfEmpty()
                orderby m.Sequence
                select new
                {
                    m.Id,
                    ChannelId = channel.Id,
                    ConversationId = m.ConversationId,
                    m.Sequence,
                    m.AuthorId,
                    m.Body,
                    m.CreatedAt,
                    m.EditedAt,
                    m.DeletedAt,
                    m.DeletedBy,
                    m.ThreadId,
                    m.ReplyToMessageId,
                    AuthorName = u != null ? u.DisplayName : m.AuthorId.Value.ToString(),
                    DeletedByName = d != null ? d.DisplayName : null
                })
                .Take(take)
                .ToArrayAsync(ct);

            var messageIds = rows.Select(x => x.Id).ToArray();
            var threadIds = rows.Where(x => x.ThreadId is not null).Select(x => x.ThreadId!.Value).Distinct().ToArray();
            var replyCounts = threadIds.Length == 0
                ? new Dictionary<Guid, int>()
                : await db.Messages.IgnoreQueryFilters().AsNoTracking()
                    .Where(m => m.TenantId == tenantId
                        && m.ThreadId != null
                        && threadIds.Contains(m.ThreadId.Value)
                        && m.ConversationId != channel.Id)
                    .GroupBy(m => m.ThreadId!.Value)
                    .Select(g => new { ThreadId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.ThreadId, x => x.Count, ct);

            var attachmentsByMessage = await LoadAttachmentsByMessageAsync(db, channel.Id, messageIds, ct);
            var pollsByMessage = await PollQuery.LoadByMessageIdsAsync(db, messageIds, profile.Id, canVote: false, includeVoters: true, ct);
            var items = rows.Select(x => new AdminConversationMessageResponse(
                x.Id.Value,
                x.ChannelId.Value,
                x.ConversationId.Value,
                x.Sequence,
                x.AuthorId.Value,
                x.AuthorName,
                x.Body,
                x.CreatedAt,
                x.EditedAt,
                x.DeletedAt,
                x.DeletedBy?.Value,
                x.DeletedByName,
                x.ThreadId,
                x.ReplyToMessageId?.Value,
                x.ThreadId is Guid tid && replyCounts.TryGetValue(tid, out var count) ? count : 0,
                attachmentsByMessage.TryGetValue(x.Id.Value, out var atts) ? atts : [],
                pollsByMessage.TryGetValue(x.Id.Value, out var poll) ? poll : null)).ToArray();

            return Results.Ok(new AdminConversationMessagesResponse(items));
        }).RequirePermission(Permissions.Admin.Dashboard);

        v1.MapGet("/admin/threads/{threadId:guid}/messages", async (
            Guid threadId,
            long? after,
            int? limit,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IPermissionChecker permissions,
            IClock clock,
            CancellationToken ct) =>
        {
            var access = await ResolveAdminDashboardAccessAsync(http, db, tenant, permissions, clock, ct);
            if (access is null)
            {
                return Results.Forbid();
            }

            var (_, tenantId) = access.Value;
            var thread = await db.MessageThreads.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == threadId && x.TenantId == tenantId, ct);
            if (thread is null)
            {
                return Results.Forbid();
            }

            var conversationId = new ChannelId(thread.Id);
            var take = Math.Clamp(limit ?? 50, 1, 200);
            var rows = await (
                from m in db.Messages.IgnoreQueryFilters().AsNoTracking()
                where m.TenantId == tenantId && m.ConversationId == conversationId && m.Sequence > (after ?? 0)
                join u in db.UserProfiles.IgnoreQueryFilters().AsNoTracking() on m.AuthorId equals u.Id into authors
                from u in authors.DefaultIfEmpty()
                join d in db.UserProfiles.IgnoreQueryFilters().AsNoTracking() on m.DeletedBy equals d.Id into deleters
                from d in deleters.DefaultIfEmpty()
                orderby m.Sequence
                select new
                {
                    m.Id,
                    ChannelId = thread.ChannelId,
                    ConversationId = m.ConversationId,
                    m.Sequence,
                    m.AuthorId,
                    m.Body,
                    m.CreatedAt,
                    m.EditedAt,
                    m.DeletedAt,
                    m.DeletedBy,
                    m.ThreadId,
                    m.ReplyToMessageId,
                    AuthorName = u != null ? u.DisplayName : m.AuthorId.Value.ToString(),
                    DeletedByName = d != null ? d.DisplayName : null
                })
                .Take(take)
                .ToArrayAsync(ct);

            var messageIds = rows.Select(x => x.Id).ToArray();
            var attachmentsByMessage = await LoadAttachmentsByMessageAsync(db, thread.ChannelId, messageIds, ct);
            var items = rows.Select(x => new AdminConversationMessageResponse(
                x.Id.Value,
                x.ChannelId.Value,
                x.ConversationId.Value,
                x.Sequence,
                x.AuthorId.Value,
                x.AuthorName,
                x.Body,
                x.CreatedAt,
                x.EditedAt,
                x.DeletedAt,
                x.DeletedBy?.Value,
                x.DeletedByName,
                x.ThreadId ?? thread.Id,
                x.ReplyToMessageId?.Value,
                0,
                attachmentsByMessage.TryGetValue(x.Id.Value, out var atts) ? atts : [])).ToArray();

            return Results.Ok(new AdminConversationMessagesResponse(items));
        }).RequirePermission(Permissions.Admin.Dashboard);

        // B-069 / ADR-020: sensitive integration settings — admin-only, secrets always masked.
        v1.MapGet("/admin/settings", async (
            Guid? workspaceId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IPermissionChecker permissions,
            RuntimeSettingsAdminService settingsAdmin,
            IClock clock,
            CancellationToken ct) =>
        {
            var access = await ResolveSensitiveSettingsAccessAsync(http, db, tenant, permissions, workspaceId, clock, ct);
            if (access is null)
            {
                return Results.Forbid();
            }

            var (_, workspace) = access.Value;
            return Results.Ok(await settingsAdmin.BuildResponseAsync(workspace, ct));
        }).RequirePermission(Permissions.Workspace.Admin);

        v1.MapPut("/admin/settings", async (
            UpdateSensitiveSettingsRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IPermissionChecker permissions,
            IConfiguration config,
            IAuditWriter audit,
            EmailSettingsResolver emailSettings,
            RuntimeSettingsAdminService settingsAdmin,
            IRuntimeSettingsCacheInvalidator cacheInvalidator,
            IClock clock,
            CancellationToken ct) =>
        {
            var access = await ResolveSensitiveSettingsAccessAsync(
                http, db, tenant, permissions, request.WorkspaceId, clock, ct);
            if (access is null)
            {
                return Results.Forbid();
            }

            var (profile, workspace) = access.Value;

            // ADR-020: secrets never via general PUT — use dedicated rotate endpoints.
            if (request.Ai?.ApiKey is not null
                || request.Email?.SmtpPassword is not null
                || request.Webhooks?.Secret is not null
                || request.Push?.VapidPrivateKey is not null)
            {
                return Results.BadRequest(new
                {
                    error = "SecretsNotWritable",
                    message = "Use POST /admin/settings/credentials/{openrouter|smtp|webhook|vapid}/rotate to rotate secrets."
                });
            }

            var changes = new List<string>();

            if (request.Ai is not null)
            {
                var aiSettings = await db.AiSettings
                    .FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId && x.WorkspaceId == workspace.Id, ct);
                if (aiSettings is null)
                {
                    aiSettings = new AiSettings
                    {
                        WorkspaceId = workspace.Id,
                        TenantId = workspace.TenantId,
                        Enabled = false,
                        Provider = "Mock"
                    };
                    db.AiSettings.Add(aiSettings);
                    changes.Add("ai.created");
                }

                if (request.Ai.WorkspaceEnabled is { } workspaceEnabled && aiSettings.Enabled != workspaceEnabled)
                {
                    aiSettings.Enabled = workspaceEnabled;
                    changes.Add("ai.workspaceEnabled");
                }

                if (!string.IsNullOrWhiteSpace(request.Ai.Provider))
                {
                    var provider = request.Ai.Provider.Trim();
                    if (!string.Equals(provider, "Mock", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(provider, "OpenRouter", StringComparison.OrdinalIgnoreCase))
                    {
                        return Results.BadRequest(new { error = "InvalidAiProvider", message = "Provider must be Mock or OpenRouter." });
                    }

                    var normalized = string.Equals(provider, "OpenRouter", StringComparison.OrdinalIgnoreCase) ? "OpenRouter" : "Mock";
                    if (!string.Equals(aiSettings.Provider, normalized, StringComparison.Ordinal))
                    {
                        aiSettings.Provider = normalized;
                        changes.Add("ai.provider");
                    }
                }
            }

            if (request.Email is not null)
            {
                var emailRow = await db.TenantEmailSettings
                    .FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId, ct);
                var created = false;
                if (emailRow is null)
                {
                    var baseline = await emailSettings.ResolveAsync(workspace.TenantId, ct);
                    emailRow = new TenantEmailSettings
                    {
                        TenantId = workspace.TenantId,
                        Enabled = baseline.Enabled,
                        Host = baseline.Host,
                        Port = baseline.Port,
                        Username = baseline.Username,
                        From = baseline.From,
                        UseStartTls = baseline.UseStartTls,
                        UpdatedAt = clock.UtcNow
                    };
                    db.TenantEmailSettings.Add(emailRow);
                    created = true;
                    changes.Add("email.created");
                }

                if (request.Email.Enabled is { } emailEnabled && emailRow.Enabled != emailEnabled)
                {
                    emailRow.Enabled = emailEnabled;
                    changes.Add("email.enabled");
                }

                if (request.Email.SmtpHost is not null)
                {
                    var host = request.Email.SmtpHost.Trim();
                    if (host.Length > 256)
                    {
                        return Results.BadRequest(new { error = "InvalidSmtpHost" });
                    }

                    if (!string.Equals(emailRow.Host, host, StringComparison.Ordinal))
                    {
                        emailRow.Host = host;
                        changes.Add("email.smtpHost");
                    }
                }

                if (request.Email.SmtpPort is { } port)
                {
                    if (port is < 1 or > 65535)
                    {
                        return Results.BadRequest(new { error = "InvalidSmtpPort" });
                    }

                    if (emailRow.Port != port)
                    {
                        emailRow.Port = port;
                        changes.Add("email.smtpPort");
                    }
                }

                if (request.Email.SmtpUsername is not null)
                {
                    var username = request.Email.SmtpUsername.Trim();
                    if (username.Length > 256)
                    {
                        return Results.BadRequest(new { error = "InvalidSmtpUsername" });
                    }

                    if (!string.Equals(emailRow.Username, username, StringComparison.Ordinal))
                    {
                        emailRow.Username = username;
                        changes.Add("email.smtpUsername");
                    }
                }

                if (request.Email.SmtpFrom is not null)
                {
                    var from = request.Email.SmtpFrom.Trim();
                    if (from.Length > 320)
                    {
                        return Results.BadRequest(new { error = "InvalidSmtpFrom" });
                    }

                    if (!string.Equals(emailRow.From, from, StringComparison.Ordinal))
                    {
                        emailRow.From = from;
                        changes.Add("email.smtpFrom");
                    }
                }

                if (request.Email.UseStartTls is { } tls && emailRow.UseStartTls != tls)
                {
                    emailRow.UseStartTls = tls;
                    changes.Add("email.useStartTls");
                }

                if (created || changes.Any(c => c.StartsWith("email.", StringComparison.Ordinal)))
                {
                    emailRow.UpdatedAt = clock.UtcNow;
                }
            }

            if (request.Webhooks is not null)
            {
                var webhookRow = await db.OutboundWebhookEndpoints
                    .FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId, ct);
                var created = false;
                if (webhookRow is null)
                {
                    webhookRow = new OutboundWebhookEndpoint
                    {
                        TenantId = workspace.TenantId,
                        Enabled = false,
                        Url = string.Empty,
                        Secret = null,
                        UpdatedAt = clock.UtcNow
                    };
                    db.OutboundWebhookEndpoints.Add(webhookRow);
                    created = true;
                    changes.Add("webhooks.created");
                }

                if (request.Webhooks.Url is not null)
                {
                    var url = request.Webhooks.Url.Trim();
                    if (url.Length > 2048)
                    {
                        return Results.BadRequest(new { error = "InvalidWebhookUrl", message = "URL exceeds 2048 characters." });
                    }

                    if (url.Length > 0 && !WebhookDelivery.IsValidHttpsUrl(url))
                    {
                        return Results.BadRequest(new
                        {
                            error = "InvalidWebhookUrl",
                            message = "URL must be https (or http://localhost for lab)."
                        });
                    }

                    if (!string.Equals(webhookRow.Url, url, StringComparison.Ordinal))
                    {
                        webhookRow.Url = url;
                        changes.Add("webhooks.url");
                    }
                }

                if (request.Webhooks.Enabled is { } webhookEnabled && webhookRow.Enabled != webhookEnabled)
                {
                    var secretConfigured = webhookRow.SigningSecret.IsPresent
                        || SecretMasking.IsConfigured(webhookRow.Secret);
                    if (webhookEnabled
                        && (!SecretMasking.IsConfigured(webhookRow.Url) || !secretConfigured))
                    {
                        return Results.BadRequest(new
                        {
                            error = "WebhookIncomplete",
                            message = "Enable requires a valid URL and signing secret (rotate via credentials/webhook/rotate)."
                        });
                    }

                    webhookRow.Enabled = webhookEnabled;
                    changes.Add("webhooks.enabled");
                }

                if (created || changes.Any(c => c.StartsWith("webhooks.", StringComparison.Ordinal)))
                {
                    webhookRow.UpdatedAt = clock.UtcNow;
                }
            }

            if (request.Retention is not null)
            {
                var retentionRow = await db.MessageRetentionSettings
                    .FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId, ct);
                var created = false;
                if (retentionRow is null)
                {
                    var defaultDays = config.GetValue(
                        "MessageRetention:DefaultRetentionDays",
                        MessageRetentionSettings.DefaultRetentionDays);
                    if (defaultDays is < MessageRetentionSettings.MinRetentionDays or > MessageRetentionSettings.MaxRetentionDays)
                    {
                        defaultDays = MessageRetentionSettings.DefaultRetentionDays;
                    }

                    retentionRow = new MessageRetentionSettings
                    {
                        TenantId = workspace.TenantId,
                        Enabled = false,
                        RetentionDays = defaultDays,
                        UpdatedAt = clock.UtcNow
                    };
                    db.MessageRetentionSettings.Add(retentionRow);
                    created = true;
                    changes.Add("retention.created");
                }

                if (request.Retention.RetentionDays is { } days)
                {
                    if (days is < MessageRetentionSettings.MinRetentionDays or > MessageRetentionSettings.MaxRetentionDays)
                    {
                        return Results.BadRequest(new
                        {
                            error = "InvalidRetentionDays",
                            message = $"RetentionDays must be between {MessageRetentionSettings.MinRetentionDays} and {MessageRetentionSettings.MaxRetentionDays}."
                        });
                    }

                    if (retentionRow.RetentionDays != days)
                    {
                        retentionRow.RetentionDays = days;
                        changes.Add("retention.retentionDays");
                    }
                }

                if (request.Retention.Enabled is { } retentionEnabled && retentionRow.Enabled != retentionEnabled)
                {
                    retentionRow.Enabled = retentionEnabled;
                    changes.Add("retention.enabled");
                }

                if (created || changes.Any(c => c.StartsWith("retention.", StringComparison.Ordinal)))
                {
                    retentionRow.UpdatedAt = clock.UtcNow;
                }
            }

            if (request.LinkPreview is not null)
            {
                var linkPreviewRow = await db.TenantLinkPreviewSettings
                    .FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId, ct);
                var created = false;
                if (linkPreviewRow is null)
                {
                    linkPreviewRow = new TenantLinkPreviewSettings
                    {
                        TenantId = workspace.TenantId,
                        Enabled = true,
                        UpdatedAt = clock.UtcNow
                    };
                    db.TenantLinkPreviewSettings.Add(linkPreviewRow);
                    created = true;
                    changes.Add("linkPreview.created");
                }

                if (request.LinkPreview.Enabled is { } linkPreviewEnabled && linkPreviewRow.Enabled != linkPreviewEnabled)
                {
                    linkPreviewRow.Enabled = linkPreviewEnabled;
                    changes.Add("linkPreview.enabled");
                }

                if (created || changes.Any(c => c.StartsWith("linkPreview.", StringComparison.Ordinal)))
                {
                    linkPreviewRow.UpdatedAt = clock.UtcNow;
                }
            }

            await settingsAdmin.ApplyFilesAsync(workspace, request.Files, changes, ct);
            await settingsAdmin.ApplyRateLimitAsync(workspace, request.RateLimit, changes, ct);
            var processResult = await settingsAdmin.ApplyProcessAsync(
                new UpdateSensitiveProcessRequest(
                    request.Ai?.ProcessEnabled,
                    request.Email?.ProcessEnabled,
                    request.Retention?.ProcessEnabled,
                    request.Push?.ProcessEnabled,
                    request.LinkPreview?.ProcessEnabled,
                    request.Ai?.OpenRouterBaseUrl,
                    request.Retention?.DefaultRetentionDays,
                    request.Retention?.BatchSize,
                    request.Retention?.IntervalMinutes,
                    request.LinkPreview?.TimeoutMs),
                changes,
                ct);
            if (!processResult.Ok)
            {
                return Results.Json(new { error = processResult.Error, message = processResult.Message }, statusCode: processResult.Status);
            }

            if (changes.Count > 0)
            {
                audit.Add(new AuditEvent
                {
                    TenantId = workspace.TenantId,
                    ActorUserId = profile.Id,
                    Action = AuditActions.SettingsChange,
                    EntityType = "SensitiveSettings",
                    EntityId = workspace.Id.Value.ToString(),
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        workspaceId = workspace.Id.Value,
                        changes
                    })
                });
                await db.SaveChangesAsync(ct);
                cacheInvalidator.InvalidateTenant(workspace.TenantId);
                cacheInvalidator.InvalidateWorkspace(workspace.TenantId, workspace.Id);
                cacheInvalidator.InvalidateProcess();
            }

            return Results.Ok(await settingsAdmin.BuildResponseAsync(workspace, ct));
        }).RequirePermission(Permissions.Workspace.Admin);

        v1.MapPost("/admin/settings/credentials/openrouter/rotate", async (
            RotateCredentialRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IPermissionChecker permissions,
            RuntimeSettingsAdminService settingsAdmin,
            IClock clock,
            CancellationToken ct) =>
        {
            var access = await ResolveSensitiveSettingsAccessAsync(
                http, db, tenant, permissions, request.WorkspaceId, clock, ct);
            if (access is null)
            {
                return Results.Forbid();
            }

            var (profile, workspace) = access.Value;
            var result = await settingsAdmin.RotateAsync(
                workspace, profile.Id, RuntimeSecretKinds.OpenRouterApiKey, request.Value ?? string.Empty, ct);
            return result.Ok
                ? Results.Ok(new { configured = result.Configured, mask = result.Mask, keyVersion = result.KeyVersion, rotatedAt = result.RotatedAt })
                : Results.Json(new { error = result.Error, message = result.Message }, statusCode: result.StatusCode);
        }).RequirePermission(Permissions.Workspace.Admin);

        v1.MapPost("/admin/settings/credentials/smtp/rotate", async (
            RotateCredentialRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IPermissionChecker permissions,
            RuntimeSettingsAdminService settingsAdmin,
            IClock clock,
            CancellationToken ct) =>
        {
            var access = await ResolveSensitiveSettingsAccessAsync(
                http, db, tenant, permissions, request.WorkspaceId, clock, ct);
            if (access is null)
            {
                return Results.Forbid();
            }

            var (profile, workspace) = access.Value;
            var result = await settingsAdmin.RotateAsync(
                workspace, profile.Id, RuntimeSecretKinds.SmtpPassword, request.Value ?? string.Empty, ct);
            return result.Ok
                ? Results.Ok(new { configured = result.Configured, mask = result.Mask, keyVersion = result.KeyVersion, rotatedAt = result.RotatedAt })
                : Results.Json(new { error = result.Error, message = result.Message }, statusCode: result.StatusCode);
        }).RequirePermission(Permissions.Workspace.Admin);

        v1.MapPost("/admin/settings/credentials/webhook/rotate", async (
            RotateCredentialRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IPermissionChecker permissions,
            RuntimeSettingsAdminService settingsAdmin,
            IClock clock,
            CancellationToken ct) =>
        {
            var access = await ResolveSensitiveSettingsAccessAsync(
                http, db, tenant, permissions, request.WorkspaceId, clock, ct);
            if (access is null)
            {
                return Results.Forbid();
            }

            var (profile, workspace) = access.Value;
            var result = await settingsAdmin.RotateAsync(
                workspace, profile.Id, RuntimeSecretKinds.WebhookSigningSecret, request.Value ?? string.Empty, ct);
            return result.Ok
                ? Results.Ok(new { configured = result.Configured, mask = result.Mask, keyVersion = result.KeyVersion, rotatedAt = result.RotatedAt })
                : Results.Json(new { error = result.Error, message = result.Message }, statusCode: result.StatusCode);
        }).RequirePermission(Permissions.Workspace.Admin);

        v1.MapPost("/admin/settings/credentials/vapid/rotate", async (
            RotateVapidRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IPermissionChecker permissions,
            RuntimeSettingsAdminService settingsAdmin,
            IClock clock,
            CancellationToken ct) =>
        {
            var access = await ResolveSensitiveSettingsAccessAsync(
                http, db, tenant, permissions, request.WorkspaceId, clock, ct);
            if (access is null)
            {
                return Results.Forbid();
            }

            var (profile, workspace) = access.Value;
            var result = await settingsAdmin.RotateVapidAsync(
                workspace,
                profile.Id,
                request.PublicKey ?? string.Empty,
                request.PrivateKey ?? string.Empty,
                request.Subject,
                ct);
            return result.Ok
                ? Results.Ok(new { configured = result.Configured, mask = result.Mask, keyVersion = result.KeyVersion, rotatedAt = result.RotatedAt })
                : Results.Json(new { error = result.Error, message = result.Message }, statusCode: result.StatusCode);
        }).RequirePermission(Permissions.Workspace.Admin);

        v1.MapPost("/admin/settings/encryption/reencrypt", async (
            RotateCredentialRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IPermissionChecker permissions,
            RuntimeSettingsAdminService settingsAdmin,
            IClock clock,
            CancellationToken ct) =>
        {
            var access = await ResolveSensitiveSettingsAccessAsync(
                http, db, tenant, permissions, request.WorkspaceId, clock, ct);
            if (access is null)
            {
                return Results.Forbid();
            }

            var (profile, workspace) = access.Value;
            var (ok, status, error, message, reencrypted) = await settingsAdmin.ReencryptAsync(workspace, profile.Id, ct);
            if (!ok)
            {
                return Results.Json(new { error, message }, statusCode: status);
            }

            var payload = await settingsAdmin.BuildResponseAsync(workspace, ct);
            return Results.Ok(new { reencrypted, settings = payload });
        }).RequirePermission(Permissions.Workspace.Admin);

        // B-046: workspace compliance export (ZIP of JSON) — workspace.admin only (not Auditor).
        v1.MapGet("/admin/workspaces/{workspaceId:guid}/export", async (
            Guid workspaceId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IPermissionChecker permissions,
            IAuditWriter audit,
            IClock clock,
            CancellationToken ct) =>
        {
            var access = await ResolveSensitiveSettingsAccessAsync(
                http, db, tenant, permissions, workspaceId, clock, ct);
            if (access is null)
            {
                return Results.Forbid();
            }

            var (profile, workspace) = access.Value;
            var exportedAt = clock.UtcNow;
            var zipBytes = await BuildWorkspaceExportZipAsync(workspace, db, profile.Id, exportedAt, ct);

            audit.Add(new AuditEvent
            {
                TenantId = workspace.TenantId,
                ActorUserId = profile.Id,
                Action = AuditActions.WorkspaceExport,
                EntityType = "Workspace",
                EntityId = workspace.Id.Value.ToString(),
                MetadataJson = JsonSerializer.Serialize(new
                {
                    workspaceId = workspace.Id.Value,
                    byteLength = zipBytes.Length
                })
            });
            await db.SaveChangesAsync(ct);

            var fileName = $"vibechat-export-{workspace.Slug}-{exportedAt:yyyyMMddHHmmss}.zip";
            return Results.File(zipBytes, "application/zip", fileName);
        }).RequirePermission(Permissions.Workspace.Admin);

        v1.MapGet("/admin/health-summary", async (HealthCheckService health, CancellationToken ct) =>
        {
            var report = await health.CheckHealthAsync(ct);
            return Results.Ok(new { status = report.Status.ToString(), checks = report.Entries.ToDictionary(x => x.Key, x => x.Value.Status.ToString()) });
        }).RequirePermission(Permissions.Admin.Dashboard);

        v1.MapGet("/admin/version", () => Results.Ok(new { name = "VibeChat.Api", version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.1.0" }))
            .RequirePermission(Permissions.Admin.Dashboard);
    }
}
