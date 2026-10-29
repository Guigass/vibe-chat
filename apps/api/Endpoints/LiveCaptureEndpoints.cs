using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Files;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.Realtime;
using VibeChat.SharedKernel;

namespace VibeChat.Api.Endpoints;

internal static class LiveCaptureEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] OpenStates = [LiveRules.RecordingConsent, LiveRules.RecordingActive];

    internal static void Map(RouteGroupBuilder v1)
    {
        v1.MapPut("/workspaces/{workspaceId:guid}/live/policy", Policy)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/channels/{channelId:guid}/live/sessions/{sessionId:guid}/share", StartShare)
            .RequirePermission(Permissions.Live.Share);
        v1.MapPost("/channels/{channelId:guid}/live/sessions/{sessionId:guid}/share/stop", StopShare)
            .RequirePermission(Permissions.Live.Share);
        v1.MapPost("/channels/{channelId:guid}/live/sessions/{sessionId:guid}/share/revoke", RevokeShare)
            .RequirePermission(Permissions.Live.Record);
        v1.MapPost("/channels/{channelId:guid}/live/sessions/{sessionId:guid}/recordings", RequestRecording)
            .RequirePermission(Permissions.Live.Record);
        v1.MapPost("/channels/{channelId:guid}/live/sessions/{sessionId:guid}/recordings/{recordingId:guid}/consent", Consent)
            .RequirePermission(Permissions.Message.Send);
        v1.MapPost("/channels/{channelId:guid}/live/sessions/{sessionId:guid}/recordings/{recordingId:guid}/stop", StopRecording)
            .RequirePermission(Permissions.Live.Record);
        v1.MapGet("/channels/{channelId:guid}/live/sessions/{sessionId:guid}/recordings/{recordingId:guid}", ReadRecording)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/channels/{channelId:guid}/live/sessions/{sessionId:guid}/recordings/{recordingId:guid}/download", Download)
            .RequirePermission(Permissions.Message.Read);
    }

    internal static async Task<bool> ShareEnabledAsync(
        VibeChatDbContext db, WorkspaceId workspaceId, LiveOptions options, CancellationToken ct) =>
        LiveRules.FeatureOn(options.ShareEnabled, await WorkspaceFlagAsync(db, workspaceId, share: true, ct));

    internal static async Task<bool> RecordingEnabledAsync(
        VibeChatDbContext db, WorkspaceId workspaceId, LiveOptions options, CancellationToken ct) =>
        LiveRules.FeatureOn(options.RecordingEnabled, await WorkspaceFlagAsync(db, workspaceId, share: false, ct));

    internal static async Task<bool> PublishBlockedAsync(
        VibeChatDbContext db, Guid sessionId, UserId userId, CancellationToken ct)
    {
        var recording = await OpenRecordingAsync(db, sessionId, ct);
        if (recording is null || recording.State != LiveRules.RecordingActive)
        {
            return false;
        }

        var decision = await db.LiveRecordingConsents.AsNoTracking()
            .Where(x => x.RecordingId == recording.Id && x.UserId == userId)
            .Select(x => x.Decision)
            .FirstOrDefaultAsync(ct);
        return decision != LiveRules.DecisionAccepted;
    }

    internal static async Task OnParticipantLeftAsync(
        VibeChatDbContext db, LiveSession session, LiveParticipant? self, bool ended, DateTimeOffset now,
        ILiveRecordingSink sink, IOptions<LiveOptions> options, IAuditWriter audit, IOutboxWriter outbox, CancellationToken ct)
    {
        if (self is not null)
        {
            self.SharingScreen = false;
        }

        var recording = await OpenRecordingAsync(db, session.Id, ct);
        if (recording is null)
        {
            return;
        }

        var actor = self?.UserId ?? recording.StartedBy;
        if (recording.State == LiveRules.RecordingConsent)
        {
            var rows = await db.LiveRecordingConsents.Where(x => x.RecordingId == recording.Id).ToListAsync(ct);
            if (self is not null)
            {
                var row = rows.FirstOrDefault(x => x.UserId == self.UserId);
                if (row is not null)
                {
                    rows.Remove(row);
                    db.LiveRecordingConsents.Remove(row);
                }
            }

            if (ended)
            {
                recording.State = LiveRules.RecordingDeclined;
                recording.StoppedAt = now;
                Publish(outbox, recording, actor, now);
                return;
            }

            var next = LiveRules.NextRecordingState(rows.Select(x => x.Decision));
            if (next != recording.State)
            {
                recording.State = next;
                if (next == LiveRules.RecordingActive)
                {
                    recording.StartedAt = now;
                    audit.Add(Audit(recording.TenantId, actor, AuditActions.LiveRecordingStart, recording.Id, now));
                }
            }

            Publish(outbox, recording, actor, now);
            return;
        }

        if (ended && recording.State == LiveRules.RecordingActive)
        {
            await SealAsync(db, recording, actor, now, sink, options.Value, audit, outbox, ct);
        }
    }

    internal static async Task<object?> OpenViewAsync(VibeChatDbContext db, Guid sessionId, CancellationToken ct)
    {
        var recording = await OpenRecordingAsync(db, sessionId, ct);
        return recording is null ? null : await ViewObjectAsync(db, recording, ct);
    }

    private static async Task<IResult> Policy(
        Guid workspaceId, LivePolicyRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IOptions<LiveOptions> options, IClock clock, CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Error(StatusCodes.Status404NotFound, LiveErrors.Disabled);
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var now = clock.UtcNow;
        var policy = await db.LiveWorkspacePolicies.FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id, ct);
        if (policy is null)
        {
            policy = new LiveWorkspacePolicy
            {
                Id = Guid.NewGuid(),
                TenantId = workspace.TenantId,
                WorkspaceId = workspace.Id
            };
            db.LiveWorkspacePolicies.Add(policy);
        }

        policy.ShareEnabled = request.ShareEnabled;
        policy.RecordingEnabled = request.RecordingEnabled;
        policy.UpdatedBy = profile.Id;
        policy.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new
        {
            shareEnabled = LiveRules.FeatureOn(options.Value.ShareEnabled, policy.ShareEnabled),
            recordingEnabled = LiveRules.FeatureOn(options.Value.RecordingEnabled, policy.RecordingEnabled)
        });
    }

    private static async Task<IResult> StartShare(
        Guid channelId, Guid sessionId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IOptions<LiveOptions> options, IAuditWriter audit, IOutboxWriter outbox, IClock clock, CancellationToken ct)
    {
        var gate = await GateAsync(channelId, sessionId, http, db, tenant, options.Value, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        if (!await ShareEnabledAsync(db, gate.Session!.WorkspaceId, options.Value, ct))
        {
            return Error(StatusCodes.Status403Forbidden, LiveErrors.ShareDisabled);
        }

        var self = await ParticipantAsync(db, gate.Session.Id, gate.Profile!.Id, ct);
        if (self is null)
        {
            return Results.Forbid();
        }

        var now = clock.UtcNow;
        if (!self.SharingScreen)
        {
            self.SharingScreen = true;
            audit.Add(Audit(gate.Session.TenantId, gate.Profile.Id, AuditActions.LiveShareStart, gate.Session.Id, now));
            PublishSession(outbox, gate.Session, now);
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(new { sharing = true, userId = self.UserId.Value });
    }

    private static async Task<IResult> StopShare(
        Guid channelId, Guid sessionId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IOptions<LiveOptions> options, IAuditWriter audit, IOutboxWriter outbox, IClock clock, CancellationToken ct)
    {
        var gate = await GateAsync(channelId, sessionId, http, db, tenant, options.Value, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var self = await ParticipantAsync(db, gate.Session!.Id, gate.Profile!.Id, ct);
        if (self is null)
        {
            return Results.Forbid();
        }

        return await ClearShareAsync(db, gate.Session, self, gate.Profile.Id, AuditActions.LiveShareStop, clock.UtcNow, audit, outbox, ct);
    }

    private static async Task<IResult> RevokeShare(
        Guid channelId, Guid sessionId, LiveRevokeRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IOptions<LiveOptions> options, IAuditWriter audit, IOutboxWriter outbox, IClock clock, CancellationToken ct)
    {
        var gate = await GateAsync(channelId, sessionId, http, db, tenant, options.Value, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var target = await ParticipantAsync(db, gate.Session!.Id, new UserId(request.UserId), ct);
        if (target is null)
        {
            return Error(StatusCodes.Status404NotFound, LiveErrors.NotFound);
        }

        return await ClearShareAsync(db, gate.Session, target, gate.Profile!.Id, AuditActions.LiveShareRevoke, clock.UtcNow, audit, outbox, ct);
    }

    private static async Task<IResult> RequestRecording(
        Guid channelId, Guid sessionId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IOptions<LiveOptions> options, IAuditWriter audit, IOutboxWriter outbox, IClock clock, CancellationToken ct)
    {
        if (!TryKey(http, out var key))
        {
            return Error(StatusCodes.Status400BadRequest, LiveErrors.IdempotencyRequired);
        }

        var gate = await GateAsync(channelId, sessionId, http, db, tenant, options.Value, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        if (!await RecordingEnabledAsync(db, gate.Session!.WorkspaceId, options.Value, ct))
        {
            return Error(StatusCodes.Status403Forbidden, LiveErrors.RecordingDisabled);
        }

        var replay = await db.LiveRecordings.FirstOrDefaultAsync(
            x => x.SessionId == gate.Session.Id && x.IdempotencyKey == key, ct);
        if (replay is not null)
        {
            return replay.RequestHash == LiveRules.RecordHash
                ? Results.Ok(await ViewObjectAsync(db, replay, ct))
                : Error(StatusCodes.Status409Conflict, LiveErrors.IdempotencyConflict);
        }

        var open = await OpenRecordingAsync(db, gate.Session.Id, ct);
        if (open is not null)
        {
            return Results.Ok(await ViewObjectAsync(db, open, ct));
        }

        var people = await db.LiveParticipants.Where(x => x.SessionId == gate.Session.Id && x.LeftAt == null).ToListAsync(ct);
        if (people.Count == 0)
        {
            return Error(StatusCodes.Status409Conflict, LiveErrors.RecordingNotReady);
        }

        var now = clock.UtcNow;
        var recording = new LiveRecording
        {
            Id = Guid.NewGuid(),
            TenantId = gate.Session.TenantId,
            WorkspaceId = gate.Session.WorkspaceId,
            ChannelId = gate.Session.ChannelId,
            SessionId = gate.Session.Id,
            State = LiveRules.RecordingConsent,
            StartedBy = gate.Profile!.Id,
            IdempotencyKey = key,
            RequestHash = LiveRules.RecordHash,
            ScanStatus = LiveRules.ScanPending,
            CreatedAt = now
        };
        db.LiveRecordings.Add(recording);
        foreach (var person in people)
        {
            db.LiveRecordingConsents.Add(new LiveRecordingConsent
            {
                Id = Guid.NewGuid(),
                TenantId = gate.Session.TenantId,
                RecordingId = recording.Id,
                UserId = person.UserId,
                Decision = LiveRules.DecisionPending
            });
        }

        audit.Add(Audit(gate.Session.TenantId, gate.Profile.Id, AuditActions.LiveRecordingRequest, recording.Id, now));
        Publish(outbox, recording, gate.Profile.Id, now);
        await db.SaveChangesAsync(ct);
        return Results.Json(await ViewObjectAsync(db, recording, ct), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> Consent(
        Guid channelId, Guid sessionId, Guid recordingId, LiveConsentRequest request, HttpContext http, VibeChatDbContext db,
        ITenantContext tenant, IOptions<LiveOptions> options, IAuditWriter audit, IOutboxWriter outbox, IClock clock, CancellationToken ct)
    {
        var decision = (request.Decision ?? "").Trim().ToLowerInvariant() switch
        {
            "accept" or "accepted" => LiveRules.DecisionAccepted,
            "decline" or "declined" => LiveRules.DecisionDeclined,
            _ => ""
        };
        if (decision.Length == 0)
        {
            return Error(StatusCodes.Status400BadRequest, LiveErrors.DecisionInvalid);
        }

        var gate = await GateAsync(channelId, sessionId, http, db, tenant, options.Value, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var recording = await FindRecordingAsync(db, gate.Session!.Id, recordingId, ct);
        if (recording is null)
        {
            return Error(StatusCodes.Status404NotFound, LiveErrors.NotFound);
        }

        if (recording.State != LiveRules.RecordingConsent)
        {
            return Results.Ok(await ViewObjectAsync(db, recording, ct));
        }

        var self = await ParticipantAsync(db, gate.Session.Id, gate.Profile!.Id, ct);
        if (self is null)
        {
            return Results.Forbid();
        }

        var row = await db.LiveRecordingConsents.FirstOrDefaultAsync(
            x => x.RecordingId == recording.Id && x.UserId == gate.Profile.Id, ct);
        var now = clock.UtcNow;
        if (row is null)
        {
            row = new LiveRecordingConsent
            {
                Id = Guid.NewGuid(),
                TenantId = gate.Session.TenantId,
                RecordingId = recording.Id,
                UserId = gate.Profile.Id
            };
            db.LiveRecordingConsents.Add(row);
        }

        row.Decision = decision;
        row.DecidedAt = now;
        audit.Add(Audit(gate.Session.TenantId, gate.Profile.Id, AuditActions.LiveRecordingConsent, recording.Id, now));
        await ApplyDecisionsAsync(db, recording, gate.Profile.Id, now, audit, outbox, ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ViewObjectAsync(db, recording, ct));
    }

    private static async Task<IResult> StopRecording(
        Guid channelId, Guid sessionId, Guid recordingId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IOptions<LiveOptions> options, ILiveRecordingSink sink, IAuditWriter audit, IOutboxWriter outbox, IClock clock,
        CancellationToken ct)
    {
        var gate = await GateAsync(channelId, sessionId, http, db, tenant, options.Value, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var recording = await FindRecordingAsync(db, gate.Session!.Id, recordingId, ct);
        if (recording is null)
        {
            return Error(StatusCodes.Status404NotFound, LiveErrors.NotFound);
        }

        if (recording.State != LiveRules.RecordingActive)
        {
            return Error(StatusCodes.Status409Conflict, LiveErrors.RecordingNotReady);
        }

        var now = clock.UtcNow;
        var sealedResult = await SealAsync(db, recording, gate.Profile!.Id, now, sink, options.Value, audit, outbox, ct);
        await db.SaveChangesAsync(ct);
        return sealedResult ?? Results.Ok(await ViewObjectAsync(db, recording, ct));
    }

    private static async Task<IResult> ReadRecording(
        Guid channelId, Guid sessionId, Guid recordingId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IOptions<LiveOptions> options, IObjectStorage storage, IClock clock, CancellationToken ct)
    {
        var gate = await GateAsync(channelId, sessionId, http, db, tenant, options.Value, clock, ct, activeOnly: false);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var recording = await FindRecordingAsync(db, gate.Session!.Id, recordingId, ct);
        if (recording is null)
        {
            return Error(StatusCodes.Status404NotFound, LiveErrors.NotFound);
        }

        await ApplyLifecycleAsync(db, recording, storage, clock.UtcNow, ct);
        return Results.Ok(await ViewObjectAsync(db, recording, ct));
    }

    private static async Task<IResult> Download(
        Guid channelId, Guid sessionId, Guid recordingId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IOptions<LiveOptions> options, IObjectStorage storage, IAuditWriter audit, IClock clock, CancellationToken ct)
    {
        var gate = await GateAsync(channelId, sessionId, http, db, tenant, options.Value, clock, ct, activeOnly: false);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var recording = await FindRecordingAsync(db, gate.Session!.Id, recordingId, ct);
        if (recording is null)
        {
            return Error(StatusCodes.Status404NotFound, LiveErrors.NotFound);
        }

        var now = clock.UtcNow;
        await ApplyLifecycleAsync(db, recording, storage, now, ct);
        if (!LiveRules.CanDownload(recording.State, recording.ScanStatus, recording.ExpiresAt, now, recording.OnHold)
            || LiveRules.IsPublicUrl(recording.ObjectKey)
            || string.IsNullOrEmpty(recording.ObjectKey))
        {
            var code = recording.State == LiveRules.RecordingExpired ? LiveErrors.RecordingExpired : LiveErrors.RecordingNotReady;
            return Error(StatusCodes.Status409Conflict, code);
        }

        var ttl = TimeSpan.FromSeconds(LiveRules.DownloadTtl(options.Value.DownloadTtlSeconds));
        var download = await storage.CreateDownloadUrlAsync(recording.ObjectKey, "huddle-recording.txt", ttl, ct);
        audit.Add(Audit(gate.Session.TenantId, gate.Profile!.Id, AuditActions.LiveRecordingDownload, recording.Id, now));
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { url = download.Url, expiresAt = download.ExpiresAt });
    }

    private static async Task<IResult?> SealAsync(
        VibeChatDbContext db, LiveRecording recording, UserId actor, DateTimeOffset now, ILiveRecordingSink sink,
        LiveOptions options, IAuditWriter audit, IOutboxWriter outbox, CancellationToken ct)
    {
        recording.OnHold = await HeldAsync(db, recording, now, ct);
        var payload = Encoding.UTF8.GetBytes(
            $"{{\"sessionId\":\"{recording.SessionId:D}\",\"actorId\":\"{actor.Value:D}\",\"stoppedAt\":\"{now:O}\"}}");
        var used = await db.LiveRecordings.Where(x => x.WorkspaceId == recording.WorkspaceId && x.State == LiveRules.RecordingReady)
            .SumAsync(x => (long?)x.ByteSize, ct) ?? 0;
        if (!LiveRules.QuotaAllows(used, payload.Length, LiveRules.QuotaBytes(options.RecordingQuotaBytes)))
        {
            recording.State = LiveRules.RecordingQuotaState;
            recording.StoppedAt = now;
            recording.ObjectKey = null;
            Publish(outbox, recording, actor, now);
            return Error(StatusCodes.Status409Conflict, LiveErrors.RecordingQuota);
        }

        var key = LiveRules.ObjectKey(recording.TenantId, recording.Id);
        var stored = await sink.StoreAsync(key, payload, ct);
        recording.StoppedAt = now;
        if (!stored.Stored || LiveRules.IsPublicUrl(stored.ObjectKey))
        {
            recording.State = LiveRules.RecordingQuarantine;
            recording.ScanStatus = LiveRules.ScanRejected;
            recording.ObjectKey = null;
            Publish(outbox, recording, actor, now);
            return Error(StatusCodes.Status409Conflict, LiveErrors.RecordingRejected);
        }

        recording.State = LiveRules.RecordingReady;
        recording.ObjectKey = stored.ObjectKey;
        recording.ByteSize = stored.SizeBytes;
        recording.ScanStatus = LiveRules.ScanClean;
        recording.ExpiresAt = now.AddDays(LiveRules.RetentionDays(options.RecordingRetentionDays));
        audit.Add(Audit(recording.TenantId, actor, AuditActions.LiveRecordingStop, recording.Id, now));
        audit.Add(Audit(recording.TenantId, actor, AuditActions.LiveRecordingReady, recording.Id, now));
        Publish(outbox, recording, actor, now);
        return null;
    }

    private static async Task ApplyDecisionsAsync(
        VibeChatDbContext db, LiveRecording recording, UserId actor, DateTimeOffset now, IAuditWriter audit,
        IOutboxWriter outbox, CancellationToken ct)
    {
        var rows = await db.LiveRecordingConsents.Where(x => x.RecordingId == recording.Id).ToListAsync(ct);
        var next = LiveRules.NextRecordingState(rows.Select(x => x.Decision));
        if (next == recording.State)
        {
            Publish(outbox, recording, actor, now);
            return;
        }

        recording.State = next;
        if (next == LiveRules.RecordingActive)
        {
            recording.StartedAt = now;
            audit.Add(Audit(recording.TenantId, actor, AuditActions.LiveRecordingStart, recording.Id, now));
        }

        Publish(outbox, recording, actor, now);
    }

    private static async Task ApplyLifecycleAsync(
        VibeChatDbContext db, LiveRecording recording, IObjectStorage storage, DateTimeOffset now, CancellationToken ct)
    {
        recording.OnHold = await HeldAsync(db, recording, now, ct);
        if (!LiveRules.ShouldPurge(recording.State, recording.ExpiresAt, now, recording.OnHold))
        {
            return;
        }

        if (!string.IsNullOrEmpty(recording.ObjectKey) && !LiveRules.IsPublicUrl(recording.ObjectKey))
        {
            await storage.DeleteObjectAsync(recording.ObjectKey, ct);
        }

        recording.State = LiveRules.RecordingExpired;
        recording.ObjectKey = null;
        await db.SaveChangesAsync(ct);
    }

    private static async Task<bool> HeldAsync(VibeChatDbContext db, LiveRecording recording, DateTimeOffset now, CancellationToken ct)
    {
        var holds = await db.LegalHolds.AsNoTracking()
            .Where(x => x.WorkspaceId == recording.WorkspaceId && x.Status == LegalHoldStatus.Active)
            .ToListAsync(ct);
        var subject = new HoldSubject(
            recording.WorkspaceId.Value,
            recording.ChannelId.Value,
            recording.ChannelId.Value,
            recording.StartedBy.Value,
            recording.CreatedAt);
        return holds.Any(hold => LegalHoldRules.IsCovering(
            hold.Status,
            hold.ExpiresAt,
            hold.WorkspaceId.Value,
            hold.ScopeKind,
            hold.ScopeId,
            hold.IntervalStart,
            hold.IntervalEnd,
            subject,
            now));
    }

    private static async Task<IResult> ClearShareAsync(
        VibeChatDbContext db, LiveSession session, LiveParticipant target, UserId actor, string action, DateTimeOffset now,
        IAuditWriter audit, IOutboxWriter outbox, CancellationToken ct)
    {
        if (target.SharingScreen)
        {
            target.SharingScreen = false;
            audit.Add(Audit(session.TenantId, actor, action, session.Id, now));
            PublishSession(outbox, session, now);
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(new { sharing = false, userId = target.UserId.Value });
    }

    private static async Task<object> ViewObjectAsync(VibeChatDbContext db, LiveRecording recording, CancellationToken ct)
    {
        var rows = await db.LiveRecordingConsents.AsNoTracking()
            .Where(x => x.RecordingId == recording.Id)
            .ToListAsync(ct);
        var ids = rows.Select(x => x.UserId).Append(recording.StartedBy).Distinct().ToArray();
        var names = await db.UserProfiles.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        return new
        {
            id = recording.Id,
            state = recording.State,
            startedBy = recording.StartedBy.Value,
            startedByName = names.TryGetValue(recording.StartedBy, out var name) ? name : "",
            expiresAt = recording.ExpiresAt,
            onHold = recording.OnHold,
            consents = rows.Select(row => new
            {
                userId = row.UserId.Value,
                decision = row.Decision
            }).ToArray()
        };
    }

    private static async Task<bool> WorkspaceFlagAsync(VibeChatDbContext db, WorkspaceId workspaceId, bool share, CancellationToken ct)
    {
        var policy = await db.LiveWorkspacePolicies.AsNoTracking().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId, ct);
        return policy is not null && (share ? policy.ShareEnabled : policy.RecordingEnabled);
    }

    private static Task<LiveRecording?> OpenRecordingAsync(VibeChatDbContext db, Guid sessionId, CancellationToken ct) =>
        db.LiveRecordings.FirstOrDefaultAsync(x => x.SessionId == sessionId && OpenStates.Contains(x.State), ct);

    private static Task<LiveRecording?> FindRecordingAsync(VibeChatDbContext db, Guid sessionId, Guid recordingId, CancellationToken ct) =>
        db.LiveRecordings.FirstOrDefaultAsync(x => x.Id == recordingId && x.SessionId == sessionId, ct);

    private static Task<LiveParticipant?> ParticipantAsync(VibeChatDbContext db, Guid sessionId, UserId userId, CancellationToken ct) =>
        db.LiveParticipants.FirstOrDefaultAsync(x => x.SessionId == sessionId && x.UserId == userId && x.LeftAt == null, ct);

    private static async Task<(IResult? Error, UserProfile? Profile, LiveSession? Session)> GateAsync(
        Guid channelId, Guid sessionId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, LiveOptions options,
        IClock clock, CancellationToken ct, bool activeOnly = true)
    {
        if (!options.Enabled)
        {
            return (Error(StatusCodes.Status404NotFound, LiveErrors.Disabled), null, null);
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var channel = await RequestAuth.ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
        if (channel is null)
        {
            return (Results.Forbid(), null, null);
        }

        var session = await db.LiveSessions.FirstOrDefaultAsync(x => x.Id == sessionId && x.ChannelId == channel.Id, ct);
        if (session is null || (activeOnly && session.State != LiveRules.StateActive))
        {
            return (Error(StatusCodes.Status404NotFound, session is null ? LiveErrors.NotFound : LiveErrors.Ended), null, null);
        }

        return (null, profile, session);
    }

    private static void Publish(IOutboxWriter outbox, LiveRecording recording, UserId actor, DateTimeOffset now)
    {
        outbox.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = recording.TenantId,
            Type = LiveRules.RecordingEvent,
            Payload = JsonSerializer.Serialize(new
            {
                schemaVersion = LiveRules.SchemaVersion,
                tenantId = recording.TenantId.Value,
                recordingId = recording.Id,
                sessionId = recording.SessionId,
                channelId = recording.ChannelId.Value,
                state = recording.State,
                actorId = actor.Value
            }, Json),
            OccurredAt = now
        });
    }

    private static void PublishSession(IOutboxWriter outbox, LiveSession session, DateTimeOffset now)
    {
        outbox.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = session.TenantId,
            Type = LiveRules.EventType,
            Payload = JsonSerializer.Serialize(new
            {
                schemaVersion = LiveRules.SchemaVersion,
                tenantId = session.TenantId.Value,
                sessionId = session.Id,
                channelId = session.ChannelId.Value,
                state = session.State,
                participantCount = 0,
                videoAllowed = session.VideoAllowed
            }, Json),
            OccurredAt = now
        });
    }

    private static AuditEvent Audit(TenantId tenantId, UserId actor, string action, Guid entityId, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorUserId = actor,
            Action = action,
            EntityType = action.StartsWith("live.recording", StringComparison.Ordinal) ? "live.recording" : "live.session",
            EntityId = entityId.ToString("D"),
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

    private static IResult Error(int status, string code) =>
        Results.Json(new { error = code }, statusCode: status);
}

internal sealed class LivePolicyRequest
{
    public bool ShareEnabled { get; set; }

    public bool RecordingEnabled { get; set; }
}

internal sealed class LiveRevokeRequest
{
    public Guid UserId { get; set; }
}

internal sealed class LiveConsentRequest
{
    public string? Decision { get; set; }
}
