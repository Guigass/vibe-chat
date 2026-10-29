using VibeChat.SharedKernel;

namespace VibeChat.Realtime;

/// <summary>
/// Meeting transcription and notes (B-149). Off by default and separate from recording.
/// </summary>
public sealed class MeetingAiOptions
{
    public const string SectionName = "Features:MeetingAi";

    /// <summary>Instance kill switch. Workspace policy must also allow transcription.</summary>
    public bool Enabled { get; set; }

    /// <summary><c>none</c>, <c>local</c> or <c>external</c>. Local is preferred and does not leave the process.</summary>
    public string Provider { get; set; } = MeetingAiRules.ProviderNone;
}

public static class MeetingAiRules
{
    public const string RequestedEvent = "live.transcription.requested";
    public const string ChangedEvent = "live.transcription.changed";
    public const int SchemaVersion = 1;
    public const string StateConsent = "consent";
    public const string StateQueued = "queued";
    public const string StateRunning = "running";
    public const string StateReady = "ready";
    public const string StateFailed = "failed";
    public const string StateDeclined = "declined";
    public const string StateDeleted = "deleted";
    public const string StateExpired = "expired";
    public const string ProviderNone = "none";
    public const string ProviderLocal = "local";
    public const string ProviderExternal = "external";
    public const string SuggestionDecision = "decision";
    public const string SuggestionTask = "task";
    public const string SuggestionSuggested = "suggested";
    public const string SuggestionConfirmed = "confirmed";
    public const string SuggestionDismissed = "dismissed";
    public const string ConfidenceLow = "low";
    public const string RequestHash = "transcribe";
    public const string ProviderError = "ProviderError";
    public const int MaxSummaryLength = 2000;
    public const int MaxSegmentLength = 500;
    public const int MaxSuggestionLength = 300;

    public static readonly string[] OpenStates = [StateConsent, StateQueued, StateRunning, StateReady, StateFailed];

    public static string NormalizeProvider(string? provider) =>
        (provider ?? "").Trim().ToLowerInvariant() switch
        {
            ProviderLocal => ProviderLocal,
            ProviderExternal => ProviderExternal,
            _ => ProviderNone
        };

    public static bool MaySend(bool consentGranted, string provider) =>
        consentGranted && provider is ProviderLocal or ProviderExternal;

    public static string PolicyMetadata(int retentionDays) =>
        $"rag=off;retentionDays={retentionDays}";

    public static string NextState(IEnumerable<string> decisions)
    {
        var recording = LiveRules.NextRecordingState(decisions);
        if (recording == LiveRules.RecordingDeclined)
        {
            return StateDeclined;
        }

        return recording == LiveRules.RecordingActive ? StateQueued : StateConsent;
    }

    public static string? OpaqueSpeaker(string? raw) =>
        raw is not null && System.Text.RegularExpressions.Regex.IsMatch(raw, "^speaker-[0-9]{1,2}$")
            ? raw
            : null;

    public static bool HasCitations(MeetingTranscriptDraft draft) =>
        draft.Segments.Count > 0
        && draft.Segments.All(segment => segment.EndMs > segment.StartMs && segment.StartMs >= 0 && segment.Text.Trim().Length > 0)
        && draft.Suggestions.Count > 0
        && draft.Suggestions.All(item =>
            item.Kind is SuggestionDecision or SuggestionTask
            && draft.Segments.Any(segment => segment.StartMs == item.StartMs && segment.EndMs == item.EndMs))
        && draft.Summary.Contains("00:04-00:09", StringComparison.Ordinal);

    public static MeetingTranscriptDraft LabDraft(Guid transcriptId) =>
        new(
            [
                new MeetingSegmentDraft(0, 4_000, "speaker-1", "Opening."),
                new MeetingSegmentDraft(4_000, 9_000, "speaker-2", "Cited decision.")
            ],
            "Note cites 00:04-00:09.",
            ConfidenceLow,
            [
                new MeetingSuggestionDraft(SuggestionDecision, "Record the cited decision.", 4_000, 9_000),
                new MeetingSuggestionDraft(SuggestionTask, "Follow the cited action.", 4_000, 9_000)
            ],
            "ref-" + transcriptId.ToString("N"));

    public static bool ShouldForget(string state, DateTimeOffset? expiresAt, DateTimeOffset now, bool onHold) =>
        !onHold && state == StateReady && expiresAt is not null && expiresAt <= now;

    public static string Clip(string? value, int max)
    {
        var text = (value ?? "").Trim();
        return text.Length <= max ? text : text[..max];
    }
}

public sealed class LiveTranscript
{
    public Guid Id { get; set; }

    public TenantId TenantId { get; set; }

    public WorkspaceId WorkspaceId { get; set; }

    public ChannelId ChannelId { get; set; }

    public Guid SessionId { get; set; }

    public Guid RecordingId { get; set; }

    public string State { get; set; } = MeetingAiRules.StateConsent;

    public string? FailureCode { get; set; }

    public string Provider { get; set; } = MeetingAiRules.ProviderNone;

    public string? ProviderRef { get; set; }

    public string Summary { get; set; } = "";

    public string Confidence { get; set; } = MeetingAiRules.ConfidenceLow;

    public UserId StartedBy { get; set; }

    public string IdempotencyKey { get; set; } = "";

    public string RequestHash { get; set; } = "";

    public bool OnHold { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? QueuedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class LiveTranscriptConsent
{
    public Guid Id { get; set; }

    public TenantId TenantId { get; set; }

    public Guid TranscriptId { get; set; }

    public UserId UserId { get; set; }

    public string Decision { get; set; } = LiveRules.DecisionPending;

    public DateTimeOffset? DecidedAt { get; set; }
}

public sealed class LiveTranscriptSegment
{
    public Guid Id { get; set; }

    public TenantId TenantId { get; set; }

    public Guid TranscriptId { get; set; }

    public int Ordinal { get; set; }

    public int StartMs { get; set; }

    public int EndMs { get; set; }

    public string? SpeakerLabel { get; set; }

    public string Text { get; set; } = "";
}

public sealed class LiveNoteSuggestion
{
    public Guid Id { get; set; }

    public TenantId TenantId { get; set; }

    public Guid TranscriptId { get; set; }

    public string Kind { get; set; } = MeetingAiRules.SuggestionDecision;

    public string Text { get; set; } = "";

    public int StartMs { get; set; }

    public int EndMs { get; set; }

    public string State { get; set; } = MeetingAiRules.SuggestionSuggested;
}

public sealed record MeetingTranscriptRequest(
    Guid TranscriptId,
    string ObjectKey,
    bool ConsentGranted,
    string Policy);

public sealed record MeetingSegmentDraft(int StartMs, int EndMs, string? SpeakerLabel, string Text);

public sealed record MeetingSuggestionDraft(string Kind, string Text, int StartMs, int EndMs);

public sealed record MeetingTranscriptDraft(
    IReadOnlyList<MeetingSegmentDraft> Segments,
    string Summary,
    string Confidence,
    IReadOnlyList<MeetingSuggestionDraft> Suggestions,
    string ProviderRef);

public sealed record MeetingAiNotice(Guid TranscriptId, Guid SessionId, ChannelId ChannelId, string State, Guid ActorId);

public interface ILiveTranscriptionProvider
{
    string Name { get; }

    Task<MeetingTranscriptDraft> TranscribeAsync(MeetingTranscriptRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(string providerRef, CancellationToken cancellationToken);
}
