using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.Infrastructure;
using VibeChat.Realtime;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-149 — transcription waits for consent, cites time, and leaves the recording intact.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class LiveNotesIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Decline_and_disabled_flag_never_send_audio()
    {
        using var host = NotesHost(factory, enabled: true, provider: MeetingAiRules.ProviderExternal);
        using var demo = Client(host, "demo");
        using var alice = Client(host, "alice");
        var channelId = await CreateChannelAsync(demo);
        await EnablePolicyAsync(demo, transcription: true);
        var sessionId = await StartSessionAsync(alice, channelId);
        try
        {
            (await JoinAsync(alice, channelId, sessionId)).EnsureSuccessStatusCode();
            var recording = await ReadyRecordingAsync(demo, alice, channelId, sessionId);
            var requested = await demo.SendAsync(TranscriptRequest(channelId, sessionId));
            requested.StatusCode.Should().Be(HttpStatusCode.Created);
            var transcript = (await requested.Content.ReadFromJsonAsync<TranscriptStub>(Json))!;
            transcript.State.Should().Be(MeetingAiRules.StateConsent);

            var declined = await alice.PostAsJsonAsync(ConsentPath(channelId, sessionId, transcript.Id), new { decision = "decline" });
            declined.EnsureSuccessStatusCode();
            (await declined.Content.ReadFromJsonAsync<TranscriptStub>(Json))!.State.Should().Be(MeetingAiRules.StateDeclined);
            await DrainAsync(host);
            host.Services.GetRequiredService<LiveTranscriptionProbe>().Sends.Should().Be(0);
            await using var db = factory.CreateMigratorDbContext();
            (await db.LiveTranscriptSegments.IgnoreQueryFilters().CountAsync(x => x.TranscriptId == transcript.Id)).Should().Be(0);
            (await db.LiveRecordings.IgnoreQueryFilters().SingleAsync(x => x.Id == recording)).ObjectKey.Should().NotBeNull();
        }
        finally
        {
            await alice.PostAsync($"/api/v1/channels/{channelId}/live/sessions/{sessionId}/leave", null);
        }

        using var off = NotesHost(factory, enabled: false, provider: MeetingAiRules.ProviderExternal);
        using var demoOff = Client(off, "demo");
        using var aliceOff = Client(off, "alice");
        await EnablePolicyAsync(demoOff, transcription: true);
        var health = await demoOff.GetFromJsonAsync<HealthStub>($"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/live/health", Json);
        health!.TranscriptionEnabled.Should().BeFalse();
        var other = await StartSessionAsync(aliceOff, channelId);
        (await JoinAsync(aliceOff, channelId, other)).EnsureSuccessStatusCode();
        var blocked = await demoOff.SendAsync(TranscriptRequest(channelId, other));
        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadError(blocked)).Should().Be(LiveErrors.TranscriptionDisabled);
        off.Services.GetRequiredService<LiveTranscriptionProbe>().Sends.Should().Be(0);
        await aliceOff.PostAsync($"/api/v1/channels/{channelId}/live/sessions/{other}/leave", null);
    }

    [Fact]
    public async Task Review_confirms_a_cited_note_and_delete_keeps_the_recording()
    {
        using var host = NotesHost(factory, enabled: true, provider: MeetingAiRules.ProviderExternal);
        using var demo = Client(host, "demo");
        using var alice = Client(host, "alice");
        var channelId = await CreateChannelAsync(demo);
        await EnablePolicyAsync(demo, transcription: true);
        var sessionId = await StartSessionAsync(alice, channelId);
        try
        {
            (await JoinAsync(alice, channelId, sessionId)).EnsureSuccessStatusCode();
            var recording = await ReadyRecordingAsync(demo, alice, channelId, sessionId);
            var requested = await demo.SendAsync(TranscriptRequest(channelId, sessionId));
            requested.StatusCode.Should().Be(HttpStatusCode.Created);
            var transcript = (await requested.Content.ReadFromJsonAsync<TranscriptStub>(Json))!;
            (await alice.PostAsJsonAsync(ConsentPath(channelId, sessionId, transcript.Id), new { decision = "accept" })).EnsureSuccessStatusCode();
            await DrainAsync(host);

            var note = await alice.GetFromJsonAsync<NoteView>(NotePath(channelId, sessionId, transcript.Id), Json);
            note!.State.Should().Be(MeetingAiRules.StateReady);
            note.Summary.Should().Contain("00:04-00:09");
            note.Segments.Should().OnlyContain(segment => segment.EndMs > segment.StartMs);
            note.Suggestions.Should().OnlyContain(item => item.State == MeetingAiRules.SuggestionSuggested);
            var probe = host.Services.GetRequiredService<LiveTranscriptionProbe>();
            probe.Sends.Should().Be(1);
            probe.LastObjectKey.Should().NotBeNull();
            LiveRules.IsPublicUrl(probe.LastObjectKey).Should().BeFalse();
            probe.LastPolicy.Should().Contain("rag=off");

            var outsider = await alice.GetAsync($"/api/v1/channels/{Guid.NewGuid()}/live/sessions/{sessionId}/transcripts/{transcript.Id}");
            outsider.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var decision = note.Suggestions.Single(item => item.Kind == MeetingAiRules.SuggestionDecision);
            var confirmed = await alice.PostAsync(SuggestionPath(channelId, sessionId, transcript.Id, decision.Id, "confirm"), null);
            confirmed.EnsureSuccessStatusCode();
            (await confirmed.Content.ReadFromJsonAsync<SuggestionView>(Json))!.State.Should().Be(MeetingAiRules.SuggestionConfirmed);
            var after = await alice.GetFromJsonAsync<NoteView>(NotePath(channelId, sessionId, transcript.Id), Json);
            after!.Suggestions.Should().ContainSingle(item => item.State == MeetingAiRules.SuggestionSuggested);

            var exported = await alice.PostAsync(NotePath(channelId, sessionId, transcript.Id) + "/export", null);
            exported.EnsureSuccessStatusCode();
            (await exported.Content.ReadFromJsonAsync<NoteView>(Json))!.Summary.Should().Contain("00:04-00:09");

            await using (var db = factory.CreateMigratorDbContext())
            {
                var payload = await db.OutboxMessages.IgnoreQueryFilters()
                    .Where(x => x.Type == MeetingAiRules.RequestedEvent)
                    .OrderByDescending(x => x.OccurredAt)
                    .Select(x => x.Payload)
                    .FirstAsync();
                payload.Should().NotContain("Opening").And.NotContain("http");
                (await db.AuditEvents.IgnoreQueryFilters().CountAsync(x =>
                    x.EntityId == transcript.Id.ToString("D") && x.Action == AuditActions.LiveTranscriptionExport)).Should().Be(1);
            }

            var deleted = await demo.PostAsync(NotePath(channelId, sessionId, transcript.Id) + "/delete", null);
            deleted.EnsureSuccessStatusCode();
            probe.Deletes.Should().Be(1);
            await using var afterDelete = factory.CreateMigratorDbContext();
            (await afterDelete.LiveTranscriptSegments.IgnoreQueryFilters().CountAsync(x => x.TranscriptId == transcript.Id)).Should().Be(0);
            (await afterDelete.LiveRecordings.IgnoreQueryFilters().SingleAsync(x => x.Id == recording)).ObjectKey.Should().NotBeNull();
        }
        finally
        {
            await alice.PostAsync($"/api/v1/channels/{channelId}/live/sessions/{sessionId}/leave", null);
        }
    }

    [Fact]
    public async Task Local_provider_does_not_egress_and_failure_keeps_the_recording()
    {
        using var local = NotesHost(factory, enabled: true, provider: MeetingAiRules.ProviderLocal);
        using var demo = Client(local, "demo");
        using var alice = Client(local, "alice");
        var channelId = await CreateChannelAsync(demo);
        await EnablePolicyAsync(demo, transcription: true);
        var sessionId = await StartSessionAsync(alice, channelId);
        try
        {
            (await JoinAsync(alice, channelId, sessionId)).EnsureSuccessStatusCode();
            await ReadyRecordingAsync(demo, alice, channelId, sessionId);
            var requested = await demo.SendAsync(TranscriptRequest(channelId, sessionId));
            var transcript = (await requested.Content.ReadFromJsonAsync<TranscriptStub>(Json))!;
            (await alice.PostAsJsonAsync(ConsentPath(channelId, sessionId, transcript.Id), new { decision = "accept" })).EnsureSuccessStatusCode();
            await DrainAsync(local);
            var note = await alice.GetFromJsonAsync<NoteView>(NotePath(channelId, sessionId, transcript.Id), Json);
            note!.Summary.Should().Contain("00:04-00:09");
            local.Services.GetRequiredService<LiveTranscriptionProbe>().Sends.Should().Be(0);
        }
        finally
        {
            await alice.PostAsync($"/api/v1/channels/{channelId}/live/sessions/{sessionId}/leave", null);
        }

        using var failing = NotesHost(factory, enabled: true, provider: MeetingAiRules.ProviderExternal, fail: true);
        using var demoFail = Client(failing, "demo");
        using var aliceFail = Client(failing, "alice");
        var failedSession = await StartSessionAsync(aliceFail, channelId);
        try
        {
            (await JoinAsync(aliceFail, channelId, failedSession)).EnsureSuccessStatusCode();
            var recording = await ReadyRecordingAsync(demoFail, aliceFail, channelId, failedSession);
            var requested = await demoFail.SendAsync(TranscriptRequest(channelId, failedSession));
            var transcript = (await requested.Content.ReadFromJsonAsync<TranscriptStub>(Json))!;
            (await aliceFail.PostAsJsonAsync(ConsentPath(channelId, failedSession, transcript.Id), new { decision = "accept" })).EnsureSuccessStatusCode();
            await DrainAsync(failing);
            var note = await aliceFail.GetFromJsonAsync<NoteView>(NotePath(channelId, failedSession, transcript.Id), Json);
            note!.State.Should().Be(MeetingAiRules.StateFailed);
            note.FailureCode.Should().Be(MeetingAiRules.ProviderError);
            failing.Services.GetRequiredService<LiveTranscriptionProbe>().Sends.Should().Be(0);
            await using var db = factory.CreateMigratorDbContext();
            (await db.LiveRecordings.IgnoreQueryFilters().SingleAsync(x => x.Id == recording)).State.Should().Be(LiveRules.RecordingReady);
        }
        finally
        {
            await aliceFail.PostAsync($"/api/v1/channels/{channelId}/live/sessions/{failedSession}/leave", null);
        }
    }

    [Fact]
    public async Task Budget_denial_does_not_send_audio_or_drop_the_recording()
    {
        using var host = NotesHost(factory, enabled: true, provider: MeetingAiRules.ProviderExternal, budget: true);
        using var demo = Client(host, "demo");
        _ = await demo.GetAsync($"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/live/health");
        var quotaId = Guid.NewGuid();
        await using (var db = factory.CreateMigratorDbContext())
        {
            db.ResourceQuotas.Add(new ResourceQuota
            {
                Id = quotaId,
                TenantId = SeedData.DemoTenantId,
                WorkspaceId = SeedData.DemoWorkspaceId,
                Resource = DelegatedResources.AiBudget,
                SoftLimit = 0,
                HardLimit = 0,
                UpdatedBy = SeedData.DemoUserId,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        try
        {
            using var alice = Client(host, "alice");
            var channelId = await CreateChannelAsync(demo);
            await EnablePolicyAsync(demo, transcription: true);
            var sessionId = await StartSessionAsync(alice, channelId);
            try
            {
                (await JoinAsync(alice, channelId, sessionId)).EnsureSuccessStatusCode();
                var recording = await ReadyRecordingAsync(demo, alice, channelId, sessionId);
                var requested = await demo.SendAsync(TranscriptRequest(channelId, sessionId));
                var transcript = (await requested.Content.ReadFromJsonAsync<TranscriptStub>(Json))!;
                (await alice.PostAsJsonAsync(ConsentPath(channelId, sessionId, transcript.Id), new { decision = "accept" })).EnsureSuccessStatusCode();
                await DrainAsync(host);
                var note = await alice.GetFromJsonAsync<NoteView>(NotePath(channelId, sessionId, transcript.Id), Json);
                note!.State.Should().Be(MeetingAiRules.StateFailed);
                note.FailureCode.Should().Be(DelegatedAdminErrors.QuotaHardLimit);
                host.Services.GetRequiredService<LiveTranscriptionProbe>().Sends.Should().Be(0);
                await using var db = factory.CreateMigratorDbContext();
                (await db.LiveRecordings.IgnoreQueryFilters().SingleAsync(x => x.Id == recording)).ObjectKey.Should().NotBeNull();
            }
            finally
            {
                await alice.PostAsync($"/api/v1/channels/{channelId}/live/sessions/{sessionId}/leave", null);
            }
        }
        finally
        {
            await using var db = factory.CreateMigratorDbContext();
            var quota = await db.ResourceQuotas.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == quotaId);
            if (quota is not null)
            {
                db.ResourceQuotas.Remove(quota);
            }

            var counters = await db.QuotaCounters.IgnoreQueryFilters()
                .Where(x => x.WorkspaceId == SeedData.DemoWorkspaceId && x.Resource == DelegatedResources.AiBudget)
                .ToListAsync();
            db.QuotaCounters.RemoveRange(counters);
            await db.SaveChangesAsync();
        }
    }

    private static async Task<Guid> ReadyRecordingAsync(HttpClient demo, HttpClient alice, Guid channelId, Guid sessionId)
    {
        var requested = await demo.SendAsync(RecordingRequest(channelId, sessionId));
        requested.EnsureSuccessStatusCode();
        var recording = (await requested.Content.ReadFromJsonAsync<RecordingStub>(Json))!;
        (await alice.PostAsJsonAsync(
            $"/api/v1/channels/{channelId}/live/sessions/{sessionId}/recordings/{recording.Id}/consent",
            new { decision = "accept" })).EnsureSuccessStatusCode();
        var stopped = await demo.PostAsync(
            $"/api/v1/channels/{channelId}/live/sessions/{sessionId}/recordings/{recording.Id}/stop", null);
        stopped.EnsureSuccessStatusCode();
        return recording.Id;
    }

    private static async Task DrainAsync(WebApplicationFactory<Program> host)
    {
        using var scope = host.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
        await processor.ProcessBatchAsync(CancellationToken.None);
    }

    private static WebApplicationFactory<Program> NotesHost(
        VibeChatApiFactory factory, bool enabled, string provider, bool fail = false, bool budget = false) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Features:Live:Enabled", "true");
            builder.UseSetting("Features:Live:VideoEnabled", "false");
            builder.UseSetting("Features:Live:Provider", LiveRules.ProviderDev);
            builder.UseSetting("Features:Live:ShareEnabled", "false");
            builder.UseSetting("Features:Live:RecordingEnabled", "true");
            builder.UseSetting("Features:Live:MaxParticipants", "8");
            builder.UseSetting("Features:Live:MaxSessionsPerWorkspace", "8");
            builder.UseSetting("Features:MeetingAi:Enabled", enabled ? "true" : "false");
            builder.UseSetting("Features:MeetingAi:Provider", provider);
            if (budget)
            {
                builder.UseSetting("Features:DelegatedAdmin:Enabled", "true");
            }

            if (fail)
            {
                builder.ConfigureServices(services =>
                    services.AddSingleton<ILiveTranscriptionProvider, FailingTranscriptionProvider>());
            }
        });

    private static async Task EnablePolicyAsync(HttpClient demo, bool transcription)
    {
        var response = await demo.PutAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/live/policy",
            new { shareEnabled = false, recordingEnabled = true, transcriptionEnabled = transcription });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> StartSessionAsync(HttpClient client, Guid channelId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/channels/{channelId}/live/sessions");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "live-" + Guid.NewGuid().ToString("N"));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SessionStub>(Json))!.SessionId;
    }

    private static async Task<Guid> CreateChannelAsync(HttpClient demo)
    {
        var created = await demo.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels",
            new { name = "note-" + Guid.NewGuid().ToString("N")[..10], type = "Public" });
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<CreatedChannel>(Json))!.Id;
    }

    private static HttpRequestMessage RecordingRequest(Guid channelId, Guid sessionId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/channels/{channelId}/live/sessions/{sessionId}/recordings");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "rec-" + Guid.NewGuid().ToString("N"));
        return request;
    }

    private static HttpRequestMessage TranscriptRequest(Guid channelId, Guid sessionId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/channels/{channelId}/live/sessions/{sessionId}/transcripts");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "tr-" + Guid.NewGuid().ToString("N"));
        return request;
    }

    private static string ConsentPath(Guid channelId, Guid sessionId, Guid transcriptId) =>
        $"/api/v1/channels/{channelId}/live/sessions/{sessionId}/transcripts/{transcriptId}/consent";

    private static string NotePath(Guid channelId, Guid sessionId, Guid transcriptId) =>
        $"/api/v1/channels/{channelId}/live/sessions/{sessionId}/transcripts/{transcriptId}";

    private static string SuggestionPath(Guid channelId, Guid sessionId, Guid transcriptId, Guid suggestionId, string action) =>
        $"{NotePath(channelId, sessionId, transcriptId)}/suggestions/{suggestionId}/{action}";

    private static Task<HttpResponseMessage> JoinAsync(HttpClient client, Guid channelId, Guid sessionId) =>
        client.PostAsync($"/api/v1/channels/{channelId}/live/sessions/{sessionId}/join", null);

    private static HttpClient Client(WebApplicationFactory<Program> host, string user)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private static async Task<string?> ReadError(HttpResponseMessage response)
    {
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("error").GetString();
    }

    private sealed class FailingTranscriptionProvider : ILiveTranscriptionProvider
    {
        public string Name => MeetingAiRules.ProviderExternal;

        public Task<MeetingTranscriptDraft> TranscribeAsync(MeetingTranscriptRequest request, CancellationToken cancellationToken)
        {
            if (!request.ConsentGranted)
            {
                throw new InvalidOperationException("consent");
            }

            throw new InvalidOperationException("lab failure");
        }

        public Task DeleteAsync(string providerRef, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed record CreatedChannel(Guid Id);
    private sealed record SessionStub(Guid SessionId);
    private sealed record RecordingStub(Guid Id);
    private sealed record TranscriptStub(Guid Id, string State);
    private sealed record HealthStub(bool TranscriptionEnabled);
    private sealed record NoteView(string State, string? FailureCode, string Summary, SegmentView[] Segments, SuggestionView[] Suggestions);
    private sealed record SegmentView(int StartMs, int EndMs);
    private sealed record SuggestionView(Guid Id, string Kind, string State);
}
