using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.Notifications;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class SchedulingIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Schedule_stores_one_utc_instant_for_equivalent_zones()
    {
        using var alice = Client("alice");
        var sao = await CreateScheduleAsync(alice, "fuso-sp", "2027-06-01T09:00:00", "America/Sao_Paulo");
        var utc = await CreateScheduleAsync(alice, "fuso-utc", "2027-06-01T12:00:00", "Etc/UTC");
        sao.DueAtUtc.Should().Be(utc.DueAtUtc);
        sao.TimeZone.Should().Be("America/Sao_Paulo");
        utc.TimeZone.Should().Be("Etc/UTC");
    }

    [Fact]
    public async Task Edit_and_cancel_before_claim_prevents_send()
    {
        using var alice = Client("alice");
        var created = await CreateScheduleAsync(alice, "antes-do-claim", "2027-06-02T09:00:00", "America/Sao_Paulo");
        var edited = await alice.PatchAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/scheduled-messages/{created.Id}",
            new { body = "texto-editado", sendAtLocal = "2027-06-02T11:00:00", timeZone = "America/Sao_Paulo" });
        edited.StatusCode.Should().Be(HttpStatusCode.OK);
        var editedBody = await edited.Content.ReadFromJsonAsync<ScheduleItemDto>(JsonOptions);
        editedBody!.Body.Should().Be("texto-editado");
        editedBody.DueAtUtc.Should().Be(created.DueAtUtc.AddHours(2));

        var cancel = await alice.DeleteAsync($"/api/v1/workspaces/{WorkspaceId}/scheduled-messages/{created.Id}");
        cancel.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await BackdateScheduledAsync(created.Id);
        var processor = factory.Services.GetRequiredService<ScheduleDispatchProcessor>();
        await processor.ProcessBatchAsync(CancellationToken.None);

        await using var db = factory.CreateMigratorDbContext();
        var row = await db.ScheduledMessages.IgnoreQueryFilters().SingleAsync(x => x.Id == created.Id);
        row.Status.Should().Be(ScheduleStatuses.Cancelled);
        var sent = await db.Messages.IgnoreQueryFilters().AnyAsync(x => x.Id == row.PlannedMessageId);
        sent.Should().BeFalse();
    }

    [Fact]
    public async Task Claim_and_cancel_race_has_a_single_winner()
    {
        using var alice = Client("alice");
        var created = await CreateScheduleAsync(alice, "corrida-claim", "2027-06-03T09:00:00", "Etc/UTC");
        await BackdateScheduledAsync(created.Id);

        var processor = factory.Services.GetRequiredService<ScheduleDispatchProcessor>();
        var claim = processor.ProcessBatchAsync(CancellationToken.None);
        var cancel = alice.DeleteAsync($"/api/v1/workspaces/{WorkspaceId}/scheduled-messages/{created.Id}");
        await Task.WhenAll(claim, cancel);
        var cancelResponse = await cancel;

        await using var db = factory.CreateMigratorDbContext();
        var row = await db.ScheduledMessages.IgnoreQueryFilters().SingleAsync(x => x.Id == created.Id);
        var sent = await db.Messages.IgnoreQueryFilters().CountAsync(x => x.Id == row.PlannedMessageId);
        if (row.Status == ScheduleStatuses.Cancelled)
        {
            sent.Should().Be(0);
            cancelResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
        else
        {
            row.Status.Should().Be(ScheduleStatuses.Sent);
            sent.Should().Be(1);
            cancelResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
    }

    [Fact]
    public async Task Retry_after_send_does_not_duplicate_the_message()
    {
        using var alice = Client("alice");
        var created = await CreateScheduleAsync(alice, "retry-idempotente", "2027-06-04T09:00:00", "Etc/UTC");
        await BackdateScheduledAsync(created.Id);
        var processor = factory.Services.GetRequiredService<ScheduleDispatchProcessor>();
        (await processor.ProcessBatchAsync(CancellationToken.None)).Should().BeGreaterThan(0);

        await using (var db = factory.CreateMigratorDbContext())
        {
            var row = await db.ScheduledMessages.IgnoreQueryFilters().SingleAsync(x => x.Id == created.Id);
            row.Status.Should().Be(ScheduleStatuses.Sent);
            row.Status = ScheduleStatuses.Pending;
            row.ClaimedAt = null;
            row.SendAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            row.NextAttemptAt = null;
            await db.SaveChangesAsync();
        }

        await processor.ProcessBatchAsync(CancellationToken.None);

        await using var verify = factory.CreateMigratorDbContext();
        var again = await verify.ScheduledMessages.IgnoreQueryFilters().SingleAsync(x => x.Id == created.Id);
        again.Status.Should().Be(ScheduleStatuses.Sent);
        var copies = await verify.Messages.IgnoreQueryFilters().CountAsync(x => x.Id == again.PlannedMessageId);
        copies.Should().Be(1);
    }

    [Fact]
    public async Task Revoked_membership_cancels_send_without_revealing_the_channel()
    {
        using var alice = Client("alice");
        var channelName = $"sched-priv-{Guid.NewGuid():N}"[..22];
        var createChannel = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels",
            new { name = channelName, type = "Private", spaceId = (Guid?)null });
        createChannel.EnsureSuccessStatusCode();
        var channel = await createChannel.Content.ReadFromJsonAsync<ChannelDto>(JsonOptions);

        var created = await CreateScheduleAsync(
            alice, "sem-membership", "2027-06-05T09:00:00", "Etc/UTC", channel!.Id);
        await using (var db = factory.CreateMigratorDbContext())
        {
            var membership = await db.ChannelMembers.IgnoreQueryFilters()
                .SingleAsync(x => x.ChannelId == new ChannelId(channel.Id) && x.UserId == SeedData.AliceUserId);
            db.ChannelMembers.Remove(membership);
            await db.SaveChangesAsync();
        }

        await BackdateScheduledAsync(created.Id);
        await factory.Services.GetRequiredService<ScheduleDispatchProcessor>().ProcessBatchAsync(CancellationToken.None);

        await using var verify = factory.CreateMigratorDbContext();
        var row = await verify.ScheduledMessages.IgnoreQueryFilters().SingleAsync(x => x.Id == created.Id);
        row.Status.Should().Be(ScheduleStatuses.MembershipRevoked);
        (await verify.Messages.IgnoreQueryFilters().AnyAsync(x => x.Id == row.PlannedMessageId)).Should().BeFalse();
        var notice = await verify.OutboxMessages.IgnoreQueryFilters()
            .Where(x => x.Type == ScheduleEventTypes.ScheduledMessageDue && x.Payload.Contains(created.Id.ToString()))
            .OrderByDescending(x => x.OccurredAt)
            .FirstAsync();
        notice.Payload.Should().Contain("membership_revoked");
        notice.Payload.Should().NotContain(created.Body!);
        notice.Payload.Should().NotContain(channel.Id.ToString());
    }

    [Fact]
    public async Task Reminder_is_private_and_push_follows_preferences()
    {
        using var alice = Client("alice");
        using var bob = Client("bob");
        var messageId = Guid.NewGuid();
        var send = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages",
            new { messageId, idempotencyKey = $"rem-{messageId:N}", body = "lembrar-item", replyToMessageId = (Guid?)null, threadId = (Guid?)null });
        send.EnsureSuccessStatusCode();

        var reminder = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/reminders",
            new
            {
                idempotencyKey = $"note-{messageId:N}",
                targetKind = "Message",
                remindAtLocal = "2027-06-06T15:00:00",
                timeZone = "America/Sao_Paulo",
                note = "só alice",
                messageId
            });
        reminder.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await reminder.Content.ReadFromJsonAsync<ScheduleItemDto>(JsonOptions);
        created!.Kind.Should().Be("reminder");
        created.Note.Should().Be("só alice");

        var bobList = await bob.GetFromJsonAsync<SchedulePageDto>(
            $"/api/v1/workspaces/{WorkspaceId}/schedule", JsonOptions);
        bobList!.Items.Should().NotContain(x => x.Id == created.Id);

        var bobCancel = await bob.DeleteAsync($"/api/v1/workspaces/{WorkspaceId}/reminders/{created.Id}");
        bobCancel.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await using (var db = factory.CreateMigratorDbContext())
        {
            var still = await db.Reminders.IgnoreQueryFilters().SingleAsync(x => x.Id == created.Id);
            still.Status.Should().Be(ScheduleStatuses.Pending);
            still.UserId.Should().Be(SeedData.AliceUserId);
        }

        var endpoint = $"https://push.example.test/schedule/{Guid.NewGuid():N}";
        var register = await alice.PostAsJsonAsync(
            "/api/v1/notifications/push/subscriptions",
            new
            {
                endpoint,
                p256dh = "dGVzdC1wMjU2ZGgtYWxpY2UtMjI=",
                auth = "dGVzdC1hdXRoLWtleS1hbGljZQ==",
                userAgent = "VibeChat-Tests"
            });
        register.StatusCode.Should().Be(HttpStatusCode.OK);
        var recorder = factory.Services.GetRequiredService<RecordingPushSender>();
        recorder.Reset();

        await BackdateReminderAsync(created.Id);
        await factory.Services.GetRequiredService<ScheduleDispatchProcessor>().ProcessBatchAsync(CancellationToken.None);
        await DrainOutboxAsync();
        recorder.Attempts.Should().Contain(x => x.Endpoint == endpoint && x.PayloadJson.Contains("Lembrete"));

        await using (var db = factory.CreateMigratorDbContext())
        {
            var prefs = await db.NotificationPreferences.IgnoreQueryFilters()
                .Where(x => x.UserId == SeedData.AliceUserId)
                .ToListAsync();
            if (prefs.Count == 0)
            {
                db.NotificationPreferences.Add(new NotificationPreference
                {
                    Id = Guid.NewGuid(),
                    TenantId = SeedData.DemoTenantId,
                    UserId = SeedData.AliceUserId,
                    PushEnabled = false,
                    EmailEnabled = true
                });
            }
            else
            {
                foreach (var pref in prefs)
                {
                    pref.PushEnabled = false;
                }
            }

            await db.SaveChangesAsync();
        }

        var quiet = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/reminders",
            new
            {
                idempotencyKey = $"quiet-{Guid.NewGuid():N}",
                targetKind = "Time",
                remindAtLocal = "2027-06-07T15:00:00",
                timeZone = "Etc/UTC",
                note = "sem push"
            });
        quiet.EnsureSuccessStatusCode();
        var quietItem = await quiet.Content.ReadFromJsonAsync<ScheduleItemDto>(JsonOptions);
        recorder.Reset();
        await BackdateReminderAsync(quietItem!.Id);
        await factory.Services.GetRequiredService<ScheduleDispatchProcessor>().ProcessBatchAsync(CancellationToken.None);
        await DrainOutboxAsync();
        recorder.Attempts.Should().NotContain(x => x.Endpoint == endpoint && x.PayloadJson.Contains("sem push"));

        await using var cleanup = factory.CreateMigratorDbContext();
        var savedPrefs = await cleanup.NotificationPreferences.IgnoreQueryFilters()
            .Where(x => x.UserId == SeedData.AliceUserId)
            .ToListAsync();
        foreach (var pref in savedPrefs)
        {
            pref.PushEnabled = true;
        }

        var subs = await cleanup.PushSubscriptions.IgnoreQueryFilters()
            .Where(x => x.Endpoint == endpoint)
            .ToListAsync();
        cleanup.PushSubscriptions.RemoveRange(subs);
        await cleanup.SaveChangesAsync();
    }

    private async Task<ScheduleItemDto> CreateScheduleAsync(
        HttpClient client,
        string body,
        string local,
        string zone,
        Guid? channelId = null)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/channels/{channelId ?? SeedData.DemoChannelId.Value}/scheduled-messages",
            new
            {
                idempotencyKey = Guid.NewGuid().ToString("N"),
                body,
                sendAtLocal = local,
                timeZone = zone
            });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var item = await response.Content.ReadFromJsonAsync<ScheduleItemDto>(JsonOptions);
        item.Should().NotBeNull();
        return item!;
    }

    private async Task BackdateScheduledAsync(Guid id)
    {
        await using var db = factory.CreateMigratorDbContext();
        var row = await db.ScheduledMessages.IgnoreQueryFilters().SingleAsync(x => x.Id == id);
        row.SendAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2);
        row.NextAttemptAt = null;
        await db.SaveChangesAsync();
    }

    private async Task BackdateReminderAsync(Guid id)
    {
        await using var db = factory.CreateMigratorDbContext();
        var row = await db.Reminders.IgnoreQueryFilters().SingleAsync(x => x.Id == id);
        row.RemindAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2);
        row.NextAttemptAt = null;
        await db.SaveChangesAsync();
    }

    private async Task DrainOutboxAsync()
    {
        var processor = factory.Services.GetRequiredService<OutboxProcessor>();
        for (var i = 0; i < 8; i++)
        {
            if (await processor.ProcessBatchAsync(CancellationToken.None) == 0)
            {
                break;
            }
        }
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private sealed record ScheduleItemDto(
        string Kind,
        Guid Id,
        string Status,
        DateTimeOffset DueAtUtc,
        string TimeZone,
        string? Body,
        string? Note);

    private sealed record SchedulePageDto(ScheduleItemDto[] Items, string? NextCursor);

    private sealed record ChannelDto(Guid Id, string Name);
}
