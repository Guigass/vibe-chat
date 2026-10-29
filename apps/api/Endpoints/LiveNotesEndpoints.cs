using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Identity;
using VibeChat.Conversations;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.Realtime;
using VibeChat.SharedKernel;

namespace VibeChat.Api.Endpoints;

internal static class LiveNotesEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] VisibleStates =
    [
        MeetingAiRules.StateConsent,
        MeetingAiRules.StateQueued,
        MeetingAiRules.StateRunning,
        MeetingAiRules.StateReady,
        MeetingAiRules.StateFailed
    ];

    internal static void Map(RouteGroupBuilder v1)
    {
        v1.MapPost("/channels/{channelId:guid}/live/sessions/{sessionId:guid}/transcripts", Request)
            .RequirePermission(Permissions.Live.Record);
        v1.MapPost("/channels/{channelId:guid}/live/sessions/{sessionId:guid}/transcripts/{transcriptId:guid}/consent", Consent)
            .RequirePermission(Permissions.Message.Send);
        v1.MapPost("/channels/{channelId:guid}/live/sessions/{sessionId:guid}/transcripts/{transcriptId:guid}/cancel", Cancel)
            .RequirePermission(Permissions.Live.Record);
        v1.MapGet("/channels/{channelId:guid}/live/sessions/{sessionId:guid}/transcripts/{transcriptId:guid}", Read)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/channels/{channelId:guid}/live/sessions/{sessionId:guid}/transcripts/{transcriptId:guid}/export", Export)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/channels/{channelId:guid}/live/sessions/{sessionId:guid}/transcripts/{transcriptId:guid}/delete", Delete)
            .RequirePermission(Permissions.Live.Record);
        v1.MapPost("/channels/{channelId:guid}/live/sessions/{sessionId:guid}/transcripts/{transcriptId:guid}/suggestions/{suggestionId:guid}/confirm", Confirm)
            .RequirePermission(Permissions.Ai.Transcribe);
        v1.MapPost("/channels/{channelId:guid}/live/sessions/{sessionId:guid}/transcripts/{transcriptId:guid}/suggestions/{suggestionId:guid}/dismiss", Dismiss)
            .RequirePermission(Permissions.Ai.Transcribe);
    }

    internal static async Task<bool> EnabledAsync(
        VibeChatDbContext db, WorkspaceId workspaceId, MeetingAiOptions options, CancellationToken ct)
    {
        var policy = await db.LiveWorkspacePolicies.AsNoTracking().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId, ct);
        return LiveRules.FeatureOn(options.Enabled, policy?.TranscriptionEnabled == true);
    }

    internal static async Task OnParticipantJoinedAsync(
        VibeChatDbContext db, LiveSession session, UserId userId, CancellationToken ct)
    {
        var transcript = await OpenAsync(db, session.Id, ct);
        if (transcript is null || transcript.State is not (MeetingAiRules.StateConsent or MeetingAiRules.StateQueued))
        {
            return;
        }

        var row = await db.LiveTranscriptConsents
            .FirstOrDefaultAsync(x => x.TranscriptId == transcript.Id && x.UserId == userId, ct);
        if (row is null)
        {
            db.LiveTranscriptConsents.Add(new LiveTranscriptConsent
            {
                Id = Guid.NewGuid(),
                TenantId = session.TenantId,
                TranscriptId = transcript.Id,
                UserId = userId,
                Decision = LiveRules.DecisionPending
            });
        }

        if (transcript.State == MeetingAiRules.StateQueued)
        {
            transcript.State = MeetingAiRules.StateConsent;
        }
    }

    internal static async Task OnParticipantLeftAsync(
        VibeChatDbContext db, LiveSession session, LiveParticipant? self, bool ended, DateTimeOffset now,
        IOutboxWriter outbox, CancellationToken ct)
    {
        var transcript = await OpenAsync(db, session.Id, ct);
        if (transcript is null || transcript.State is not (MeetingAiRules.StateConsent or MeetingAiRules.StateQueued))
        {
            return;
        }

        var rows = await db.LiveTranscriptConsents.Where(x => x.TranscriptId == transcript.Id).ToListAsync(ct);
        if (self is not null)
        {
            var row = rows.FirstOrDefault(x => x.UserId == self.UserId);
            if (row is not null)
            {
                rows.Remove(row);
                db.LiveTranscriptConsents.Remove(row);
            }
        }

        if (ended && transcript.State == MeetingAiRules.StateConsent)
        {
            transcript.State = MeetingAiRules.StateDeclined;
            return;
        }

        if (transcript.State != MeetingAiRules.StateConsent)
        {
            return;
        }

        QueueIfReady(transcript, rows.Select(x => x.Decision), self?.UserId ?? transcript.StartedBy, now, outbox);
    }

    internal static async Task<object?> OpenViewAsync(VibeChatDbContext db, Guid sessionId, CancellationToken ct)
    {
        var transcript = await db.LiveTranscripts.AsNoTracking()
            .Where(x => x.SessionId == sessionId && VisibleStates.Contains(x.State))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (transcript is null)
        {
            return null;
        }

        var name = await db.UserProfiles.AsNoTracking()
            .Where(x => x.Id == transcript.StartedBy)
            .Select(x => x.DisplayName)
            .FirstOrDefaultAsync(ct);
        return new
        {
            id = transcript.Id,
            state = transcript.State,
            failureCode = transcript.FailureCode,
            startedBy = transcript.StartedBy.Value,
            startedByName = name ?? ""
        };
    }

    private static async Task<IResult> Request(
        Guid channelId, Guid sessionId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IOptions<LiveOptions> live, IOptions<MeetingAiOptions> meeting, IAuditWriter audit, IClock clock, CancellationToken ct)
    {
        if (!LiveCaptureEndpoints.TryKey(http, out var key))
        {
            return LiveCaptureEndpoints.Error(StatusCodes.Status400BadRequest, LiveErrors.IdempotencyRequired);
        }

        var gate = await LiveCaptureEndpoints.GateAsync(channelId, sessionId, http, db, tenant, live.Value, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        if (!await EnabledAsync(db, gate.Session!.WorkspaceId, meeting.Value, ct))
        {
            return LiveCaptureEndpoints.Error(StatusCodes.Status403Forbidden, LiveErrors.TranscriptionDisabled);
        }

        if (!MeetingAiRules.MaySend(true, MeetingAiRules.NormalizeProvider(meeting.Value.Provider)))
        {
            return LiveCaptureEndpoints.Error(StatusCodes.Status403Forbidden, LiveErrors.TranscriptionUnavailable);
        }

        var confidential = await db.Channels.AsNoTracking()
            .AnyAsync(x => x.Id == gate.Session.ChannelId && x.Type == ChannelType.ConfidentialE2ee, ct);
        if (confidential)
        {
            return LiveCaptureEndpoints.Error(StatusCodes.Status403Forbidden, E2eeErrors.CapabilityUnavailable);
        }

        var replay = await db.LiveTranscripts.FirstOrDefaultAsync(
            x => x.SessionId == gate.Session.Id && x.IdempotencyKey == key, ct);
        if (replay is not null)
        {
            return replay.RequestHash == MeetingAiRules.RequestHash
                ? Results.Ok(await OpenViewAsync(db, gate.Session.Id, ct))
                : LiveCaptureEndpoints.Error(StatusCodes.Status409Conflict, LiveErrors.IdempotencyConflict);
        }

        var open = await OpenAsync(db, gate.Session.Id, ct);
        if (open is not null)
        {
            return Results.Ok(await OpenViewAsync(db, gate.Session.Id, ct));
        }

        var recording = await db.LiveRecordings.AsNoTracking()
            .Where(x => x.SessionId == gate.Session.Id && x.State == LiveRules.RecordingReady && x.ObjectKey != null)
            .OrderByDescending(x => x.StoppedAt)
            .FirstOrDefaultAsync(ct);
        if (recording is null || LiveRules.IsPublicUrl(recording.ObjectKey))
        {
            return LiveCaptureEndpoints.Error(StatusCodes.Status409Conflict, LiveErrors.TranscriptionNoMedia);
        }

        var people = await db.LiveParticipants.Where(x => x.SessionId == gate.Session.Id && x.LeftAt == null).ToListAsync(ct);
        if (people.Count == 0)
        {
            return LiveCaptureEndpoints.Error(StatusCodes.Status409Conflict, LiveErrors.TranscriptionNoMedia);
        }

        var now = clock.UtcNow;
        var transcript = new LiveTranscript
        {
            Id = Guid.NewGuid(),
            TenantId = gate.Session.TenantId,
            WorkspaceId = gate.Session.WorkspaceId,
            ChannelId = gate.Session.ChannelId,
            SessionId = gate.Session.Id,
            RecordingId = recording.Id,
            State = MeetingAiRules.StateConsent,
            Provider = MeetingAiRules.NormalizeProvider(meeting.Value.Provider),
            StartedBy = gate.Profile!.Id,
            IdempotencyKey = key,
            RequestHash = MeetingAiRules.RequestHash,
            CreatedAt = now
        };
        db.LiveTranscripts.Add(transcript);
        foreach (var person in people)
        {
            db.LiveTranscriptConsents.Add(new LiveTranscriptConsent
            {
                Id = Guid.NewGuid(),
                TenantId = gate.Session.TenantId,
                TranscriptId = transcript.Id,
                UserId = person.UserId,
                Decision = LiveRules.DecisionPending
            });
        }

        audit.Add(Audit(transcript, gate.Profile.Id, AuditActions.LiveTranscriptionRequest, now));
        await db.SaveChangesAsync(ct);
        return Results.Json(await OpenViewAsync(db, gate.Session.Id, ct), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> Consent(
        Guid channelId, Guid sessionId, Guid transcriptId, LiveConsentRequest request, HttpContext http,
        VibeChatDbContext db, ITenantContext tenant, IOptions<LiveOptions> live, IAuditWriter audit, IOutboxWriter outbox,
        IClock clock, CancellationToken ct)
    {
        var decision = (request.Decision ?? "").Trim().ToLowerInvariant() switch
        {
            "accept" or "accepted" => LiveRules.DecisionAccepted,
            "decline" or "declined" => LiveRules.DecisionDeclined,
            _ => ""
        };
        if (decision.Length == 0)
        {
            return LiveCaptureEndpoints.Error(StatusCodes.Status400BadRequest, LiveErrors.TranscriptionDecisionInvalid);
        }

        var gate = await LiveCaptureEndpoints.GateAsync(channelId, sessionId, http, db, tenant, live.Value, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var transcript = await FindAsync(db, gate.Session!.Id, transcriptId, ct);
        if (transcript is null || transcript.State != MeetingAiRules.StateConsent)
        {
            return LiveCaptureEndpoints.Error(StatusCodes.Status404NotFound, LiveErrors.TranscriptionNotFound);
        }

        var row = await db.LiveTranscriptConsents
            .FirstOrDefaultAsync(x => x.TranscriptId == transcript.Id && x.UserId == gate.Profile!.Id, ct);
        if (row is null)
        {
            return Results.Forbid();
        }

        var now = clock.UtcNow;
        if (row.Decision != decision)
        {
            row.Decision = decision;
            row.DecidedAt = now;
            audit.Add(Audit(transcript, gate.Profile!.Id, AuditActions.LiveTranscriptionConsent, now));
        }

        var decisions = await db.LiveTranscriptConsents.Where(x => x.TranscriptId == transcript.Id).ToListAsync(ct);
        QueueIfReady(transcript, decisions.Select(x => x.Decision), gate.Profile!.Id, now, outbox);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new
        {
            id = transcript.Id,
            state = transcript.State,
            failureCode = transcript.FailureCode,
            startedBy = transcript.StartedBy.Value,
            startedByName = ""
        });
    }

    private static async Task<IResult> Cancel(
        Guid channelId, Guid sessionId, Guid transcriptId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IOptions<LiveOptions> live, IAuditWriter audit, IClock clock, CancellationToken ct)
    {
        var gate = await LiveCaptureEndpoints.GateAsync(channelId, sessionId, http, db, tenant, live.Value, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var transcript = await FindAsync(db, gate.Session!.Id, transcriptId, ct);
        if (transcript is null || transcript.State is not (MeetingAiRules.StateConsent or MeetingAiRules.StateQueued))
        {
            return LiveCaptureEndpoints.Error(StatusCodes.Status404NotFound, LiveErrors.TranscriptionNotFound);
        }

        transcript.State = MeetingAiRules.StateDeclined;
        audit.Add(Audit(transcript, gate.Profile!.Id, AuditActions.LiveTranscriptionDelete, clock.UtcNow));
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { id = transcript.Id, state = transcript.State });
    }

    private static async Task<IResult> Read(
        Guid channelId, Guid sessionId, Guid transcriptId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IOptions<LiveOptions> live, ILiveTranscriptionProvider provider, IClock clock, CancellationToken ct) =>
        await NoteAsync(channelId, sessionId, transcriptId, http, db, tenant, live, provider, clock, null, ct);

    private static Task<IResult> Export(
        Guid channelId, Guid sessionId, Guid transcriptId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IOptions<LiveOptions> live, ILiveTranscriptionProvider provider, IAuditWriter audit, IClock clock, CancellationToken ct) =>
        NoteAsync(channelId, sessionId, transcriptId, http, db, tenant, live, provider, clock, audit, ct);

    private static async Task<IResult> Delete(
        Guid channelId, Guid sessionId, Guid transcriptId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IOptions<LiveOptions> live, ILiveTranscriptionProvider provider, IAuditWriter audit, IClock clock, CancellationToken ct)
    {
        var gate = await LiveCaptureEndpoints.GateAsync(channelId, sessionId, http, db, tenant, live.Value, clock, ct, activeOnly: false);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var transcript = await FindAsync(db, gate.Session!.Id, transcriptId, ct);
        if (transcript is null || transcript.State is MeetingAiRules.StateDeleted or MeetingAiRules.StateExpired)
        {
            return LiveCaptureEndpoints.Error(StatusCodes.Status404NotFound, LiveErrors.TranscriptionNotFound);
        }

        await ForgetAsync(db, transcript, provider, ct);
        transcript.State = MeetingAiRules.StateDeleted;
        audit.Add(Audit(transcript, gate.Profile!.Id, AuditActions.LiveTranscriptionDelete, clock.UtcNow));
        await db.SaveChangesAsync(ct);
        var recording = await db.LiveRecordings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == transcript.RecordingId, ct);
        return Results.Ok(new { id = transcript.Id, state = transcript.State, recordingState = recording?.State });
    }

    private static async Task<IResult> Confirm(
        Guid channelId, Guid sessionId, Guid transcriptId, Guid suggestionId, HttpContext http, VibeChatDbContext db,
        ITenantContext tenant, IOptions<LiveOptions> live, IAuditWriter audit, IClock clock, CancellationToken ct) =>
        await ReviewAsync(channelId, sessionId, transcriptId, suggestionId, MeetingAiRules.SuggestionConfirmed, http, db, tenant, live, audit, clock, ct);

    private static async Task<IResult> Dismiss(
        Guid channelId, Guid sessionId, Guid transcriptId, Guid suggestionId, HttpContext http, VibeChatDbContext db,
        ITenantContext tenant, IOptions<LiveOptions> live, IAuditWriter audit, IClock clock, CancellationToken ct) =>
        await ReviewAsync(channelId, sessionId, transcriptId, suggestionId, MeetingAiRules.SuggestionDismissed, http, db, tenant, live, audit, clock, ct);

    private static async Task<IResult> ReviewAsync(
        Guid channelId, Guid sessionId, Guid transcriptId, Guid suggestionId, string next, HttpContext http,
        VibeChatDbContext db, ITenantContext tenant, IOptions<LiveOptions> live, IAuditWriter audit, IClock clock,
        CancellationToken ct)
    {
        var gate = await LiveCaptureEndpoints.GateAsync(channelId, sessionId, http, db, tenant, live.Value, clock, ct, activeOnly: false);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var transcript = await FindAsync(db, gate.Session!.Id, transcriptId, ct);
        if (transcript is null || transcript.State != MeetingAiRules.StateReady)
        {
            return LiveCaptureEndpoints.Error(StatusCodes.Status409Conflict, LiveErrors.NoteNotReady);
        }

        var suggestion = await db.LiveNoteSuggestions
            .FirstOrDefaultAsync(x => x.Id == suggestionId && x.TranscriptId == transcript.Id, ct);
        if (suggestion is null)
        {
            return LiveCaptureEndpoints.Error(StatusCodes.Status404NotFound, LiveErrors.SuggestionInvalid);
        }

        if (suggestion.State == MeetingAiRules.SuggestionDismissed && next == MeetingAiRules.SuggestionConfirmed)
        {
            return LiveCaptureEndpoints.Error(StatusCodes.Status409Conflict, LiveErrors.SuggestionInvalid);
        }

        if (suggestion.State != next)
        {
            suggestion.State = next;
            audit.Add(Audit(transcript, gate.Profile!.Id, AuditActions.LiveTranscriptionConfirm, clock.UtcNow));
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(new
        {
            id = suggestion.Id,
            kind = suggestion.Kind,
            state = suggestion.State,
            startMs = suggestion.StartMs,
            endMs = suggestion.EndMs
        });
    }

    private static async Task<IResult> NoteAsync(
        Guid channelId, Guid sessionId, Guid transcriptId, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IOptions<LiveOptions> live, ILiveTranscriptionProvider provider, IClock clock, IAuditWriter? audit, CancellationToken ct)
    {
        var gate = await LiveCaptureEndpoints.GateAsync(channelId, sessionId, http, db, tenant, live.Value, clock, ct, activeOnly: false);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var transcript = await FindAsync(db, gate.Session!.Id, transcriptId, ct);
        if (transcript is null || transcript.State is MeetingAiRules.StateDeleted or MeetingAiRules.StateDeclined)
        {
            return LiveCaptureEndpoints.Error(StatusCodes.Status404NotFound, LiveErrors.TranscriptionNotFound);
        }

        var now = clock.UtcNow;
        transcript.OnHold = await HeldAsync(db, transcript, now, ct);
        if (MeetingAiRules.ShouldForget(transcript.State, transcript.ExpiresAt, now, transcript.OnHold))
        {
            await ForgetAsync(db, transcript, provider, ct);
            transcript.State = MeetingAiRules.StateExpired;
            await db.SaveChangesAsync(ct);
            return LiveCaptureEndpoints.Error(StatusCodes.Status409Conflict, LiveErrors.TranscriptionExpired);
        }

        if (audit is not null && transcript.State != MeetingAiRules.StateReady)
        {
            return LiveCaptureEndpoints.Error(StatusCodes.Status409Conflict, LiveErrors.NoteNotReady);
        }

        if (audit is not null)
        {
            audit.Add(Audit(transcript, gate.Profile!.Id, AuditActions.LiveTranscriptionExport, now));
            await db.SaveChangesAsync(ct);
        }

        var segments = await db.LiveTranscriptSegments.AsNoTracking()
            .Where(x => x.TranscriptId == transcript.Id)
            .OrderBy(x => x.Ordinal)
            .ToListAsync(ct);
        var suggestions = await db.LiveNoteSuggestions.AsNoTracking()
            .Where(x => x.TranscriptId == transcript.Id)
            .OrderBy(x => x.StartMs)
            .ThenBy(x => x.Kind)
            .ToListAsync(ct);
        return Results.Ok(new
        {
            id = transcript.Id,
            state = transcript.State,
            failureCode = transcript.FailureCode,
            confidence = transcript.State == MeetingAiRules.StateReady ? transcript.Confidence : null,
            summary = transcript.State == MeetingAiRules.StateReady ? transcript.Summary : "",
            recordingId = transcript.RecordingId,
            onHold = transcript.OnHold,
            segments = segments.Select(segment => new
            {
                startMs = segment.StartMs,
                endMs = segment.EndMs,
                speakerLabel = segment.SpeakerLabel,
                text = segment.Text
            }).ToArray(),
            suggestions = suggestions.Select(item => new
            {
                id = item.Id,
                kind = item.Kind,
                text = item.Text,
                startMs = item.StartMs,
                endMs = item.EndMs,
                state = item.State
            }).ToArray()
        });
    }

    private static void QueueIfReady(
        LiveTranscript transcript, IEnumerable<string> decisions, UserId actor, DateTimeOffset now, IOutboxWriter outbox)
    {
        var next = MeetingAiRules.NextState(decisions);
        if (next == MeetingAiRules.StateDeclined)
        {
            transcript.State = MeetingAiRules.StateDeclined;
            return;
        }

        if (next != MeetingAiRules.StateQueued || transcript.State == MeetingAiRules.StateQueued)
        {
            return;
        }

        transcript.State = MeetingAiRules.StateQueued;
        transcript.QueuedAt = now;
        outbox.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = transcript.TenantId,
            Type = MeetingAiRules.RequestedEvent,
            Payload = JsonSerializer.Serialize(new
            {
                schemaVersion = MeetingAiRules.SchemaVersion,
                tenantId = transcript.TenantId.Value,
                transcriptId = transcript.Id,
                sessionId = transcript.SessionId,
                channelId = transcript.ChannelId.Value,
                state = transcript.State,
                actorId = actor.Value
            }, Json),
            OccurredAt = now
        });
    }

    private static async Task ForgetAsync(
        VibeChatDbContext db, LiveTranscript transcript, ILiveTranscriptionProvider provider, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(transcript.ProviderRef)
            && transcript.Provider is MeetingAiRules.ProviderLocal or MeetingAiRules.ProviderExternal)
        {
            await provider.DeleteAsync(transcript.ProviderRef, ct);
        }

        var segments = await db.LiveTranscriptSegments.Where(x => x.TranscriptId == transcript.Id).ToListAsync(ct);
        var suggestions = await db.LiveNoteSuggestions.Where(x => x.TranscriptId == transcript.Id).ToListAsync(ct);
        db.LiveTranscriptSegments.RemoveRange(segments);
        db.LiveNoteSuggestions.RemoveRange(suggestions);
        transcript.Summary = "";
        transcript.ProviderRef = null;
    }

    private static async Task<bool> HeldAsync(VibeChatDbContext db, LiveTranscript transcript, DateTimeOffset now, CancellationToken ct)
    {
        var holds = await db.LegalHolds.AsNoTracking()
            .Where(x => x.WorkspaceId == transcript.WorkspaceId && x.Status == LegalHoldStatus.Active)
            .ToListAsync(ct);
        var subject = new HoldSubject(
            transcript.WorkspaceId.Value,
            transcript.ChannelId.Value,
            transcript.ChannelId.Value,
            transcript.StartedBy.Value,
            transcript.CreatedAt);
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

    private static Task<LiveTranscript?> OpenAsync(VibeChatDbContext db, Guid sessionId, CancellationToken ct) =>
        db.LiveTranscripts
            .Where(x => x.SessionId == sessionId && (x.State == MeetingAiRules.StateConsent || x.State == MeetingAiRules.StateQueued || x.State == MeetingAiRules.StateRunning))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

    private static Task<LiveTranscript?> FindAsync(VibeChatDbContext db, Guid sessionId, Guid transcriptId, CancellationToken ct) =>
        db.LiveTranscripts.FirstOrDefaultAsync(x => x.Id == transcriptId && x.SessionId == sessionId, ct);

    private static AuditEvent Audit(LiveTranscript transcript, UserId actor, string action, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = transcript.TenantId,
            ActorUserId = actor,
            Action = action,
            EntityType = "live.transcript",
            EntityId = transcript.Id.ToString("D"),
            MetadataJson = "{}",
            OccurredAt = now
        };
}
