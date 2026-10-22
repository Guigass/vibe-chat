using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Audit;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-111 — interactive action, history grant and attachment quota.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class PluginGovernedIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid ChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Builtin_action_is_idempotent_and_history_stops_when_revoked()
    {
        using var admin = Client("demo");
        await RemoveSlugAsync(PluginManifestRules.BuiltinActionAckId);
        var created = await InstallAsync(admin);
        created.Capabilities.Should().Contain(PluginManifestRules.CapabilityMessagesInteract);
        using var bot = BotClient(created.Token);

        var key = $"ack-{Guid.NewGuid():N}";
        var posted = await bot.PostAsJsonAsync(
            $"/api/v1/integrations/v1/channels/{ChannelId}/interactive-messages",
            new { body = $"ack-{key}", actions = new[] { "ack" }, idempotencyKey = key });
        posted.StatusCode.Should().Be(HttpStatusCode.Accepted, await posted.Content.ReadAsStringAsync());
        var message = await posted.Content.ReadFromJsonAsync<InteractiveDto>(Json);
        var replay = await bot.PostAsJsonAsync(
            $"/api/v1/integrations/v1/channels/{ChannelId}/interactive-messages",
            new { body = $"ack-{key}", actions = new[] { "ack" }, idempotencyKey = key });
        var replayedPost = await replay.Content.ReadFromJsonAsync<InteractiveDto>(Json);
        replayedPost!.MessageId.Should().Be(message!.MessageId);
        replayedPost.Idempotent.Should().BeTrue();

        using var alice = Client("alice");
        var timeline = await alice.GetFromJsonAsync<HistoryDto>(
            $"/api/v1/channels/{ChannelId}/messages?after={message.Sequence - 1}",
            Json);
        timeline!.Messages.Should().Contain(item =>
            item.Id == message.MessageId
            && item.PluginCard != null
            && item.PluginCard.PluginId == PluginManifestRules.BuiltinActionAckId
            && item.PluginCard.Actions.Contains("action-ack/ack"));

        var actionKey = $"click-{Guid.NewGuid():N}";
        var click = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels/{ChannelId}/messages/{message.MessageId}/plugin-actions",
            new { action = "action-ack/ack", idempotencyKey = actionKey });
        click.StatusCode.Should().Be(HttpStatusCode.Accepted, await click.Content.ReadAsStringAsync());
        var again = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels/{ChannelId}/messages/{message.MessageId}/plugin-actions",
            new { action = "action-ack/ack", idempotencyKey = actionKey });
        var replayed = await again.Content.ReadFromJsonAsync<ActionDto>(Json);
        replayed!.Idempotent.Should().BeTrue();
        replayed.Status.Should().Be(PluginActionStatus.Accepted);

        await using (var auditDb = factory.CreateMigratorDbContext())
        {
            var audits = await auditDb.AuditEvents.IgnoreQueryFilters()
                .CountAsync(item => item.Action == AuditActions.IntegrationPluginAction && item.MetadataJson.Contains(actionKey) == false && item.MetadataJson.Contains(message.MessageId.ToString()));
            audits.Should().Be(1);
        }

        await RemoveSlugAsync("history-peek");
        const string historyManifest = """
            {"schema":"vibechat.plugin.manifest.v1","id":"history-peek","name":"History Peek","version":"1.0.0","capabilities":["history.read"]}
            """;
        var reader = await InstallAsync(admin, historyManifest);
        using var readerBot = BotClient(reader.Token);
        var start = DateTimeOffset.UtcNow.AddMinutes(-5);
        var end = DateTimeOffset.UtcNow.AddMinutes(5);
        var grant = await admin.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/plugins/{reader.Id}/history-grants",
            new { channelId = ChannelId, windowStart = start, windowEnd = end });
        grant.StatusCode.Should().Be(HttpStatusCode.Created, await grant.Content.ReadAsStringAsync());
        var granted = await grant.Content.ReadFromJsonAsync<GrantDto>(Json);

        var from = start.AddMinutes(1);
        var to = end.AddMinutes(-1);
        var readKey = $"read-{Guid.NewGuid():N}";
        var history = await readerBot.GetFromJsonAsync<PluginHistoryDto>(HistoryUrl(ChannelId, from, to, readKey), Json);
        history!.Messages.Should().Contain(item => item.Id == message.MessageId && item.Body == $"ack-{key}");
        var cached = await readerBot.GetFromJsonAsync<PluginHistoryDto>(HistoryUrl(ChannelId, from, to, readKey), Json);
        cached!.Messages.Length.Should().Be(history.Messages.Length);

        var wide = await readerBot.GetAsync(HistoryUrl(ChannelId, start.AddHours(-3), end, $"wide-{Guid.NewGuid():N}"));
        wide.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await wide.Content.ReadAsStringAsync()).Should().Contain(PluginPlatformErrors.HistoryWindowDenied);

        var hidden = await readerBot.GetAsync(HistoryUrl(Guid.NewGuid(), start, end, $"hidden-{Guid.NewGuid():N}"));
        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await hidden.Content.ReadAsStringAsync()).Should().Contain(PluginPlatformErrors.HistoryGrantNotFound);

        var revoked = await admin.DeleteAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/plugins/{reader.Id}/history-grants/{granted!.Id}");
        revoked.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var after = await readerBot.GetAsync(HistoryUrl(ChannelId, from, to, $"after-{Guid.NewGuid():N}"));
        after.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using (var db = factory.CreateMigratorDbContext())
        {
            var counter = await db.PluginQuotaCounters.IgnoreQueryFilters()
                .SingleAsync(item => item.InstalledPluginId == created.Id && item.Capability == PluginManifestRules.CapabilityMessagesInteract);
            counter.Count = PluginGovernedRules.DailyLimit(PluginManifestRules.CapabilityMessagesInteract);
            await db.SaveChangesAsync();
        }

        var blocked = await bot.PostAsJsonAsync(
            $"/api/v1/integrations/v1/channels/{ChannelId}/interactive-messages",
            new { body = "over-quota", actions = new[] { "ack" }, idempotencyKey = $"over-{Guid.NewGuid():N}" });
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        (await admin.DeleteAsync($"/api/v1/admin/workspaces/{WorkspaceId}/plugins/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.DeleteAsync($"/api/v1/admin/workspaces/{WorkspaceId}/plugins/{reader.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Plugin_upload_rejects_script_and_sends_a_plain_file()
    {
        using var admin = Client("demo");
        await RemoveSlugAsync("file-bridge");
        const string manifest = """
            {"schema":"vibechat.plugin.manifest.v1","id":"file-bridge","name":"File Bridge","version":"1.0.0","capabilities":["messages.send","attachments.send"]}
            """;
        var created = await InstallAsync(admin, manifest);
        using var bot = BotClient(created.Token);
        var script = await bot.PostAsJsonAsync(
            $"/api/v1/integrations/v1/channels/{ChannelId}/attachments",
            new { fileName = "payload.js", contentType = "text/javascript", sizeBytes = 12, idempotencyKey = $"js-{Guid.NewGuid():N}" });
        script.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await script.Content.ReadAsStringAsync()).Should().Contain(PluginPlatformErrors.AttachmentRejected);

        var dll = await bot.PostAsJsonAsync(
            $"/api/v1/integrations/v1/channels/{ChannelId}/attachments",
            new { fileName = "payload.dll", contentType = "text/plain", sizeBytes = 4, idempotencyKey = $"dll-{Guid.NewGuid():N}" });
        dll.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var bytes = "nota"u8.ToArray();
        var initiate = await bot.PostAsJsonAsync(
            $"/api/v1/integrations/v1/channels/{ChannelId}/attachments",
            new { fileName = "nota.txt", contentType = "text/plain", sizeBytes = bytes.Length, idempotencyKey = $"up-{Guid.NewGuid():N}" });
        initiate.StatusCode.Should().Be(HttpStatusCode.OK, await initiate.Content.ReadAsStringAsync());
        var upload = await initiate.Content.ReadFromJsonAsync<UploadDto>(Json);
        using var put = new HttpRequestMessage(HttpMethod.Put, upload!.UploadUrl)
        {
            Content = new ByteArrayContent(bytes)
        };
        (await new HttpClient().SendAsync(put)).IsSuccessStatusCode.Should().BeTrue();
        var complete = await bot.PostAsync(
            $"/api/v1/integrations/v1/channels/{ChannelId}/attachments/{upload.AttachmentId}/complete",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        complete.StatusCode.Should().Be(HttpStatusCode.OK, await complete.Content.ReadAsStringAsync());

        var sent = await bot.PostAsJsonAsync(
            $"/api/v1/integrations/v1/channels/{ChannelId}/messages",
            new { body = "com anexo", idempotencyKey = $"send-{Guid.NewGuid():N}", attachmentIds = new[] { upload.AttachmentId } });
        sent.StatusCode.Should().Be(HttpStatusCode.Accepted, await sent.Content.ReadAsStringAsync());

        await admin.DeleteAsync($"/api/v1/admin/workspaces/{WorkspaceId}/plugins/{created.Id}");
    }

    private static string HistoryUrl(Guid channelId, DateTimeOffset from, DateTimeOffset to, string key) =>
        $"/api/v1/integrations/v1/channels/{channelId}/history?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}&idempotencyKey={key}";

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private HttpClient BotClient(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<InstalledDto> InstallAsync(HttpClient admin, string? manifest = null)
    {
        var body = manifest is null
            ? (object)new { builtinId = PluginManifestRules.BuiltinActionAckId, channelIds = new[] { ChannelId }, allowDms = false }
            : new { manifest = JsonSerializer.Deserialize<JsonElement>(manifest), channelIds = new[] { ChannelId }, allowDms = false };
        var response = await admin.PostAsJsonAsync($"/api/v1/admin/workspaces/{WorkspaceId}/plugins", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<InstalledDto>(Json))!;
    }

    private async Task RemoveSlugAsync(string slug)
    {
        await using var db = factory.CreateMigratorDbContext();
        var rows = await db.InstalledPlugins.IgnoreQueryFilters()
            .Where(plugin => plugin.WorkspaceId == SeedData.DemoWorkspaceId && plugin.PluginId == slug)
            .ToListAsync();
        if (rows.Count == 0)
        {
            return;
        }

        var ids = rows.Select(plugin => plugin.Id).ToArray();
        db.PluginHistoryGrants.RemoveRange(await db.PluginHistoryGrants.IgnoreQueryFilters().Where(grant => ids.Contains(grant.InstalledPluginId)).ToListAsync());
        db.PluginInteractiveOffers.RemoveRange(await db.PluginInteractiveOffers.IgnoreQueryFilters().Where(offer => ids.Contains(offer.InstalledPluginId)).ToListAsync());
        db.PluginActionReceipts.RemoveRange(await db.PluginActionReceipts.IgnoreQueryFilters().Where(receipt => ids.Contains(receipt.InstalledPluginId)).ToListAsync());
        db.PluginQuotaCounters.RemoveRange(await db.PluginQuotaCounters.IgnoreQueryFilters().Where(counter => ids.Contains(counter.InstalledPluginId)).ToListAsync());
        db.InstalledPlugins.RemoveRange(rows);
        await db.SaveChangesAsync();
    }

    private sealed record InstalledDto(Guid Id, string Token, string[] Capabilities);
    private sealed record InteractiveDto(Guid MessageId, long Sequence, bool Idempotent);
    private sealed record ActionDto(string Status, bool Idempotent);
    private sealed record GrantDto(Guid Id);
    private sealed record HistoryDto(HistoryMessage[] Messages);
    private sealed record HistoryMessage(Guid Id, string Body, PluginCardDto? PluginCard);
    private sealed record PluginCardDto(string PluginId, string[] Actions);
    private sealed record PluginHistoryDto(PluginHistoryItemDto[] Messages);
    private sealed record PluginHistoryItemDto(Guid Id, string Body);
    private sealed record UploadDto(Guid AttachmentId, string UploadUrl);
}
