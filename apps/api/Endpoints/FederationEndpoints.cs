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

internal static class FederationEndpoints
{
    internal static void MapFederation(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/channels/{channelId:guid}/federation", Read)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/federation/trusts", CreateTrust)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/federation/trusts/{trustId:guid}/consent", Consent)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/federation/trusts/{trustId:guid}/pause", Pause)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/federation/trusts/{trustId:guid}/resume", Resume)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/federation/trusts/{trustId:guid}/revoke", Revoke)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/federation/trusts/{trustId:guid}/keys/rotate", Rotate)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/federation/trusts/{trustId:guid}/keys/retire", Retire)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/federation/trusts/{trustId:guid}/rooms", CreateRoom)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/federation/rooms/{roomId:guid}/events", Inbound)
            .AllowAnonymous()
            .AllowPermissionGateExempt("federation bot token (B-065)");
        v1.MapPost("/federation/rooms/{roomId:guid}/reconcile", Reconcile)
            .AllowAnonymous()
            .AllowPermissionGateExempt("federation bot token (B-065)");
    }

    private static async Task<IResult> Read(
        Guid workspaceId, Guid channelId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IPermissionChecker permissions, IOptions<FederationOptions> options, IClock clock, CancellationToken ct)
    {
        if (Off(options.Value) is { } off) return off;
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var channel = await RequestAuth.ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
        if (channel is null || channel.WorkspaceId.Value != workspaceId) return Results.Forbid();
        var room = await db.FederationRooms.AsNoTracking().FirstOrDefaultAsync(x => x.ChannelId == channel.Id, ct);
        if (room is null) return Error(StatusCodes.Status404NotFound, FederationErrors.NotFound);
        var trust = await db.FederationTrusts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == room.TrustId, ct);
        if (trust is null) return Error(StatusCodes.Status404NotFound, FederationErrors.NotFound);
        var canManage = await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Workspace.Admin, ct);
        var identities = await db.FederationIdentities.AsNoTracking()
            .Where(x => x.TrustId == trust.Id)
            .OrderBy(x => x.RemoteSubject)
            .ToListAsync(ct);
        return Results.Json(View(trust, room, canManage, identities, null));
    }

    private static async Task<IResult> CreateTrust(
        Guid workspaceId, FederationTrustRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IAuditWriter audit, IOptions<FederationOptions> options, IOptions<BotIntegrationOptions> bots, IClock clock, CancellationToken ct)
    {
        if (Off(options.Value) is { } off) return off;
        if (!bots.Value.Enabled) return IntegrationBotEndpoints.Disabled();
        if (string.IsNullOrWhiteSpace(options.Value.InstanceId)) return Error(StatusCodes.Status400BadRequest, FederationErrors.InstanceMissing);
        if (!TryKey(http, request.IdempotencyKey, out var key)) return Error(StatusCodes.Status400BadRequest, "IdempotencyKeyRequired");
        var gate = await AuthorizeAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate is null) return Results.Forbid();
        if (FederationRules.EgressBlocked(request.Domain) || FederationRules.EgressBlocked(request.RemoteInstanceId))
        {
            return Error(StatusCodes.Status400BadRequest, FederationErrors.EgressBlocked);
        }

        if (!FederationRules.DomainAllowed(request.Domain, options.Value.AllowedDomains))
        {
            return Error(StatusCodes.Status400BadRequest, FederationErrors.DomainUnpinned);
        }

        if (!FederationRules.TryDomain(request.Domain, out var domain) || !FederationRules.TryInstance(request.RemoteInstanceId, out var remoteInstance))
        {
            return Error(StatusCodes.Status400BadRequest, FederationErrors.EnvelopeInvalid);
        }

        if (!FederationRules.TryNegotiate(request.Capabilities, request.RemoteCapabilities, request.RemoteSchemaVersion, out var granted, out var negotiateError))
        {
            return Error(StatusCodes.Status400BadRequest, negotiateError);
        }

        var hash = Hash("trust", domain, remoteInstance, string.Join(',', granted));
        var replay = await db.FederationTrusts.FirstOrDefaultAsync(x => x.WorkspaceId == gate.Workspace.Id && x.IdempotencyKey == key, ct);
        if (replay is not null)
        {
            return replay.RequestHash == hash
                ? Results.Json(View(replay, null, true, [], null))
                : Error(StatusCodes.Status409Conflict, FederationErrors.IdempotencyConflict);
        }

        if (await db.FederationTrusts.AnyAsync(x => x.WorkspaceId == gate.Workspace.Id && x.Domain == domain && x.RemoteInstanceId == remoteInstance, ct))
        {
            return Error(StatusCodes.Status409Conflict, FederationErrors.StateConflict);
        }

        var now = clock.UtcNow;
        var (bot, token, tokenRow) = Provision(db, gate.Workspace.TenantId, gate.Workspace.Id, FederationRules.BotName(domain), now);
        var trust = new FederationTrust
        {
            Id = Guid.NewGuid(),
            TenantId = gate.Workspace.TenantId,
            WorkspaceId = gate.Workspace.Id,
            BotId = bot.Id,
            Domain = domain,
            RemoteInstanceId = remoteInstance,
            Status = FederationRules.StatusPending,
            Capabilities = granted,
            CurrentKeyId = FederationRules.InitialKeyId,
            CurrentTokenId = tokenRow.Id,
            DeleteRemoteGuaranteed = false,
            IdempotencyKey = key,
            RequestHash = hash,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.FederationTrusts.Add(trust);
        audit.Add(Audit(gate, AuditActions.FederationTrustCreate, trust, null, now));
        await db.SaveChangesAsync(ct);
        return Results.Json(View(trust, null, true, [], token), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> Consent(
        Guid workspaceId, Guid trustId, FederationConsentRequest request, HttpContext http, VibeChatDbContext db,
        ITenantContext tenant, IAuditWriter audit, IOptions<FederationOptions> options, IClock clock, CancellationToken ct)
    {
        if (Off(options.Value) is { } off) return off;
        var gate = await AuthorizeAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate is null) return Results.Forbid();
        var trust = await FindTrustAsync(db, gate.Workspace.Id, trustId, ct);
        if (trust is null) return Error(StatusCodes.Status404NotFound, FederationErrors.NotFound);
        if (trust.Status == FederationRules.StatusActive) return Results.Json(View(trust, null, true, [], null));
        if (trust.Status != FederationRules.StatusPending) return Error(StatusCodes.Status409Conflict, FederationErrors.StateConflict);
        if (!request.AcknowledgeCopy) return Error(StatusCodes.Status400BadRequest, FederationErrors.ConsentRequired);
        var now = clock.UtcNow;
        trust.Status = FederationRules.StatusActive;
        trust.ConsentAt = now;
        trust.ConsentedByUserId = gate.ActorId.Value;
        trust.UpdatedAt = now;
        audit.Add(Audit(gate, AuditActions.FederationConsent, trust, null, now));
        await db.SaveChangesAsync(ct);
        return Results.Json(View(trust, null, true, [], null));
    }

    private static Task<IResult> Pause(
        Guid workspaceId, Guid trustId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IAuditWriter audit, IOptions<FederationOptions> options, IClock clock, CancellationToken ct) =>
        Transition(workspaceId, trustId, FederationRules.StatusActive, FederationRules.StatusPaused, AuditActions.FederationPause, false, http, db, tenant, audit, options, clock, ct);

    private static Task<IResult> Resume(
        Guid workspaceId, Guid trustId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IAuditWriter audit, IOptions<FederationOptions> options, IClock clock, CancellationToken ct) =>
        Transition(workspaceId, trustId, FederationRules.StatusPaused, FederationRules.StatusActive, AuditActions.FederationResume, false, http, db, tenant, audit, options, clock, ct);

    private static Task<IResult> Revoke(
        Guid workspaceId, Guid trustId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IAuditWriter audit, IOptions<FederationOptions> options, IClock clock, CancellationToken ct) =>
        Transition(workspaceId, trustId, null, FederationRules.StatusRevoked, AuditActions.FederationRevoke, true, http, db, tenant, audit, options, clock, ct);

    private static async Task<IResult> Rotate(
        Guid workspaceId, Guid trustId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IAuditWriter audit, IOptions<FederationOptions> options, IClock clock, CancellationToken ct)
    {
        if (Off(options.Value) is { } off) return off;
        var gate = await AuthorizeAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate is null) return Results.Forbid();
        var trust = await FindTrustAsync(db, gate.Workspace.Id, trustId, ct);
        if (trust is null) return Error(StatusCodes.Status404NotFound, FederationErrors.NotFound);
        if (trust.Status is FederationRules.StatusRevoked) return Error(StatusCodes.Status409Conflict, FederationErrors.TrafficBlocked);
        if (trust.PreviousKeyId is not null) return Error(StatusCodes.Status409Conflict, FederationErrors.RotationPending);
        var bot = await db.IntegrationBots.FirstAsync(x => x.Id == trust.BotId, ct);
        var now = clock.UtcNow;
        var (raw, token) = IntegrationBotEndpoints.IssueToken(bot, now);
        db.IntegrationBotTokens.Add(token);
        trust.PreviousKeyId = trust.CurrentKeyId;
        trust.PreviousTokenId = trust.CurrentTokenId;
        trust.CurrentKeyId = FederationRules.NextKeyId(trust.CurrentKeyId);
        trust.CurrentTokenId = token.Id;
        trust.UpdatedAt = now;
        audit.Add(Audit(gate, AuditActions.FederationKeyRotate, trust, null, now));
        await db.SaveChangesAsync(ct);
        return Results.Json(View(trust, null, true, [], raw));
    }

    private static async Task<IResult> Retire(
        Guid workspaceId, Guid trustId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IAuditWriter audit, IOptions<FederationOptions> options, IClock clock, CancellationToken ct)
    {
        if (Off(options.Value) is { } off) return off;
        var gate = await AuthorizeAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate is null) return Results.Forbid();
        var trust = await FindTrustAsync(db, gate.Workspace.Id, trustId, ct);
        if (trust is null) return Error(StatusCodes.Status404NotFound, FederationErrors.NotFound);
        if (trust.PreviousTokenId is Guid previous)
        {
            var token = await db.IntegrationBotTokens.FirstOrDefaultAsync(x => x.Id == previous && x.BotId == trust.BotId, ct);
            if (token is not null && token.RevokedAt is null) token.RevokedAt = clock.UtcNow;
        }

        var now = clock.UtcNow;
        trust.PreviousKeyId = null;
        trust.PreviousTokenId = null;
        trust.UpdatedAt = now;
        audit.Add(Audit(gate, AuditActions.FederationKeyRetire, trust, null, now));
        await db.SaveChangesAsync(ct);
        return Results.Json(View(trust, null, true, [], null));
    }

    private static async Task<IResult> CreateRoom(
        Guid workspaceId, Guid trustId, FederationRoomRequest request, HttpContext http, VibeChatDbContext db,
        ITenantContext tenant, IAuditWriter audit, IOptions<FederationOptions> options, IClock clock, CancellationToken ct)
    {
        if (Off(options.Value) is { } off) return off;
        if (!TryKey(http, request.IdempotencyKey, out var key)) return Error(StatusCodes.Status400BadRequest, "IdempotencyKeyRequired");
        var gate = await AuthorizeAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate is null) return Results.Forbid();
        var trust = await FindTrustAsync(db, gate.Workspace.Id, trustId, ct);
        if (trust is null) return Error(StatusCodes.Status404NotFound, FederationErrors.NotFound);
        if (trust.Status is FederationRules.StatusRevoked or FederationRules.StatusPaused)
        {
            return Error(StatusCodes.Status409Conflict, FederationErrors.TrafficBlocked);
        }

        if (FederationRules.EgressBlocked(request.RemoteRoom) || !FederationRules.TryRoom(request.RemoteRoom, out var roomName))
        {
            return Error(StatusCodes.Status400BadRequest, FederationErrors.EnvelopeInvalid);
        }

        var channel = await db.Channels.FirstOrDefaultAsync(x => x.Id == new ChannelId(request.ChannelId) && x.WorkspaceId == gate.Workspace.Id, ct);
        if (channel is null || channel.Type is ChannelType.Direct or ChannelType.GroupDm)
        {
            return Error(StatusCodes.Status400BadRequest, FederationErrors.ChannelUnsupported);
        }

        var hash = Hash("room", trust.Id.ToString("D"), channel.Id.Value.ToString("D"), roomName);
        var replay = await db.FederationRooms.FirstOrDefaultAsync(x => x.WorkspaceId == gate.Workspace.Id && x.IdempotencyKey == key, ct);
        if (replay is not null)
        {
            return replay.RequestHash == hash
                ? Results.Json(View(trust, replay, true, [], null))
                : Error(StatusCodes.Status409Conflict, FederationErrors.IdempotencyConflict);
        }

        if (await db.FederationRooms.AnyAsync(x => x.ChannelId == channel.Id, ct))
        {
            return Error(StatusCodes.Status409Conflict, FederationErrors.ChannelTaken);
        }

        if (await db.FederationRooms.AnyAsync(x => x.TrustId == trust.Id && x.RemoteRoom == roomName, ct))
        {
            return Error(StatusCodes.Status409Conflict, FederationErrors.RoomTaken);
        }

        var now = clock.UtcNow;
        var bot = await db.IntegrationBots.FirstAsync(x => x.Id == trust.BotId, ct);
        if (!await db.IntegrationBotChannelScopes.AnyAsync(x => x.BotId == bot.Id && x.ChannelId == channel.Id, ct))
        {
            db.IntegrationBotChannelScopes.Add(new IntegrationBotChannelScope
            {
                Id = Guid.NewGuid(),
                TenantId = gate.Workspace.TenantId,
                BotId = bot.Id,
                ChannelId = channel.Id
            });
        }

        if (!await db.ChannelMembers.AnyAsync(x => x.ChannelId == channel.Id && x.UserId == bot.UserId && x.LeftAt == null, ct))
        {
            db.ChannelMembers.Add(new ChannelMember
            {
                Id = Guid.NewGuid(),
                TenantId = gate.Workspace.TenantId,
                ChannelId = channel.Id,
                UserId = bot.UserId,
                JoinedAt = now
            });
        }

        var room = new FederationRoom
        {
            Id = Guid.NewGuid(),
            TenantId = gate.Workspace.TenantId,
            WorkspaceId = gate.Workspace.Id,
            TrustId = trust.Id,
            ChannelId = channel.Id,
            RemoteRoom = roomName,
            IdempotencyKey = key,
            RequestHash = hash,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.FederationRooms.Add(room);
        audit.Add(Audit(gate, AuditActions.FederationRoomCreate, trust, room, now));
        await db.SaveChangesAsync(ct);
        return Results.Json(View(trust, room, true, [], null), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> Inbound(
        Guid roomId, FederationInboundRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IMessageWriter messages, IAuditWriter audit, IOptions<FederationOptions> options, IClock clock, CancellationToken ct)
    {
        if (Off(options.Value) is { } off) return off;
        var session = await IntegrationBotEndpoints.AuthenticateAsync(http, db, tenant, clock, ct);
        if (session is null) return Results.Unauthorized();
        var room = await db.FederationRooms.FirstOrDefaultAsync(x => x.Id == roomId && x.TenantId == session.Bot.TenantId, ct);
        if (room is null) return Error(StatusCodes.Status404NotFound, FederationErrors.NotFound);
        var trust = await db.FederationTrusts.FirstOrDefaultAsync(x => x.Id == room.TrustId, ct);
        if (trust is null) return Error(StatusCodes.Status404NotFound, FederationErrors.NotFound);
        if (trust.BotId != session.Bot.Id) return Error(StatusCodes.Status403Forbidden, FederationErrors.ScopeMismatch);
        if (!session.Bot.Enabled || !FederationRules.AllowsTraffic(trust.Status))
        {
            return Error(StatusCodes.Status409Conflict, FederationErrors.TrafficBlocked);
        }

        if (!string.Equals(request.Schema, FederationRules.EnvelopeSchema, StringComparison.Ordinal) || request.SchemaVersion != FederationRules.SchemaVersion)
        {
            return Error(StatusCodes.Status400BadRequest, FederationErrors.EnvelopeInvalid);
        }

        if (!FederationRules.TryEventId(request.EventId, out var eventId) || !FederationRules.TrySubject(request.RemoteSubject, out var subject))
        {
            return Error(StatusCodes.Status400BadRequest, FederationErrors.EnvelopeInvalid);
        }

        var eventType = (request.EventType ?? string.Empty).Trim().ToLowerInvariant();
        if (!FederationRules.Capabilities.Contains(eventType, StringComparer.Ordinal))
        {
            return Error(StatusCodes.Status400BadRequest, FederationErrors.CapabilityUnknown);
        }

        if (!FederationRules.TryKeyId(request.KeyId, out var keyId) || !KeyAccepted(trust, session.Token.Id, keyId))
        {
            return await RejectSignatureAsync(db, audit, trust, room, session.Bot.UserId, clock.UtcNow, ct);
        }

        if (!string.Equals(request.DestinationInstanceId, options.Value.InstanceId, StringComparison.Ordinal))
        {
            return Error(StatusCodes.Status400BadRequest, FederationErrors.DestinationMismatch);
        }

        if (!string.Equals(request.OriginDomain, trust.Domain, StringComparison.Ordinal))
        {
            return Error(StatusCodes.Status400BadRequest, FederationErrors.DomainMismatch);
        }

        if (!string.Equals(request.RemoteRoom, room.RemoteRoom, StringComparison.Ordinal) || request.ChannelId != room.ChannelId.Value)
        {
            return Error(StatusCodes.Status400BadRequest, FederationErrors.EnvelopeInvalid);
        }

        if (!FederationRules.ExpiryAcceptable(request.ExpiresAt, clock.UtcNow, options.Value.ClockSkewSeconds, options.Value.MaxTtlSeconds))
        {
            return Error(StatusCodes.Status400BadRequest, FederationErrors.Expired);
        }

        if (!FederationRules.TryDisplayName(request.DisplayName, out var displayName))
        {
            return Error(StatusCodes.Status400BadRequest, FederationErrors.EnvelopeInvalid);
        }

        var canonical = FederationRules.Canonical(
            eventType, eventId, request.OriginInstanceId ?? string.Empty, trust.Domain, options.Value.InstanceId,
            room.RemoteRoom, room.ChannelId.Value, subject, displayName, request.Body, request.Cursor, request.ExpiresAt, keyId, eventId);
        if (!FederationRules.SignatureMatches(request.Signature, session.Token.TokenHash, canonical))
        {
            return await RejectSignatureAsync(db, audit, trust, room, session.Bot.UserId, clock.UtcNow, ct);
        }

        var existing = await db.FederationEvents.FirstOrDefaultAsync(x => x.TrustId == trust.Id && x.RemoteEventId == eventId, ct);
        if (existing is not null)
        {
            return Results.Json(new
            {
                applied = existing.Outcome == FederationRules.Applied,
                duplicated = true,
                loop = existing.Outcome == FederationRules.Loop,
                limitation = FederationRules.DeleteLimitation,
                deleteRemoteGuaranteed = false,
                messageId = existing.LocalMessageId
            });
        }

        if (FederationRules.IsLoop(request.OriginInstanceId, options.Value.InstanceId))
        {
            AddEvent(db, trust, room, FederationRules.Inbound, eventType, eventId, null, subject, string.Empty, request.Cursor, FederationRules.Loop, clock.UtcNow);
            await db.SaveChangesAsync(ct);
            return Results.Json(new { applied = false, duplicated = false, loop = true, deleteRemoteGuaranteed = false, limitation = FederationRules.DeleteLimitation });
        }

        if (eventType == FederationRules.Delete || !trust.Capabilities.Contains(eventType, StringComparer.Ordinal))
        {
            if (eventType != FederationRules.Delete)
            {
                return Error(StatusCodes.Status409Conflict, FederationErrors.CapabilityUnknown);
            }

            AddEvent(db, trust, room, FederationRules.Inbound, eventType, eventId, null, subject, string.Empty, request.Cursor, FederationRules.Limited, clock.UtcNow);
            audit.Add(LimitationAudit(trust, session.Bot.UserId, clock.UtcNow));
            await db.SaveChangesAsync(ct);
            return Results.Json(new { applied = false, duplicated = false, limitation = FederationRules.DeleteLimitation, deleteRemoteGuaranteed = false });
        }

        if (MessageBodyPolicies.IsEmpty(request.Body) || !MessageBodyPolicies.IsWithinLimit(request.Body))
        {
            return Error(StatusCodes.Status400BadRequest, FederationErrors.EnvelopeInvalid);
        }

        await RememberIdentityAsync(db, trust, subject, displayName, clock.UtcNow, ct);
        if (request.Cursor > room.RemoteCheckpoint) room.RemoteCheckpoint = request.Cursor;
        var sent = await messages.SendAsync(new SendMessageCommand(
            trust.TenantId, session.Bot.UserId, room.ChannelId, MessageId.New(),
            "federation:" + trust.Id.ToString("N") + ":" + eventId, request.Body!.Trim(), null, null, null, AuthorIsBot: true), ct);
        AddEvent(db, trust, room, FederationRules.Inbound, eventType, eventId, sent.MessageId.Value, subject, displayName, request.Cursor, FederationRules.Applied, clock.UtcNow);
        audit.Add(InboundAudit(trust, session.Bot.UserId, clock.UtcNow));
        room.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Json(new
        {
            applied = true,
            duplicated = sent.Idempotent,
            messageId = sent.MessageId.Value,
            remote = true,
            domain = trust.Domain,
            deleteRemoteGuaranteed = false,
            limitation = FederationRules.DeleteLimitation
        }, statusCode: sent.Idempotent ? StatusCodes.Status200OK : StatusCodes.Status201Created);
    }

    private static async Task<IResult> Reconcile(
        Guid roomId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IAuditWriter audit, DlpService dlp,
        IOptions<FederationOptions> options, IClock clock, CancellationToken ct)
    {
        if (Off(options.Value) is { } off) return off;
        var session = await IntegrationBotEndpoints.AuthenticateAsync(http, db, tenant, clock, ct);
        if (session is null) return Results.Unauthorized();
        var room = await db.FederationRooms.FirstOrDefaultAsync(x => x.Id == roomId && x.TenantId == session.Bot.TenantId, ct);
        if (room is null) return Error(StatusCodes.Status404NotFound, FederationErrors.NotFound);
        var trust = await db.FederationTrusts.FirstOrDefaultAsync(x => x.Id == room.TrustId, ct);
        if (trust is null) return Error(StatusCodes.Status404NotFound, FederationErrors.NotFound);
        if (trust.BotId != session.Bot.Id) return Error(StatusCodes.Status403Forbidden, FederationErrors.ScopeMismatch);
        if (!session.Bot.Enabled || !FederationRules.AllowsTraffic(trust.Status))
        {
            return Error(StatusCodes.Status409Conflict, FederationErrors.TrafficBlocked);
        }

        if (session.Token.Id != trust.CurrentTokenId)
        {
            return Error(StatusCodes.Status401Unauthorized, FederationErrors.KeyUnknown);
        }

        var channel = await db.Channels.AsNoTracking().FirstAsync(x => x.Id == room.ChannelId, ct);
        var rows = await db.Messages.AsNoTracking()
            .Where(x => x.ConversationId == room.ChannelId && x.Sequence > room.LocalCheckpoint && x.DeletedAt == null)
            .OrderBy(x => x.Sequence)
            .Take(FederationRules.MaxPull)
            .Select(x => new OutboundRow(x.Id, x.Sequence, x.AuthorId, x.Body))
            .ToListAsync(ct);
        var inboundIds = await db.FederationEvents.AsNoTracking()
            .Where(x => x.RoomId == room.Id && x.Direction == FederationRules.Inbound && x.LocalMessageId != null)
            .Select(x => x.LocalMessageId!.Value)
            .ToListAsync(ct);
        var known = inboundIds.ToHashSet();
        var envelopes = new List<object>();
        long checkpoint = room.LocalCheckpoint;
        var expiresAt = clock.UtcNow.AddSeconds(Math.Min(options.Value.MaxTtlSeconds, 300)).ToUnixTimeSeconds();
        foreach (var row in rows)
        {
            checkpoint = row.Sequence;
            var remoteId = FederationRules.LocalEventId(row.Id.Value);
            var seen = await db.FederationEvents.AnyAsync(x => x.TrustId == trust.Id && x.RemoteEventId == remoteId, ct);
            if (seen || row.AuthorId == session.Bot.UserId || known.Contains(row.Id.Value))
            {
                continue;
            }

            var outcome = await dlp.EvaluateAsync(
                trust.TenantId, trust.WorkspaceId, channel.SpaceId, channel.Id.Value, channel.Type.ToString(),
                DlpActions.Federation, row.Body, row.Id.Value, save: true, ct);
            if (!outcome.Proceed)
            {
                AddEvent(db, trust, room, FederationRules.Outbound, FederationRules.Message, remoteId, row.Id.Value, "local", string.Empty, row.Sequence, FederationRules.Skipped, clock.UtcNow);
                audit.Add(new AuditEvent
                {
                    TenantId = trust.TenantId,
                    ActorUserId = session.Bot.UserId,
                    Action = AuditActions.FederationCopyBlocked,
                    EntityType = "FederationTrust",
                    EntityId = trust.Id.ToString(),
                    MetadataJson = JsonSerializer.Serialize(new { trustId = trust.Id, roomId = room.Id, messageId = row.Id.Value, reason = outcome.ReasonCode }),
                    OccurredAt = clock.UtcNow
                });
                continue;
            }

            var subject = "local:" + row.AuthorId.Value.ToString("N");
            var canonical = FederationRules.Canonical(
                FederationRules.Message, remoteId, options.Value.InstanceId, trust.Domain, options.Value.InstanceId,
                room.RemoteRoom, room.ChannelId.Value, subject, "local", row.Body, row.Sequence, expiresAt, trust.CurrentKeyId, remoteId);
            AddEvent(db, trust, room, FederationRules.Outbound, FederationRules.Message, remoteId, row.Id.Value, subject, string.Empty, row.Sequence, FederationRules.Applied, clock.UtcNow);
            envelopes.Add(new
            {
                schema = FederationRules.EnvelopeSchema,
                schemaVersion = FederationRules.SchemaVersion,
                eventType = FederationRules.Message,
                eventId = remoteId,
                originInstanceId = options.Value.InstanceId,
                originDomain = trust.Domain,
                destinationInstanceId = options.Value.InstanceId,
                remoteRoom = room.RemoteRoom,
                channelId = room.ChannelId.Value,
                remoteSubject = subject,
                displayName = "local",
                body = row.Body,
                cursor = row.Sequence,
                expiresAt,
                keyId = trust.CurrentKeyId,
                copyPolicy = FederationRules.CopyPolicy,
                copied = new[] { FederationRules.CopiedData },
                deleteRemoteGuaranteed = false,
                deleteLimitation = FederationRules.DeleteLimitation,
                remote = true,
                signature = FederationRules.Sign(session.Token.TokenHash, canonical)
            });
        }

        room.LocalCheckpoint = checkpoint;
        room.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Json(new
        {
            checkpoint,
            domain = trust.Domain,
            remoteInstanceId = trust.RemoteInstanceId,
            deleteRemoteGuaranteed = false,
            deleteLimitation = FederationRules.DeleteLimitation,
            envelopes
        });
    }

    private static async Task<IResult> Transition(
        Guid workspaceId, Guid trustId, string? required, string next, string action, bool revoke,
        HttpContext http, VibeChatDbContext db, ITenantContext tenant, IAuditWriter audit, IOptions<FederationOptions> options,
        IClock clock, CancellationToken ct)
    {
        if (Off(options.Value) is { } off) return off;
        var gate = await AuthorizeAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate is null) return Results.Forbid();
        var trust = await FindTrustAsync(db, gate.Workspace.Id, trustId, ct);
        if (trust is null) return Error(StatusCodes.Status404NotFound, FederationErrors.NotFound);
        if (trust.Status == next) return Results.Json(View(trust, null, true, [], null));
        if (required is not null && trust.Status != required) return Error(StatusCodes.Status409Conflict, FederationErrors.StateConflict);
        if (trust.Status == FederationRules.StatusRevoked) return Error(StatusCodes.Status409Conflict, FederationErrors.StateConflict);
        var now = clock.UtcNow;
        trust.Status = next;
        trust.UpdatedAt = now;
        if (revoke)
        {
            var bot = await db.IntegrationBots.FirstAsync(x => x.Id == trust.BotId, ct);
            bot.Enabled = false;
            await IntegrationBotEndpoints.RevokeActiveTokensAsync(db, bot.Id, now, ct);
            trust.PreviousKeyId = null;
            trust.PreviousTokenId = null;
        }

        audit.Add(Audit(gate, action, trust, null, now));
        await db.SaveChangesAsync(ct);
        return Results.Json(View(trust, null, true, [], null));
    }

    private static async Task<IResult> RejectSignatureAsync(
        VibeChatDbContext db, IAuditWriter audit, FederationTrust trust, FederationRoom room, UserId actor, DateTimeOffset now, CancellationToken ct)
    {
        audit.Add(new AuditEvent
        {
            TenantId = trust.TenantId,
            ActorUserId = actor,
            Action = AuditActions.FederationSignatureRejected,
            EntityType = "FederationTrust",
            EntityId = trust.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new { trustId = trust.Id, roomId = room.Id }),
            OccurredAt = now
        });
        await db.SaveChangesAsync(ct);
        return Error(StatusCodes.Status400BadRequest, FederationErrors.SignatureInvalid);
    }

    private static bool KeyAccepted(FederationTrust trust, Guid tokenId, string keyId)
    {
        if (tokenId == trust.CurrentTokenId && keyId == trust.CurrentKeyId) return true;
        return trust.PreviousTokenId is Guid previous
            && tokenId == previous
            && trust.PreviousKeyId is not null
            && keyId == trust.PreviousKeyId;
    }

    private static async Task RememberIdentityAsync(
        VibeChatDbContext db, FederationTrust trust, string subject, string displayName, DateTimeOffset now, CancellationToken ct)
    {
        var row = await db.FederationIdentities.FirstOrDefaultAsync(x => x.TrustId == trust.Id && x.RemoteSubject == subject, ct);
        if (row is not null)
        {
            row.DisplayName = displayName;
            return;
        }

        db.FederationIdentities.Add(new FederationIdentity
        {
            Id = Guid.NewGuid(),
            TenantId = trust.TenantId,
            WorkspaceId = trust.WorkspaceId,
            TrustId = trust.Id,
            RemoteSubject = subject,
            DisplayName = displayName,
            RemoteInstanceId = trust.RemoteInstanceId,
            LocalMarker = Guid.NewGuid(),
            CreatedAt = now
        });
    }

    private static void AddEvent(
        VibeChatDbContext db, FederationTrust trust, FederationRoom room, string direction, string eventType, string remoteEventId,
        Guid? messageId, string subject, string displayName, long cursor, string outcome, DateTimeOffset now)
    {
        db.FederationEvents.Add(new FederationEvent
        {
            Id = Guid.NewGuid(),
            TenantId = trust.TenantId,
            WorkspaceId = trust.WorkspaceId,
            TrustId = trust.Id,
            RoomId = room.Id,
            Direction = direction,
            EventType = eventType,
            RemoteEventId = remoteEventId,
            LocalMessageId = messageId,
            RemoteSubject = subject,
            DisplayName = displayName,
            Cursor = cursor,
            Outcome = outcome,
            CreatedAt = now
        });
    }

    private static (IntegrationBot Bot, string Raw, IntegrationBotToken Token) Provision(
        VibeChatDbContext db, TenantId tenantId, WorkspaceId workspaceId, string name, DateTimeOffset now)
    {
        var (bot, raw, _) = IntegrationBotEndpoints.ProvisionBot(db, tenantId, workspaceId, name, false, now);
        var token = db.IntegrationBotTokens.Local.First(x => x.BotId == bot.Id);
        return (bot, raw, token);
    }

    private static async Task<AuthGate?> AuthorizeAsync(
        Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        return workspace is null ? null : new AuthGate(workspace, profile.Id);
    }

    private static Task<FederationTrust?> FindTrustAsync(VibeChatDbContext db, WorkspaceId workspaceId, Guid trustId, CancellationToken ct) =>
        db.FederationTrusts.FirstOrDefaultAsync(x => x.Id == trustId && x.WorkspaceId == workspaceId, ct);

    private static bool TryKey(HttpContext http, string? bodyKey, out string key)
    {
        var raw = bodyKey;
        if (string.IsNullOrWhiteSpace(raw) && http.Request.Headers.TryGetValue("Idempotency-Key", out var header))
        {
            raw = header.ToString();
        }

        return PluginSdkIdempotency.TryNormalize(raw, out key);
    }

    private static string Hash(string operation, params string[] parts)
    {
        var text = operation + "|" + string.Join('|', parts);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static IResult? Off(FederationOptions options) =>
        options.Enabled ? null : Error(StatusCodes.Status404NotFound, FederationErrors.Disabled);

    private static IResult Error(int status, string code) => Results.Json(new { error = code }, statusCode: status);

    private static object View(FederationTrust trust, FederationRoom? room, bool canManage, IReadOnlyList<FederationIdentity> identities, string? token) => new
    {
        id = trust.Id,
        trustId = trust.Id,
        roomId = room?.Id,
        channelId = room?.ChannelId.Value,
        domain = trust.Domain,
        remoteInstanceId = trust.RemoteInstanceId,
        remoteRoom = room?.RemoteRoom,
        status = trust.Status,
        protocol = FederationRules.EnvelopeSchema,
        schemaVersion = FederationRules.SchemaVersion,
        capabilities = trust.Capabilities,
        currentKeyId = canManage ? trust.CurrentKeyId : null,
        previousKeyId = canManage ? trust.PreviousKeyId : null,
        consentRequired = trust.Status == FederationRules.StatusPending,
        copied = new[] { FederationRules.CopiedData },
        deleteRemoteGuaranteed = false,
        deleteLimitation = FederationRules.DeleteLimitation,
        remote = true,
        canManage,
        token,
        identities = identities.Select(row => new
        {
            remoteSubject = row.RemoteSubject,
            displayName = row.DisplayName,
            remoteInstanceId = row.RemoteInstanceId,
            remote = true
        }).ToArray()
    };

    private static AuditEvent Audit(AuthGate gate, string action, FederationTrust trust, FederationRoom? room, DateTimeOffset now) => new()
    {
        TenantId = gate.Workspace.TenantId,
        ActorUserId = gate.ActorId,
        Action = action,
        EntityType = "FederationTrust",
        EntityId = trust.Id.ToString(),
        MetadataJson = JsonSerializer.Serialize(new
        {
            trustId = trust.Id,
            roomId = room?.Id,
            domain = trust.Domain,
            remoteInstanceId = trust.RemoteInstanceId,
            status = trust.Status,
            deleteRemoteGuaranteed = false,
            limitation = FederationRules.DeleteLimitation
        }),
        OccurredAt = now
    };

    private static AuditEvent InboundAudit(FederationTrust trust, UserId actor, DateTimeOffset now) => new()
    {
        TenantId = trust.TenantId,
        ActorUserId = actor,
        Action = AuditActions.FederationInbound,
        EntityType = "FederationTrust",
        EntityId = trust.Id.ToString(),
        MetadataJson = JsonSerializer.Serialize(new { trustId = trust.Id, remote = true, domain = trust.Domain }),
        OccurredAt = now
    };

    private static AuditEvent LimitationAudit(FederationTrust trust, UserId actor, DateTimeOffset now) => new()
    {
        TenantId = trust.TenantId,
        ActorUserId = actor,
        Action = AuditActions.FederationDeleteLimited,
        EntityType = "FederationTrust",
        EntityId = trust.Id.ToString(),
        MetadataJson = JsonSerializer.Serialize(new { trustId = trust.Id, deleteRemoteGuaranteed = false, limitation = FederationRules.DeleteLimitation }),
        OccurredAt = now
    };

    private sealed record AuthGate(Workspace Workspace, UserId ActorId);
    private sealed record OutboundRow(MessageId Id, long Sequence, UserId AuthorId, string Body);

    private sealed class FederationTrustRequest
    {
        public string? Domain { get; set; }
        public string? RemoteInstanceId { get; set; }
        public string[]? Capabilities { get; set; }
        public string[]? RemoteCapabilities { get; set; }
        public int RemoteSchemaVersion { get; set; } = FederationRules.SchemaVersion;
        public string? IdempotencyKey { get; set; }
    }

    private sealed class FederationConsentRequest
    {
        public bool AcknowledgeCopy { get; set; }
    }

    private sealed class FederationRoomRequest
    {
        public Guid ChannelId { get; set; }
        public string? RemoteRoom { get; set; }
        public string? IdempotencyKey { get; set; }
    }

    private sealed class FederationInboundRequest
    {
        public string? Schema { get; set; }
        public int SchemaVersion { get; set; }
        public string? EventType { get; set; }
        public string? EventId { get; set; }
        public string? OriginInstanceId { get; set; }
        public string? OriginDomain { get; set; }
        public string? DestinationInstanceId { get; set; }
        public string? RemoteRoom { get; set; }
        public Guid ChannelId { get; set; }
        public string? RemoteSubject { get; set; }
        public string? DisplayName { get; set; }
        public string? Body { get; set; }
        public long Cursor { get; set; }
        public long ExpiresAt { get; set; }
        public string? KeyId { get; set; }
        public string? Signature { get; set; }
    }
}
