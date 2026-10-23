using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Infrastructure;
using VibeChat.Infrastructure.Compliance;
using VibeChat.Integrations;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Api.Endpoints;

internal static class BridgeEndpoints
{
    internal static void MapBridges(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/channels/{channelId:guid}/bridge", Read)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/bridges", Create)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/bridges/{bridgeId:guid}/consent", Consent)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/bridges/{bridgeId:guid}/pause", Pause)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/bridges/{bridgeId:guid}/resume", Resume)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/bridges/{bridgeId:guid}/disconnect", Disconnect)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapGet("/admin/workspaces/{workspaceId:guid}/bridges/{bridgeId:guid}/mapping", Mapping)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/bridges/{bridgeId:guid}/events", Inbound)
            .AllowAnonymous()
            .AllowPermissionGateExempt("bridge bot token (B-138)");
        v1.MapPost("/bridges/{bridgeId:guid}/outbound", Outbound)
            .AllowAnonymous()
            .AllowPermissionGateExempt("bridge bot token (B-138)");
    }

    private static async Task<IResult> Read(
        Guid workspaceId, Guid channelId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IPermissionChecker permissions, IOptions<BridgeOptions> options, IClock clock, CancellationToken ct)
    {
        if (Off(options.Value) is { } off) return off;
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var channel = await RequestAuth.ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
        if (channel is null || channel.WorkspaceId.Value != workspaceId) return Results.Forbid();
        var bridge = await db.Bridges.AsNoTracking().FirstOrDefaultAsync(x => x.ChannelId == channel.Id, ct);
        if (bridge is null) return Error(StatusCodes.Status404NotFound, BridgeErrors.NotFound);
        var canManage = await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Workspace.Admin, ct);
        return Results.Json(View(bridge, canManage));
    }

    private static async Task<IResult> Create(
        Guid workspaceId, BridgeCreateRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IAuditWriter audit, IOptions<BridgeOptions> options, IOptions<BotIntegrationOptions> bots, IClock clock, CancellationToken ct)
    {
        if (Off(options.Value) is { } off) return off;
        if (!bots.Value.Enabled) return IntegrationBotEndpoints.Disabled();
        if (string.IsNullOrWhiteSpace(options.Value.InstanceId)) return Error(StatusCodes.Status400BadRequest, BridgeErrors.InstanceMissing);
        if (!TryKey(http, request.IdempotencyKey, out var key)) return Error(StatusCodes.Status400BadRequest, "IdempotencyKeyRequired");
        var gate = await AuthorizeAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate is null) return Results.Forbid();
        if (BridgeRules.EgressBlocked(request.DestinationHost) || BridgeRules.EgressBlocked(request.RemoteRoom) || BridgeRules.EgressBlocked(request.Network))
        {
            return Error(StatusCodes.Status400BadRequest, BridgeErrors.EgressBlocked);
        }

        if (!BridgeRules.HostPinned(request.DestinationHost, options.Value.PinnedHosts))
        {
            return Error(StatusCodes.Status400BadRequest, BridgeErrors.DestinationUnpinned);
        }

        if (!BridgeRules.TryNetwork(request.Network, out var network) || !BridgeRules.TryRoom(request.RemoteRoom, out var room))
        {
            return Error(StatusCodes.Status400BadRequest, BridgeErrors.EnvelopeInvalid);
        }

        if (!BridgeRules.TryNegotiate(request.Capabilities, request.RemoteCapabilities, out var granted, out var negotiateError))
        {
            return Error(StatusCodes.Status400BadRequest, negotiateError);
        }

        var channel = await db.Channels.FirstOrDefaultAsync(x => x.Id == new ChannelId(request.ChannelId) && x.WorkspaceId == gate.Workspace.Id, ct);
        if (channel is null || channel.Type is ChannelType.Direct or ChannelType.GroupDm)
        {
            return Error(StatusCodes.Status400BadRequest, BridgeErrors.ChannelUnsupported);
        }

        var hash = Hash("create", channel.Id.Value, network, room, request.DestinationHost!.Trim(), granted);
        var replay = await db.Bridges.FirstOrDefaultAsync(x => x.WorkspaceId == gate.Workspace.Id && x.IdempotencyKey == key, ct);
        if (replay is not null)
        {
            return replay.RequestHash == hash
                ? Results.Json(View(replay, true))
                : Error(StatusCodes.Status409Conflict, BridgeErrors.IdempotencyConflict);
        }

        if (await db.Bridges.AnyAsync(x => x.ChannelId == channel.Id, ct))
        {
            return Error(StatusCodes.Status409Conflict, BridgeErrors.ChannelTaken);
        }

        var now = clock.UtcNow;
        var (bot, token, _) = IntegrationBotEndpoints.ProvisionBot(
            db, gate.Workspace.TenantId, gate.Workspace.Id, "bridge:" + network, false, now);
        db.IntegrationBotChannelScopes.Add(new IntegrationBotChannelScope
        {
            Id = Guid.NewGuid(),
            TenantId = gate.Workspace.TenantId,
            BotId = bot.Id,
            ChannelId = channel.Id
        });
        db.ChannelMembers.Add(new ChannelMember
        {
            Id = Guid.NewGuid(),
            TenantId = gate.Workspace.TenantId,
            ChannelId = channel.Id,
            UserId = bot.UserId,
            JoinedAt = now
        });
        var bridge = new Bridge
        {
            Id = Guid.NewGuid(),
            TenantId = gate.Workspace.TenantId,
            WorkspaceId = gate.Workspace.Id,
            ChannelId = channel.Id,
            BotId = bot.Id,
            Network = network,
            RemoteRoom = room,
            DestinationHost = request.DestinationHost.Trim(),
            Status = BridgeRules.StatusPending,
            Capabilities = granted,
            DeleteRemoteGuaranteed = false,
            IdempotencyKey = key,
            RequestHash = hash,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Bridges.Add(bridge);
        audit.Add(Audit(gate, AuditActions.BridgeCreate, bridge, now));
        await db.SaveChangesAsync(ct);
        return Results.Json(View(bridge, true, token), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> Consent(
        Guid workspaceId, Guid bridgeId, BridgeConsentRequest request, HttpContext http, VibeChatDbContext db,
        ITenantContext tenant, IAuditWriter audit, IOptions<BridgeOptions> options, IClock clock, CancellationToken ct)
    {
        if (Off(options.Value) is { } off) return off;
        var gate = await AuthorizeAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate is null) return Results.Forbid();
        var bridge = await FindAsync(db, gate.Workspace.Id, bridgeId, ct);
        if (bridge is null) return Error(StatusCodes.Status404NotFound, BridgeErrors.NotFound);
        if (bridge.Status == BridgeRules.StatusActive) return Results.Json(View(bridge, true));
        if (bridge.Status != BridgeRules.StatusPending) return Error(StatusCodes.Status409Conflict, BridgeErrors.StateConflict);
        if (!request.AcknowledgeCopy) return Error(StatusCodes.Status400BadRequest, BridgeErrors.ConsentRequired);
        var now = clock.UtcNow;
        bridge.Status = BridgeRules.StatusActive;
        bridge.ConsentAt = now;
        bridge.ConsentedByUserId = gate.ActorId.Value;
        bridge.UpdatedAt = now;
        audit.Add(Audit(gate, AuditActions.BridgeConsent, bridge, now));
        await db.SaveChangesAsync(ct);
        return Results.Json(View(bridge, true));
    }

    private static Task<IResult> Pause(
        Guid workspaceId, Guid bridgeId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IAuditWriter audit, IOptions<BridgeOptions> options, IClock clock, CancellationToken ct) =>
        Transition(workspaceId, bridgeId, BridgeRules.StatusActive, BridgeRules.StatusPaused, AuditActions.BridgePause, false, http, db, tenant, audit, options, clock, ct);

    private static Task<IResult> Resume(
        Guid workspaceId, Guid bridgeId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IAuditWriter audit, IOptions<BridgeOptions> options, IClock clock, CancellationToken ct) =>
        Transition(workspaceId, bridgeId, BridgeRules.StatusPaused, BridgeRules.StatusActive, AuditActions.BridgeResume, false, http, db, tenant, audit, options, clock, ct);

    private static Task<IResult> Disconnect(
        Guid workspaceId, Guid bridgeId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IAuditWriter audit, IOptions<BridgeOptions> options, IClock clock, CancellationToken ct) =>
        Transition(workspaceId, bridgeId, null, BridgeRules.StatusDisconnected, AuditActions.BridgeDisconnect, true, http, db, tenant, audit, options, clock, ct);

    private static async Task<IResult> Mapping(
        Guid workspaceId, Guid bridgeId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IOptions<BridgeOptions> options, IClock clock, CancellationToken ct)
    {
        if (Off(options.Value) is { } off) return off;
        var gate = await AuthorizeAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate is null) return Results.Forbid();
        var bridge = await FindAsync(db, gate.Workspace.Id, bridgeId, ct);
        if (bridge is null) return Error(StatusCodes.Status404NotFound, BridgeErrors.NotFound);
        var rows = await db.BridgeIdentities.AsNoTracking()
            .Where(x => x.BridgeId == bridge.Id)
            .OrderBy(x => x.RemoteSubject)
            .ToListAsync(ct);
        return Results.Json(new
        {
            bridgeId = bridge.Id,
            deleteRemoteGuaranteed = false,
            deleteLimitation = BridgeRules.DeleteLimitation,
            identities = rows.Select(row => new
            {
                remoteSubject = row.RemoteSubject,
                displayName = row.DisplayName,
                localMarker = row.LocalMarker,
                remote = true
            }).ToArray()
        });
    }

    private static async Task<IResult> Inbound(
        Guid bridgeId, BridgeInboundRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IMessageWriter messages, IOutboxWriter outbox, IAuditWriter audit, IOptions<BridgeOptions> options,
        IClock clock, CancellationToken ct)
    {
        if (Off(options.Value) is { } off) return off;
        var session = await IntegrationBotEndpoints.AuthenticateAsync(http, db, tenant, clock, ct);
        if (session is null) return Results.Unauthorized();
        var bridge = await db.Bridges.FirstOrDefaultAsync(x => x.Id == bridgeId && x.TenantId == session.Bot.TenantId, ct);
        if (bridge is null) return Error(StatusCodes.Status404NotFound, BridgeErrors.NotFound);
        if (bridge.BotId != session.Bot.Id) return Error(StatusCodes.Status403Forbidden, BridgeErrors.ScopeMismatch);
        if (!session.Bot.Enabled || !BridgeRules.AllowsTraffic(bridge.Status))
        {
            return Error(StatusCodes.Status409Conflict, BridgeErrors.TrafficBlocked);
        }

        if (!string.Equals(request.Schema, BridgeRules.EnvelopeSchema, StringComparison.Ordinal) || request.SchemaVersion != BridgeRules.SchemaVersion)
        {
            return Error(StatusCodes.Status400BadRequest, BridgeErrors.EnvelopeInvalid);
        }

        if (!BridgeRules.TryEventId(request.EventId, out var eventId) || !BridgeRules.TrySubject(request.RemoteSubject, out var subject))
        {
            return Error(StatusCodes.Status400BadRequest, BridgeErrors.EnvelopeInvalid);
        }

        var eventType = (request.EventType ?? string.Empty).Trim().ToLowerInvariant();
        if (!BridgeRules.Capabilities.Contains(eventType, StringComparer.Ordinal))
        {
            return Error(StatusCodes.Status400BadRequest, BridgeErrors.CapabilityUnknown);
        }

        var existing = await db.BridgeEvents.FirstOrDefaultAsync(x => x.BridgeId == bridge.Id && x.RemoteEventId == eventId, ct);
        if (existing is not null)
        {
            return Results.Json(new { applied = existing.Outcome == BridgeRules.Applied, duplicated = true, loop = existing.Outcome == BridgeRules.Loop, limitation = existing.Outcome == BridgeRules.Limited ? BridgeRules.DeleteLimitation : null, messageId = existing.LocalMessageId });
        }

        var outboundIds = await db.BridgeEvents.AsNoTracking()
            .Where(x => x.BridgeId == bridge.Id && x.Direction == BridgeRules.Outbound)
            .Select(x => x.RemoteEventId)
            .ToListAsync(ct);
        if (BridgeRules.IsLoop(request.OriginInstanceId, options.Value.InstanceId, eventId, outboundIds.ToHashSet(StringComparer.Ordinal)))
        {
            AddEvent(db, bridge, BridgeRules.Inbound, eventType, eventId, null, subject, string.Empty, null, request.Cursor, BridgeRules.Loop, clock.UtcNow);
            await db.SaveChangesAsync(ct);
            return Results.Json(new { applied = false, duplicated = false, loop = true });
        }

        if (!bridge.Capabilities.Contains(eventType, StringComparer.Ordinal))
        {
            var limited = eventType == BridgeRules.Delete;
            if (limited)
            {
                AddEvent(db, bridge, BridgeRules.Inbound, eventType, eventId, null, subject, string.Empty, null, request.Cursor, BridgeRules.Limited, clock.UtcNow);
                audit.Add(new AuditEvent
                {
                    TenantId = bridge.TenantId,
                    ActorUserId = session.Bot.UserId,
                    Action = AuditActions.BridgeDeleteLimited,
                    EntityType = "Bridge",
                    EntityId = bridge.Id.ToString(),
                    MetadataJson = JsonSerializer.Serialize(new { bridgeId = bridge.Id, deleteRemoteGuaranteed = false, limitation = BridgeRules.DeleteLimitation }),
                    OccurredAt = clock.UtcNow
                });
                await db.SaveChangesAsync(ct);
                return Results.Json(new { applied = false, duplicated = false, limitation = BridgeRules.DeleteLimitation, deleteRemoteGuaranteed = false });
            }

            return Error(StatusCodes.Status409Conflict, BridgeErrors.CapabilityDenied);
        }

        if (!BridgeRules.TryDisplayName(request.DisplayName, out var displayName))
        {
            return Error(StatusCodes.Status400BadRequest, BridgeErrors.EnvelopeInvalid);
        }

        await RememberIdentityAsync(db, bridge, subject, displayName, clock.UtcNow, ct);
        var link = BridgeRules.SafeLink(request.Link, bridge.DestinationHost);
        if (request.Cursor > bridge.RemoteCheckpoint) bridge.RemoteCheckpoint = request.Cursor;

        if (eventType is BridgeRules.Message or BridgeRules.File)
        {
            var body = eventType == BridgeRules.File ? request.FileName : request.Body;
            if (eventType == BridgeRules.File)
            {
                if (!BridgeRules.TryFileName(body, out var fileName)) return Error(StatusCodes.Status400BadRequest, BridgeErrors.EnvelopeInvalid);
                body = fileName;
            }
            else if (MessageBodyPolicies.IsEmpty(body) || !MessageBodyPolicies.IsWithinLimit(body))
            {
                return Error(StatusCodes.Status400BadRequest, BridgeErrors.EnvelopeInvalid);
            }

            var sent = await messages.SendAsync(new SendMessageCommand(
                bridge.TenantId, session.Bot.UserId, bridge.ChannelId, MessageId.New(),
                "bridge:" + bridge.Id.ToString("N") + ":" + eventId, body!.Trim(), null, null, null, AuthorIsBot: true), ct);
            AddEvent(db, bridge, BridgeRules.Inbound, eventType, eventId, sent.MessageId.Value, subject, displayName, link, request.Cursor, BridgeRules.Applied, clock.UtcNow);
            audit.Add(InboundAudit(bridge, session.Bot.UserId, eventType, clock.UtcNow));
            await db.SaveChangesAsync(ct);
            return Results.Json(new { applied = true, duplicated = sent.Idempotent, messageId = sent.MessageId.Value, deleteRemoteGuaranteed = false }, statusCode: sent.Idempotent ? StatusCodes.Status200OK : StatusCodes.Status201Created);
        }

        if (eventType == BridgeRules.Reaction)
        {
            AddEvent(db, bridge, BridgeRules.Inbound, eventType, eventId, null, subject, displayName, link, request.Cursor, BridgeRules.Applied, clock.UtcNow);
            audit.Add(InboundAudit(bridge, session.Bot.UserId, eventType, clock.UtcNow));
            await db.SaveChangesAsync(ct);
            return Results.Json(new { applied = true, duplicated = false, deleteRemoteGuaranteed = false });
        }

        if (string.IsNullOrWhiteSpace(request.TargetEventId)) return Error(StatusCodes.Status400BadRequest, BridgeErrors.TargetRequired);
        var target = await db.BridgeEvents.FirstOrDefaultAsync(x => x.BridgeId == bridge.Id && x.RemoteEventId == request.TargetEventId && x.LocalMessageId != null, ct);
        if (target?.LocalMessageId is not Guid messageId) return Error(StatusCodes.Status404NotFound, BridgeErrors.NotFound);
        var message = await db.Messages.FirstOrDefaultAsync(x => x.Id == new MessageId(messageId) && x.ConversationId == bridge.ChannelId, ct);
        if (message is null) return Error(StatusCodes.Status404NotFound, BridgeErrors.NotFound);

        if (eventType == BridgeRules.Edit)
        {
            var nextBody = MessageBodyPolicies.Normalize(request.Body);
            if (MessageBodyPolicies.IsEmpty(nextBody) || !MessageBodyPolicies.IsWithinLimit(nextBody))
            {
                return Error(StatusCodes.Status400BadRequest, BridgeErrors.EnvelopeInvalid);
            }

            message.Body = nextBody;
            message.EditedAt = clock.UtcNow;
            outbox.Add(new OutboxMessage
            {
                TenantId = bridge.TenantId,
                Type = nameof(MessageEditedEvent),
                Payload = JsonSerializer.Serialize(new { tenantId = bridge.TenantId.Value, channelId = bridge.ChannelId.Value, conversationId = message.ConversationId.Value, messageId, sequence = message.Sequence, body = message.Body, editedAt = message.EditedAt })
            });
        }
        else if (eventType == BridgeRules.Delete)
        {
            if (message.DeletedAt is null)
            {
                message.DeletedAt = clock.UtcNow;
                message.DeletedBy = session.Bot.UserId;
                outbox.Add(new OutboxMessage
                {
                    TenantId = bridge.TenantId,
                    Type = nameof(MessageDeletedEvent),
                    Payload = JsonSerializer.Serialize(new { tenantId = bridge.TenantId.Value, channelId = bridge.ChannelId.Value, conversationId = message.ConversationId.Value, messageId, sequence = message.Sequence, deletedAt = message.DeletedAt })
                });
            }

            audit.Add(new AuditEvent
            {
                TenantId = bridge.TenantId,
                ActorUserId = session.Bot.UserId,
                Action = AuditActions.BridgeDeleteLimited,
                EntityType = "Bridge",
                EntityId = bridge.Id.ToString(),
                MetadataJson = JsonSerializer.Serialize(new { bridgeId = bridge.Id, messageId, deleteRemoteGuaranteed = false, limitation = BridgeRules.DeleteLimitation }),
                OccurredAt = clock.UtcNow
            });
        }

        AddEvent(db, bridge, BridgeRules.Inbound, eventType, eventId, messageId, subject, displayName, link, request.Cursor, BridgeRules.Applied, clock.UtcNow);
        bridge.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Json(new { applied = true, duplicated = false, messageId, deleteRemoteGuaranteed = false, limitation = eventType == BridgeRules.Delete ? BridgeRules.DeleteLimitation : null });
    }

    private static async Task<IResult> Outbound(
        Guid bridgeId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IAuditWriter audit, DlpService dlp,
        IOptions<BridgeOptions> options, IClock clock, CancellationToken ct)
    {
        if (Off(options.Value) is { } off) return off;
        var session = await IntegrationBotEndpoints.AuthenticateAsync(http, db, tenant, clock, ct);
        if (session is null) return Results.Unauthorized();
        var bridge = await db.Bridges.FirstOrDefaultAsync(x => x.Id == bridgeId && x.TenantId == session.Bot.TenantId, ct);
        if (bridge is null) return Error(StatusCodes.Status404NotFound, BridgeErrors.NotFound);
        if (bridge.BotId != session.Bot.Id) return Error(StatusCodes.Status403Forbidden, BridgeErrors.ScopeMismatch);
        if (!session.Bot.Enabled || !BridgeRules.AllowsTraffic(bridge.Status))
        {
            return Error(StatusCodes.Status409Conflict, BridgeErrors.TrafficBlocked);
        }

        var channel = await db.Channels.AsNoTracking().FirstAsync(x => x.Id == bridge.ChannelId, ct);
        var rows = await db.Messages.AsNoTracking()
            .Where(x => x.ConversationId == bridge.ChannelId && x.Sequence > bridge.LocalCheckpoint && x.DeletedAt == null)
            .OrderBy(x => x.Sequence)
            .Take(BridgeRules.MaxPull)
            .Select(x => new OutboundRow(x.Id, x.Sequence, x.AuthorId, x.Body))
            .ToListAsync(ct);
        var inboundIds = await db.BridgeEvents.AsNoTracking()
            .Where(x => x.BridgeId == bridge.Id && x.Direction == BridgeRules.Inbound && x.LocalMessageId != null)
            .Select(x => x.LocalMessageId!.Value)
            .ToListAsync(ct);
        var known = inboundIds.ToHashSet();
        var envelopes = new List<object>();
        long checkpoint = bridge.LocalCheckpoint;
        foreach (var row in rows)
        {
            checkpoint = row.Sequence;
            var remoteId = BridgeRules.LocalEventId(row.Id.Value);
            var seen = await db.BridgeEvents.AnyAsync(x => x.BridgeId == bridge.Id && x.RemoteEventId == remoteId, ct);
            if (seen || row.AuthorId == session.Bot.UserId || known.Contains(row.Id.Value))
            {
                continue;
            }

            var outcome = await dlp.EvaluateAsync(
                bridge.TenantId, bridge.WorkspaceId, channel.SpaceId, channel.Id.Value, channel.Type.ToString(),
                DlpActions.Federation, row.Body, row.Id.Value, save: true, ct);
            if (!outcome.Proceed)
            {
                AddEvent(db, bridge, BridgeRules.Outbound, BridgeRules.Message, remoteId, row.Id.Value, "local", string.Empty, null, row.Sequence, BridgeRules.Skipped, clock.UtcNow);
                audit.Add(new AuditEvent
                {
                    TenantId = bridge.TenantId,
                    ActorUserId = session.Bot.UserId,
                    Action = AuditActions.BridgeCopyBlocked,
                    EntityType = "Bridge",
                    EntityId = bridge.Id.ToString(),
                    MetadataJson = JsonSerializer.Serialize(new { bridgeId = bridge.Id, messageId = row.Id.Value, reason = outcome.ReasonCode }),
                    OccurredAt = clock.UtcNow
                });
                continue;
            }

            var canonical = BridgeRules.Canonical(
                BridgeRules.Message, remoteId, options.Value.InstanceId, bridge.DestinationHost, bridge.RemoteRoom,
                bridge.ChannelId.Value, "local:" + row.AuthorId.Value.ToString("N"), "local", row.Body, row.Sequence, remoteId);
            AddEvent(db, bridge, BridgeRules.Outbound, BridgeRules.Message, remoteId, row.Id.Value, "local", string.Empty, null, row.Sequence, BridgeRules.Applied, clock.UtcNow);
            envelopes.Add(new
            {
                schema = BridgeRules.EnvelopeSchema,
                schemaVersion = BridgeRules.SchemaVersion,
                eventType = BridgeRules.Message,
                eventId = remoteId,
                originInstanceId = options.Value.InstanceId,
                destinationHost = bridge.DestinationHost,
                remoteRoom = bridge.RemoteRoom,
                channelId = bridge.ChannelId.Value,
                body = row.Body,
                cursor = row.Sequence,
                copyPolicy = BridgeRules.CopyPolicy,
                deleteRemoteGuaranteed = false,
                deleteLimitation = BridgeRules.DeleteLimitation,
                signature = BridgeRules.Sign(session.Token.TokenHash, canonical)
            });
        }

        bridge.LocalCheckpoint = checkpoint;
        bridge.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Json(new { checkpoint, deleteRemoteGuaranteed = false, deleteLimitation = BridgeRules.DeleteLimitation, envelopes });
    }

    private static async Task<IResult> Transition(
        Guid workspaceId, Guid bridgeId, string? required, string next, string action, bool disableBot,
        HttpContext http, VibeChatDbContext db, ITenantContext tenant, IAuditWriter audit, IOptions<BridgeOptions> options,
        IClock clock, CancellationToken ct)
    {
        if (Off(options.Value) is { } off) return off;
        var gate = await AuthorizeAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate is null) return Results.Forbid();
        var bridge = await FindAsync(db, gate.Workspace.Id, bridgeId, ct);
        if (bridge is null) return Error(StatusCodes.Status404NotFound, BridgeErrors.NotFound);
        if (bridge.Status == next) return Results.Json(View(bridge, true));
        if (required is not null && bridge.Status != required) return Error(StatusCodes.Status409Conflict, BridgeErrors.StateConflict);
        var now = clock.UtcNow;
        bridge.Status = next;
        bridge.UpdatedAt = now;
        if (disableBot)
        {
            var bot = await db.IntegrationBots.FirstAsync(x => x.Id == bridge.BotId, ct);
            bot.Enabled = false;
        }

        audit.Add(Audit(gate, action, bridge, now));
        await db.SaveChangesAsync(ct);
        return Results.Json(View(bridge, true));
    }

    private static async Task RememberIdentityAsync(VibeChatDbContext db, Bridge bridge, string subject, string displayName, DateTimeOffset now, CancellationToken ct)
    {
        var row = await db.BridgeIdentities.FirstOrDefaultAsync(x => x.BridgeId == bridge.Id && x.RemoteSubject == subject, ct);
        if (row is not null)
        {
            row.DisplayName = displayName;
            return;
        }

        db.BridgeIdentities.Add(new BridgeIdentity
        {
            Id = Guid.NewGuid(),
            TenantId = bridge.TenantId,
            WorkspaceId = bridge.WorkspaceId,
            BridgeId = bridge.Id,
            RemoteSubject = subject,
            DisplayName = displayName,
            LocalMarker = Guid.NewGuid(),
            CreatedAt = now
        });
    }

    private static void AddEvent(
        VibeChatDbContext db, Bridge bridge, string direction, string eventType, string remoteEventId, Guid? messageId,
        string subject, string displayName, string? link, long cursor, string outcome, DateTimeOffset now)
    {
        db.BridgeEvents.Add(new BridgeEvent
        {
            Id = Guid.NewGuid(),
            TenantId = bridge.TenantId,
            WorkspaceId = bridge.WorkspaceId,
            BridgeId = bridge.Id,
            Direction = direction,
            EventType = eventType,
            RemoteEventId = remoteEventId,
            LocalMessageId = messageId,
            RemoteSubject = subject,
            DisplayName = displayName,
            Link = link,
            Cursor = cursor,
            Outcome = outcome,
            CreatedAt = now
        });
    }

    private static async Task<AuthGate?> AuthorizeAsync(
        Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        return workspace is null ? null : new AuthGate(workspace, profile.Id);
    }

    private static Task<Bridge?> FindAsync(VibeChatDbContext db, WorkspaceId workspaceId, Guid bridgeId, CancellationToken ct) =>
        db.Bridges.FirstOrDefaultAsync(x => x.Id == bridgeId && x.WorkspaceId == workspaceId, ct);

    private static bool TryKey(HttpContext http, string? bodyKey, out string key)
    {
        var raw = bodyKey;
        if (string.IsNullOrWhiteSpace(raw) && http.Request.Headers.TryGetValue("Idempotency-Key", out var header))
        {
            raw = header.ToString();
        }

        return PluginSdkIdempotency.TryNormalize(raw, out key);
    }

    private static string Hash(string operation, Guid channelId, string network, string room, string host, string[] capabilities)
    {
        var text = string.Join('|', new[] { operation, channelId.ToString("D"), network, room, host.ToLowerInvariant(), string.Join(',', capabilities) });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static IResult? Off(BridgeOptions options) =>
        options.Enabled ? null : Error(StatusCodes.Status404NotFound, BridgeErrors.Disabled);

    private static IResult Error(int status, string code) => Results.Json(new { error = code }, statusCode: status);

    private static AuditEvent Audit(AuthGate gate, string action, Bridge bridge, DateTimeOffset now) => new()
    {
        TenantId = gate.Workspace.TenantId,
        ActorUserId = gate.ActorId,
        Action = action,
        EntityType = "Bridge",
        EntityId = bridge.Id.ToString(),
        MetadataJson = JsonSerializer.Serialize(new
        {
            bridgeId = bridge.Id,
            channelId = bridge.ChannelId.Value,
            network = bridge.Network,
            remoteRoom = bridge.RemoteRoom,
            status = bridge.Status,
            deleteRemoteGuaranteed = false,
            limitation = BridgeRules.DeleteLimitation
        }),
        OccurredAt = now
    };

    private static AuditEvent InboundAudit(Bridge bridge, UserId actor, string eventType, DateTimeOffset now) => new()
    {
        TenantId = bridge.TenantId,
        ActorUserId = actor,
        Action = AuditActions.BridgeInbound,
        EntityType = "Bridge",
        EntityId = bridge.Id.ToString(),
        MetadataJson = JsonSerializer.Serialize(new { bridgeId = bridge.Id, eventType, deleteRemoteGuaranteed = false }),
        OccurredAt = now
    };

    private static object View(Bridge bridge, bool canManage, string? token = null) => new
    {
        id = bridge.Id,
        channelId = bridge.ChannelId.Value,
        network = bridge.Network,
        remoteRoom = bridge.RemoteRoom,
        destinationHost = bridge.DestinationHost,
        status = bridge.Status,
        capabilities = bridge.Capabilities,
        consentRequired = bridge.Status == BridgeRules.StatusPending,
        deleteRemoteGuaranteed = false,
        deleteLimitation = BridgeRules.DeleteLimitation,
        canManage,
        token
    };

    private sealed record AuthGate(Workspace Workspace, UserId ActorId);

    private sealed record OutboundRow(MessageId Id, long Sequence, UserId AuthorId, string Body);

    private sealed class BridgeCreateRequest
    {
        public Guid ChannelId { get; set; }
        public string? Network { get; set; }
        public string? RemoteRoom { get; set; }
        public string? DestinationHost { get; set; }
        public string[]? Capabilities { get; set; }
        public string[]? RemoteCapabilities { get; set; }
        public string? IdempotencyKey { get; set; }
    }

    private sealed class BridgeConsentRequest
    {
        public bool AcknowledgeCopy { get; set; }
        public string? IdempotencyKey { get; set; }
    }

    private sealed class BridgeInboundRequest
    {
        public string? Schema { get; set; }
        public int SchemaVersion { get; set; }
        public string? EventType { get; set; }
        public string? EventId { get; set; }
        public string? OriginInstanceId { get; set; }
        public string? RemoteSubject { get; set; }
        public string? DisplayName { get; set; }
        public string? Body { get; set; }
        public string? FileName { get; set; }
        public string? Link { get; set; }
        public string? TargetEventId { get; set; }
        public long Cursor { get; set; }
    }
}
