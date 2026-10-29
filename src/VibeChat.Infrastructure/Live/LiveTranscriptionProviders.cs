using VibeChat.Realtime;

namespace VibeChat.Infrastructure;

/// <summary>Counts authorized media references handed to the external adapter. Local never increments it.</summary>
public sealed class LiveTranscriptionProbe
{
    private int _sends;
    private int _deletes;

    public int Sends => _sends;

    public int Deletes => _deletes;

    public string? LastObjectKey { get; private set; }

    public string? LastPolicy { get; private set; }

    public void RecordSend(string objectKey, string policy)
    {
        Interlocked.Increment(ref _sends);
        LastObjectKey = objectKey;
        LastPolicy = policy;
    }

    public void RecordDelete() => Interlocked.Increment(ref _deletes);
}

public sealed class LocalLiveTranscriptionProvider : ILiveTranscriptionProvider
{
    public string Name => MeetingAiRules.ProviderLocal;

    public Task<MeetingTranscriptDraft> TranscribeAsync(MeetingTranscriptRequest request, CancellationToken cancellationToken)
    {
        if (!request.ConsentGranted)
        {
            throw new InvalidOperationException("Transcription requires consent.");
        }

        return Task.FromResult(MeetingAiRules.LabDraft(request.TranscriptId));
    }

    public Task DeleteAsync(string providerRef, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class ExternalLiveTranscriptionProvider(LiveTranscriptionProbe probe) : ILiveTranscriptionProvider
{
    public string Name => MeetingAiRules.ProviderExternal;

    public Task<MeetingTranscriptDraft> TranscribeAsync(MeetingTranscriptRequest request, CancellationToken cancellationToken)
    {
        if (!request.ConsentGranted || LiveRules.IsPublicUrl(request.ObjectKey))
        {
            throw new InvalidOperationException("External transcription requires an authorized object key.");
        }

        probe.RecordSend(request.ObjectKey, request.Policy);
        return Task.FromResult(MeetingAiRules.LabDraft(request.TranscriptId));
    }

    public Task DeleteAsync(string providerRef, CancellationToken cancellationToken)
    {
        probe.RecordDelete();
        return Task.CompletedTask;
    }
}

public sealed class NullLiveTranscriptionProvider : ILiveTranscriptionProvider
{
    public string Name => MeetingAiRules.ProviderNone;

    public Task<MeetingTranscriptDraft> TranscribeAsync(MeetingTranscriptRequest request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Meeting AI provider is none.");

    public Task DeleteAsync(string providerRef, CancellationToken cancellationToken) => Task.CompletedTask;
}
