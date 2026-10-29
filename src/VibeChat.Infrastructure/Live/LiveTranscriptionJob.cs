using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.Infrastructure.Compliance;
using VibeChat.Realtime;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

/// <summary>
/// Async transcription job. A failure leaves the recording object untouched.
/// The provider is called only after every remaining participant has accepted.
/// </summary>
public sealed class LiveTranscriptionJob(
    ILiveTranscriptionProvider provider,
    DelegatedAdminService quotas,
    IAuditWriter audit,
    IClock clock,
    IOptions<LiveOptions> liveOptions,
    IOptions<MeetingAiOptions> meetingOptions)
{
    public async Task<MeetingAiNotice?> CompleteAsync(VibeChatDbContext db, Guid transcriptId, CancellationToken cancellationToken)
    {
        var transcript = await db.LiveTranscripts.FirstOrDefaultAsync(x => x.Id == transcriptId, cancellationToken);
        if (transcript is null || transcript.State != MeetingAiRules.StateQueued)
        {
            return transcript is null ? null : Notice(transcript, transcript.StartedBy);
        }

        var decisions = await db.LiveTranscriptConsents.AsNoTracking()
            .Where(x => x.TranscriptId == transcript.Id)
            .Select(x => x.Decision)
            .ToListAsync(cancellationToken);
        if (decisions.Count == 0 || decisions.Any(decision => decision != LiveRules.DecisionAccepted))
        {
            return Notice(transcript, transcript.StartedBy);
        }

        var recording = await db.LiveRecordings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == transcript.RecordingId, cancellationToken);
        var objectKey = recording?.ObjectKey;
        if (recording is null
            || recording.State != LiveRules.RecordingReady
            || string.IsNullOrEmpty(objectKey)
            || LiveRules.IsPublicUrl(objectKey))
        {
            return Fail(transcript, LiveErrors.TranscriptionNoMedia);
        }

        var selected = MeetingAiRules.NormalizeProvider(meetingOptions.Value.Provider);
        if (!MeetingAiRules.MaySend(consentGranted: true, selected))
        {
            return Fail(transcript, LiveErrors.TranscriptionUnavailable);
        }

        var quota = await quotas.TryConsumeAsync(
            transcript.TenantId, transcript.WorkspaceId, DelegatedResources.AiBudget, 1, cancellationToken);
        if (!quota.Allowed)
        {
            return Fail(transcript, quota.Error ?? DelegatedAdminErrors.QuotaHardLimit);
        }

        transcript.State = MeetingAiRules.StateRunning;
        transcript.Provider = selected;
        MeetingTranscriptDraft draft;
        try
        {
            draft = await provider.TranscribeAsync(
                new MeetingTranscriptRequest(
                    transcript.Id,
                    objectKey,
                    ConsentGranted: true,
                    MeetingAiRules.PolicyMetadata(LiveRules.RetentionDays(liveOptions.Value.RecordingRetentionDays))),
                cancellationToken);
        }
        catch (Exception)
        {
            return Fail(transcript, MeetingAiRules.ProviderError);
        }

        if (!MeetingAiRules.HasCitations(draft))
        {
            return Fail(transcript, MeetingAiRules.ProviderError);
        }

        var now = clock.UtcNow;
        var ordinal = 0;
        foreach (var segment in draft.Segments)
        {
            db.LiveTranscriptSegments.Add(new LiveTranscriptSegment
            {
                Id = Guid.NewGuid(),
                TenantId = transcript.TenantId,
                TranscriptId = transcript.Id,
                Ordinal = ordinal++,
                StartMs = segment.StartMs,
                EndMs = segment.EndMs,
                SpeakerLabel = MeetingAiRules.OpaqueSpeaker(segment.SpeakerLabel),
                Text = MeetingAiRules.Clip(segment.Text, MeetingAiRules.MaxSegmentLength)
            });
        }

        foreach (var suggestion in draft.Suggestions)
        {
            db.LiveNoteSuggestions.Add(new LiveNoteSuggestion
            {
                Id = Guid.NewGuid(),
                TenantId = transcript.TenantId,
                TranscriptId = transcript.Id,
                Kind = suggestion.Kind,
                Text = MeetingAiRules.Clip(suggestion.Text, MeetingAiRules.MaxSuggestionLength),
                StartMs = suggestion.StartMs,
                EndMs = suggestion.EndMs,
                State = MeetingAiRules.SuggestionSuggested
            });
        }

        transcript.Summary = MeetingAiRules.Clip(draft.Summary, MeetingAiRules.MaxSummaryLength);
        transcript.Confidence = MeetingAiRules.ConfidenceLow;
        transcript.ProviderRef = MeetingAiRules.Clip(draft.ProviderRef, 80);
        transcript.State = MeetingAiRules.StateReady;
        transcript.FailureCode = null;
        transcript.CompletedAt = now;
        transcript.ExpiresAt = now.AddDays(LiveRules.RetentionDays(liveOptions.Value.RecordingRetentionDays));
        audit.Add(Audit(transcript, AuditActions.LiveTranscriptionReady, now));
        VibeChatMetrics.LiveTranscriptions.Add(1);
        return Notice(transcript, transcript.StartedBy);
    }

    private MeetingAiNotice Fail(LiveTranscript transcript, string code)
    {
        var now = clock.UtcNow;
        transcript.State = MeetingAiRules.StateFailed;
        transcript.FailureCode = code;
        transcript.CompletedAt = now;
        audit.Add(Audit(transcript, AuditActions.LiveTranscriptionFailed, now));
        VibeChatMetrics.LiveTranscriptions.Add(1);
        return Notice(transcript, transcript.StartedBy);
    }

    private static MeetingAiNotice Notice(LiveTranscript transcript, UserId actor) =>
        new(transcript.Id, transcript.SessionId, transcript.ChannelId, transcript.State, actor.Value);

    private static AuditEvent Audit(LiveTranscript transcript, string action, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = transcript.TenantId,
            ActorUserId = transcript.StartedBy,
            Action = action,
            EntityType = "live.transcript",
            EntityId = transcript.Id.ToString("D"),
            MetadataJson = "{}",
            OccurredAt = now
        };
}
