using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Api;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.Realtime;
using VibeChat.SharedKernel;

namespace VibeChat.Api.Endpoints;

internal static class LiveEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string StartHash = "start";

    internal static void MapLive(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/live/health", Health)
            .RequirePermission(Permissions.Message.Read);
        v1.MapGet("/channels/{channelId:guid}/live", Read)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/channels/{channelId:guid}/live/sessions", Start)
            .RequirePermission(Permissions.Message.Send);
        v1.MapPost("/channels/{channelId:guid}/live/sessions/{sessionId:guid}/join", Join)
            .RequirePermission(Permissions.Message.Send);
        v1.MapPost("/channels/{channelId:guid}/live/sessions/{sessionId:guid}/connect", Connect)
            .RequirePermission(Permissions.Message.Send);
        v1.MapPost("/channels/{channelId:guid}/live/sessions/{sessionId:guid}/leave", Leave)
            .RequirePermission(Permissions.Message.Send);
        v1.MapPut("/channels/{channelId:guid}/live/sessions/{sessionId:guid}/media", Media)
            .RequirePermission(Permissions.Message.Send);
        v1.MapPost("/channels/{channelId:guid}/live/sessions/{sessionId:guid}/reactions", React)
            .RequirePermission(Permissions.Message.Send);
        LiveCaptureEndpoints.Map(v1);
        LiveNotesEndpoints.Map(v1);
    }

    private static async Task<IResult> Health(
        Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, ILiveMediaProvider provider,
        IOptions<LiveOptions> options, IOptions<MeetingAiOptions> meeting, IPermissionChecker permissions, IClock clock, CancellationToken ct)
    {
        if (Disabled(options.Value) is { } off)
        {
            return off;
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var status = await provider.StatusAsync(ct);
        var active = await db.LiveSessions.CountAsync(
            x => x.WorkspaceId == workspace.Id && x.State == LiveRules.StateActive, ct);
        var shareOn = await LiveCaptureEndpoints.ShareEnabledAsync(db, workspace.Id, options.Value, ct);
        var recordOn = await LiveCaptureEndpoints.RecordingEnabledAsync(db, workspace.Id, options.Value, ct);
        var notesOn = await LiveNotesEndpoints.EnabledAsync(db, workspace.Id, meeting.Value, ct);
        var canRecord = recordOn && await permissions.HasPermissionAsync(workspace.TenantId, profile.Id, Permissions.Live.Record, ct);
        return Results.Ok(new
        {
            enabled = true,
            provider = provider.Name,
            healthy = status.Healthy,
            videoEnabled = options.Value.VideoEnabled,
            shareEnabled = shareOn,
            recordingEnabled = recordOn,
            canShare = shareOn && await permissions.HasPermissionAsync(workspace.TenantId, profile.Id, Permissions.Live.Share, ct),
            canRecord,
            transcriptionEnabled = notesOn,
            canTranscribe = notesOn && canRecord,
            activeSessions = active,
            maxSessions = LiveRules.SessionLimit(options.Value.MaxSessionsPerWorkspace),
            maxParticipants = LiveRules.ParticipantLimit(options.Value.MaxParticipants),
            metrics = new[]
            {
                "vibechat.live.sessions",
                "vibechat.live.joins",
                "vibechat.live.rejected",
                "vibechat.live.participants",
                "vibechat.live.transcriptions"
            }
        });
    }

    private static async Task<IResult> Read(
        Guid channelId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IOptions<LiveOptions> options, IClock clock, CancellationToken ct)
    {
        if (Disabled(options.Value) is { } off)
        {
            return off;
        }

        var gate = await OpenAsync(channelId, http, db, tenant, clock, ct);
        if (gate is null)
        {
            return Results.Forbid();
        }

        var session = await ActiveAsync(db, gate.Value.Channel.Id, ct);
        if (session is null)
        {
            return Error(StatusCodes.Status404NotFound, LiveErrors.NotFound);
        }

        return await ViewAsync(db, session, StatusCodes.Status200OK, ct);
    }

    private static async Task<IResult> Start(
        Guid channelId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, ILiveMediaProvider provider,
        IOptions<LiveOptions> options, IAuditWriter audit, IOutboxWriter outbox, IClock clock, CancellationToken ct)
    {
        if (Disabled(options.Value) is { } off)
        {
            return off;
        }

        if (!TryKey(http, out var key))
        {
            return Error(StatusCodes.Status400BadRequest, LiveErrors.IdempotencyRequired);
        }

        var gate = await OpenAsync(channelId, http, db, tenant, clock, ct);
        if (gate is null)
        {
            return Results.Forbid();
        }

        if (!await ReadyAsync(provider, ct))
        {
            VibeChatMetrics.LiveRejected.Add(1);
            return Error(StatusCodes.Status503ServiceUnavailable, LiveErrors.ProviderUnavailable);
        }

        var (profile, channel) = gate.Value;
        var replay = await db.LiveSessions.FirstOrDefaultAsync(
            x => x.ChannelId == channel.Id && x.IdempotencyKey == key, ct);
        if (replay is not null)
        {
            return replay.RequestHash == StartHash
                ? await ViewAsync(db, replay, StatusCodes.Status200OK, ct)
                : Error(StatusCodes.Status409Conflict, LiveErrors.IdempotencyConflict);
        }

        var active = await ActiveAsync(db, channel.Id, ct);
        if (active is not null)
        {
            return await ViewAsync(db, active, StatusCodes.Status200OK, ct);
        }

        var sessionLimit = LiveRules.SessionLimit(options.Value.MaxSessionsPerWorkspace);
        var sessions = await db.LiveSessions.CountAsync(
            x => x.WorkspaceId == channel.WorkspaceId && x.State == LiveRules.StateActive, ct);
        if (!LiveRules.HasCapacity(sessions, sessionLimit))
        {
            VibeChatMetrics.LiveRejected.Add(1);
            audit.Add(Audit(channel.TenantId, profile.Id, AuditActions.LiveCapacityDenied, null, clock.UtcNow));
            await db.SaveChangesAsync(ct);
            return Error(StatusCodes.Status429TooManyRequests, LiveErrors.Capacity);
        }

        var now = clock.UtcNow;
        var session = new LiveSession
        {
            Id = Guid.NewGuid(),
            TenantId = channel.TenantId,
            WorkspaceId = channel.WorkspaceId,
            ChannelId = channel.Id,
            State = LiveRules.StateActive,
            VideoAllowed = options.Value.VideoEnabled,
            CreatedBy = profile.Id,
            IdempotencyKey = key,
            RequestHash = StartHash,
            CreatedAt = now
        };
        db.LiveSessions.Add(session);
        audit.Add(Audit(channel.TenantId, profile.Id, AuditActions.LiveSessionStart, session.Id, now));
        Publish(outbox, session, 0, now);
        VibeChatMetrics.LiveSessions.Add(1);
        await db.SaveChangesAsync(ct);
        return await ViewAsync(db, session, StatusCodes.Status201Created, ct);
    }

    private static async Task<IResult> Join(
        Guid channelId, Guid sessionId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        ILiveMediaProvider provider, IOptions<LiveOptions> options, IAuditWriter audit, IOutboxWriter outbox,
        IClock clock, CancellationToken ct)
    {
        if (Disabled(options.Value) is { } off)
        {
            return off;
        }

        var gate = await OpenAsync(channelId, http, db, tenant, clock, ct);
        if (gate is null)
        {
            return Results.Forbid();
        }

        var session = await FindSessionAsync(db, gate.Value.Channel.Id, sessionId, ct);
        if (session is null)
        {
            return Error(StatusCodes.Status404NotFound, LiveErrors.NotFound);
        }

        if (session.State != LiveRules.StateActive)
        {
            return Error(StatusCodes.Status409Conflict, LiveErrors.Ended);
        }

        if (!await ReadyAsync(provider, ct))
        {
            VibeChatMetrics.LiveRejected.Add(1);
            return Error(StatusCodes.Status503ServiceUnavailable, LiveErrors.ProviderUnavailable);
        }

        var (profile, channel) = gate.Value;
        var people = await db.LiveParticipants.Where(x => x.SessionId == session.Id && x.LeftAt == null).ToListAsync(ct);
        var self = await db.LiveParticipants.FirstOrDefaultAsync(
            x => x.SessionId == session.Id && x.UserId == profile.Id, ct);
        var joining = self is null || self.LeftAt is not null;
        if (joining && !LiveRules.HasCapacity(people.Count, LiveRules.ParticipantLimit(options.Value.MaxParticipants)))
        {
            VibeChatMetrics.LiveRejected.Add(1);
            audit.Add(Audit(channel.TenantId, profile.Id, AuditActions.LiveCapacityDenied, session.Id, clock.UtcNow));
            await db.SaveChangesAsync(ct);
            return Error(StatusCodes.Status429TooManyRequests, LiveErrors.Capacity);
        }

        var now = clock.UtcNow;
        if (self is null)
        {
            self = new LiveParticipant
            {
                Id = Guid.NewGuid(),
                TenantId = channel.TenantId,
                SessionId = session.Id,
                UserId = profile.Id,
                JoinedAt = now
            };
            db.LiveParticipants.Add(self);
        }
        else if (self.LeftAt is not null)
        {
            self.LeftAt = null;
            self.JoinedAt = now;
            self.AudioConnected = false;
            self.CameraOn = false;
            self.Muted = false;
        }

        if (joining)
        {
            VibeChatMetrics.LiveParticipants.Add(1);
            VibeChatMetrics.LiveJoins.Add(1);
            audit.Add(Audit(channel.TenantId, profile.Id, AuditActions.LiveParticipantJoin, session.Id, now));
            await LiveNotesEndpoints.OnParticipantJoinedAsync(db, session, profile.Id, ct);
        }

        var room = LiveRules.Room(channel.TenantId, channel.Id, session.Id);
        var open = await db.LiveJoinTokens
            .Where(x => x.SessionId == session.Id && x.UserId == profile.Id && x.ConsumedAt == null)
            .ToListAsync(ct);
        foreach (var previous in open)
        {
            previous.ConsumedAt = now;
        }

        var shareOn = await LiveCaptureEndpoints.ShareEnabledAsync(db, channel.WorkspaceId, options.Value, ct);
        if (!shareOn)
        {
            self.SharingScreen = false;
        }

        var blocked = await LiveCaptureEndpoints.PublishBlockedAsync(db, session.Id, profile.Id, ct);
        var screen = shareOn && self.SharingScreen && !blocked;
        LiveAccessToken issued;
        try
        {
            issued = provider.Issue(new LiveTokenRequest(
                room,
                profile.Id.Value.ToString("N"),
                !blocked,
                session.VideoAllowed && !blocked,
                now.AddSeconds(LiveRules.TokenTtl(options.Value.TokenTtlSeconds)),
                Guid.NewGuid().ToString("N"),
                screen));
        }
        catch (InvalidOperationException)
        {
            VibeChatMetrics.LiveRejected.Add(1);
            return Error(StatusCodes.Status503ServiceUnavailable, LiveErrors.ProviderUnavailable);
        }

        db.LiveJoinTokens.Add(new LiveJoinToken
        {
            Id = Guid.NewGuid(),
            TenantId = channel.TenantId,
            SessionId = session.Id,
            UserId = profile.Id,
            TokenHash = LiveRules.HashToken(issued.Token),
            Room = room,
            ExpiresAt = issued.ExpiresAt
        });
        var count = people.Count + (joining ? 1 : 0);
        Publish(outbox, session, count, now);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new
        {
            sessionId = session.Id,
            token = issued.Token,
            expiresAt = issued.ExpiresAt,
            room = issued.Room,
            signalUrl = issued.SignalUrl,
            canPublishAudio = !blocked,
            canPublishVideo = session.VideoAllowed && !blocked,
            canPublishScreen = screen,
            recordingNotice = blocked
        });
    }

    private static async Task<IResult> Connect(
        Guid channelId, Guid sessionId, LiveConnectRequest request, HttpContext http, VibeChatDbContext db,
        ITenantContext tenant, ILiveMediaProvider provider, IOptions<LiveOptions> options, IAuditWriter audit,
        IOutboxWriter outbox, IClock clock, CancellationToken ct)
    {
        if (Disabled(options.Value) is { } off)
        {
            return off;
        }

        var gate = await OpenAsync(channelId, http, db, tenant, clock, ct);
        if (gate is null)
        {
            return Results.Forbid();
        }

        var session = await FindSessionAsync(db, gate.Value.Channel.Id, sessionId, ct);
        if (session is null || session.State != LiveRules.StateActive)
        {
            return Error(StatusCodes.Status404NotFound, LiveErrors.NotFound);
        }

        var token = request.Token?.Trim() ?? "";
        var hash = LiveRules.HashToken(token);
        var stored = await db.LiveJoinTokens.FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (stored is null)
        {
            return Error(StatusCodes.Status403Forbidden, LiveErrors.TokenInvalid);
        }

        var now = clock.UtcNow;
        if (stored.ConsumedAt is not null)
        {
            VibeChatMetrics.LiveRejected.Add(1);
            audit.Add(Audit(session.TenantId, gate.Value.Profile.Id, AuditActions.LiveTokenReplay, session.Id, now));
            await db.SaveChangesAsync(ct);
            return Error(StatusCodes.Status403Forbidden, LiveErrors.TokenReplayed);
        }

        if (stored.ExpiresAt <= now)
        {
            return Error(StatusCodes.Status403Forbidden, LiveErrors.TokenExpired);
        }

        var claims = provider.Read(token);
        var room = LiveRules.Room(session.TenantId, session.ChannelId, session.Id);
        if (claims is null
            || !string.Equals(claims.Identity, gate.Value.Profile.Id.Value.ToString("N"), StringComparison.Ordinal)
            || stored.UserId != gate.Value.Profile.Id)
        {
            return Error(StatusCodes.Status403Forbidden, LiveErrors.TokenInvalid);
        }

        if (stored.SessionId != session.Id || !LiveRules.RoomMatches(claims.Room, room))
        {
            return Error(StatusCodes.Status403Forbidden, LiveErrors.RoomMismatch);
        }

        var self = await db.LiveParticipants.FirstOrDefaultAsync(
            x => x.SessionId == session.Id && x.UserId == gate.Value.Profile.Id && x.LeftAt == null, ct);
        if (self is null)
        {
            return Results.Forbid();
        }

        stored.ConsumedAt = now;
        self.AudioConnected = claims.CanPublishAudio;
        if (!session.VideoAllowed)
        {
            self.CameraOn = false;
        }

        var count = await db.LiveParticipants.CountAsync(x => x.SessionId == session.Id && x.LeftAt == null, ct);
        Publish(outbox, session, count, now);
        await db.SaveChangesAsync(ct);
        return await ViewAsync(db, session, StatusCodes.Status200OK, ct);
    }

    private static async Task<IResult> Leave(
        Guid channelId, Guid sessionId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IOptions<LiveOptions> options, ILiveRecordingSink sink, IAuditWriter audit, IOutboxWriter outbox, IClock clock,
        CancellationToken ct)
    {
        if (Disabled(options.Value) is { } off)
        {
            return off;
        }

        var gate = await OpenAsync(channelId, http, db, tenant, clock, ct);
        if (gate is null)
        {
            return Results.Forbid();
        }

        var session = await FindSessionAsync(db, gate.Value.Channel.Id, sessionId, ct);
        if (session is null)
        {
            return Error(StatusCodes.Status404NotFound, LiveErrors.NotFound);
        }

        var self = await db.LiveParticipants.FirstOrDefaultAsync(
            x => x.SessionId == session.Id && x.UserId == gate.Value.Profile.Id, ct);
        var now = clock.UtcNow;
        var departed = false;
        if (self is not null && self.LeftAt is null)
        {
            self.LeftAt = now;
            self.AudioConnected = false;
            self.CameraOn = false;
            self.SharingScreen = false;
            VibeChatMetrics.LiveParticipants.Add(-1);
            audit.Add(Audit(session.TenantId, gate.Value.Profile.Id, AuditActions.LiveParticipantLeave, session.Id, now));
            departed = true;
        }

        var remaining = await db.LiveParticipants.CountAsync(
            x => x.SessionId == session.Id && x.LeftAt == null && (self == null || x.Id != self.Id), ct);
        var ended = false;
        if (remaining == 0 && session.State == LiveRules.StateActive)
        {
            session.State = LiveRules.StateEnded;
            session.EndedAt = now;
            ended = true;
        }

        if (departed || ended)
        {
            await LiveCaptureEndpoints.OnParticipantLeftAsync(
                db, session, self, ended, now, sink, options, audit, outbox, ct);
            await LiveNotesEndpoints.OnParticipantLeftAsync(db, session, self, ended, now, outbox, ct);
            Publish(outbox, session, remaining, now);
            await db.SaveChangesAsync(ct);
        }

        return await ViewAsync(db, session, StatusCodes.Status200OK, ct);
    }

    private static async Task<IResult> Media(
        Guid channelId, Guid sessionId, LiveMediaRequest request, HttpContext http, VibeChatDbContext db,
        ITenantContext tenant, IOptions<LiveOptions> options, IOutboxWriter outbox, IClock clock, CancellationToken ct)
    {
        if (Disabled(options.Value) is { } off)
        {
            return off;
        }

        var gate = await OpenAsync(channelId, http, db, tenant, clock, ct);
        if (gate is null)
        {
            return Results.Forbid();
        }

        var session = await FindSessionAsync(db, gate.Value.Channel.Id, sessionId, ct);
        if (session is null || session.State != LiveRules.StateActive)
        {
            return Error(StatusCodes.Status404NotFound, LiveErrors.NotFound);
        }

        if (request.Camera == true && !session.VideoAllowed)
        {
            return Error(StatusCodes.Status403Forbidden, LiveErrors.VideoDisabled);
        }

        var self = await db.LiveParticipants.FirstOrDefaultAsync(
            x => x.SessionId == session.Id && x.UserId == gate.Value.Profile.Id && x.LeftAt == null, ct);
        if (self is null)
        {
            return Results.Forbid();
        }

        if (request.Muted is not null)
        {
            self.Muted = request.Muted.Value;
        }

        if (request.Camera is not null)
        {
            self.CameraOn = request.Camera.Value && session.VideoAllowed;
        }

        var now = clock.UtcNow;
        var count = await db.LiveParticipants.CountAsync(x => x.SessionId == session.Id && x.LeftAt == null, ct);
        Publish(outbox, session, count, now);
        await db.SaveChangesAsync(ct);
        return await ViewAsync(db, session, StatusCodes.Status200OK, ct);
    }

    private static async Task<IResult> React(
        Guid channelId, Guid sessionId, LiveReactionRequest request, HttpContext http, VibeChatDbContext db,
        ITenantContext tenant, IOptions<LiveOptions> options, IOutboxWriter outbox, IClock clock, CancellationToken ct)
    {
        if (Disabled(options.Value) is { } off)
        {
            return off;
        }

        if (!LiveRules.TryReaction(request.Emoji, out var reaction))
        {
            return Error(StatusCodes.Status400BadRequest, LiveErrors.ReactionInvalid);
        }

        var gate = await OpenAsync(channelId, http, db, tenant, clock, ct);
        if (gate is null)
        {
            return Results.Forbid();
        }

        var session = await FindSessionAsync(db, gate.Value.Channel.Id, sessionId, ct);
        if (session is null || session.State != LiveRules.StateActive)
        {
            return Error(StatusCodes.Status404NotFound, LiveErrors.NotFound);
        }

        var self = await db.LiveParticipants.FirstOrDefaultAsync(
            x => x.SessionId == session.Id && x.UserId == gate.Value.Profile.Id && x.LeftAt == null, ct);
        if (self is null)
        {
            return Results.Forbid();
        }

        self.Reaction = reaction;
        var now = clock.UtcNow;
        var count = await db.LiveParticipants.CountAsync(x => x.SessionId == session.Id && x.LeftAt == null, ct);
        Publish(outbox, session, count, now);
        await db.SaveChangesAsync(ct);
        return await ViewAsync(db, session, StatusCodes.Status200OK, ct);
    }

    private static async Task<(UserProfile Profile, VibeChat.Conversations.Channel Channel)?> OpenAsync(
        Guid channelId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var channel = await RequestAuth.ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
        return channel is null ? null : (profile, channel);
    }

    private static Task<LiveSession?> ActiveAsync(VibeChatDbContext db, ChannelId channelId, CancellationToken ct) =>
        db.LiveSessions.FirstOrDefaultAsync(x => x.ChannelId == channelId && x.State == LiveRules.StateActive, ct);

    private static Task<LiveSession?> FindSessionAsync(VibeChatDbContext db, ChannelId channelId, Guid sessionId, CancellationToken ct) =>
        db.LiveSessions.FirstOrDefaultAsync(x => x.Id == sessionId && x.ChannelId == channelId, ct);

    private static async Task<bool> ReadyAsync(ILiveMediaProvider provider, CancellationToken ct)
    {
        var status = await provider.StatusAsync(ct);
        return status.Healthy && provider.Name is LiveRules.ProviderDev or LiveRules.ProviderLiveKit;
    }

    private static async Task<IResult> ViewAsync(VibeChatDbContext db, LiveSession session, int status, CancellationToken ct)
    {
        var rows = await db.LiveParticipants.AsNoTracking()
            .Where(x => x.SessionId == session.Id && x.LeftAt == null)
            .OrderBy(x => x.JoinedAt)
            .ToListAsync(ct);
        var ids = rows.Select(x => x.UserId).ToArray();
        var names = await db.UserProfiles.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        var people = rows.Select(row => new
        {
            userId = row.UserId.Value,
            displayName = names.TryGetValue(row.UserId, out var name) ? name : "",
            muted = row.Muted,
            cameraOn = row.CameraOn,
            audioConnected = row.AudioConnected,
            sharingScreen = row.SharingScreen,
            reaction = row.Reaction
        }).ToArray();
        return Results.Json(new
        {
            sessionId = session.Id,
            state = session.State,
            videoAllowed = session.VideoAllowed,
            participants = people,
            recording = await LiveCaptureEndpoints.OpenViewAsync(db, session.Id, ct),
            transcript = await LiveNotesEndpoints.OpenViewAsync(db, session.Id, ct)
        }, statusCode: status);
    }

    private static void Publish(IOutboxWriter outbox, LiveSession session, int participantCount, DateTimeOffset now)
    {
        outbox.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = session.TenantId,
            Type = LiveRules.EventType,
            Payload = JsonSerializer.Serialize(new LiveSessionEvent(
                LiveRules.SchemaVersion,
                session.TenantId.Value,
                session.Id,
                session.ChannelId.Value,
                session.State,
                participantCount,
                session.VideoAllowed), Json),
            OccurredAt = now
        });
    }

    private static AuditEvent Audit(TenantId tenantId, UserId actor, string action, Guid? sessionId, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorUserId = actor,
            Action = action,
            EntityType = "live.session",
            EntityId = sessionId?.ToString("D"),
            MetadataJson = "{}",
            OccurredAt = now
        };

    private static bool TryKey(HttpContext http, out string key)
    {
        key = "";
        if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1)
        {
            return false;
        }

        key = keys[0]?.Trim() ?? "";
        return key.Length is > 0 and <= 200;
    }

    private static IResult? Disabled(LiveOptions options) =>
        options.Enabled ? null : Error(StatusCodes.Status404NotFound, LiveErrors.Disabled);

    private static IResult Error(int status, string code) =>
        Results.Json(new { error = code }, statusCode: status);

    private sealed record LiveSessionEvent(
        int SchemaVersion,
        Guid TenantId,
        Guid SessionId,
        Guid ChannelId,
        string State,
        int ParticipantCount,
        bool VideoAllowed);
}

internal sealed class LiveConnectRequest
{
    public string? Token { get; set; }
}

internal sealed class LiveMediaRequest
{
    public bool? Muted { get; set; }

    public bool? Camera { get; set; }
}

internal sealed class LiveReactionRequest
{
    public string? Emoji { get; set; }
}
