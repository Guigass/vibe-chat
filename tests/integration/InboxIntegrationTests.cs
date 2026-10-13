using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class InboxIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid ChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Kinds_are_unique_and_ordered_and_read_cursor_is_shared()
    {
        using var demo = Client("demo");
        using var alice = Client("alice");
        using var bob = Client("bob");

        var announcementName = $"anuncio-{Guid.NewGuid():N}"[..20];
        var announcementChannel = await CreateChannelAsync(demo, announcementName, "Announcement");
        var announcementMessage = Guid.NewGuid();
        var published = await demo.PostAsJsonAsync(
            $"/api/v1/channels/{announcementChannel}/messages",
            new SendMessageRequest(announcementMessage, $"idem-{announcementMessage:N}", "leiam", null, null, true));
        published.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var mentionId = Guid.NewGuid();
        var mentionBody = $"inbox-{mentionId:N} {MentionTokens.UserBodyToken(SeedData.BobUserId)}";
        var mention = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{ChannelId}/messages",
            new SendMessageRequest(mentionId, $"idem-{mentionId:N}", mentionBody, null, null));
        mention.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var mentionDto = (await mention.Content.ReadFromJsonAsync<MessageDto>(JsonOptions))!;

        var direct = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/dms",
            new { userId = SeedData.BobUserId.Value });
        direct.EnsureSuccessStatusCode();
        var directChannel = (await direct.Content.ReadFromJsonAsync<IdDto>(JsonOptions))!;
        var dmId = Guid.NewGuid();
        var dm = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{directChannel.Id}/messages",
            new SendMessageRequest(dmId, $"idem-{dmId:N}", $"dm-{dmId:N}", null, null));
        dm.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var rootId = Guid.NewGuid();
        var root = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{ChannelId}/messages",
            new SendMessageRequest(rootId, $"idem-{rootId:N}", $"root-{rootId:N}", null, null));
        root.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var open = await bob.PostAsJsonAsync($"/api/v1/channels/{ChannelId}/messages/{rootId}/threads", new { });
        open.EnsureSuccessStatusCode();
        var thread = (await open.Content.ReadFromJsonAsync<IdDto>(JsonOptions))!;
        (await bob.PostAsync($"/api/v1/threads/{thread.Id}/subscription", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var replyId = Guid.NewGuid();
        var reply = await alice.PostAsJsonAsync(
            $"/api/v1/threads/{thread.Id}/messages",
            new SendMessageRequest(replyId, $"idem-{replyId:N}", $"reply-{replyId:N}", null, thread.Id));
        reply.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var savedId = Guid.NewGuid();
        var savedSend = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{ChannelId}/messages",
            new SendMessageRequest(savedId, $"idem-{savedId:N}", $"saved-{savedId:N}", null, null));
        savedSend.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var save = await bob.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/saved",
            new { messageId = savedId });
        save.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await GetInboxAsync(bob, "open");
        var keys = new[]
        {
            InboxPolicies.ItemKey(InboxItemKind.Announcement, announcementMessage),
            InboxPolicies.ItemKey(InboxItemKind.Mention, mentionId),
            InboxPolicies.ItemKey(InboxItemKind.Direct, directChannel.Id),
            InboxPolicies.ItemKey(InboxItemKind.Thread, thread.Id),
            InboxPolicies.ItemKey(InboxItemKind.Saved, savedId),
        };
        var indexes = keys.Select(key => IndexOf(page, key)).ToArray();
        indexes.Should().OnlyContain(index => index >= 0);
        indexes.Should().BeInAscendingOrder();
        page.Items.Select(item => item.ItemKey).Should().OnlyHaveUniqueItems();
        page.Items[indexes[1]].Reason.Should().Be("mention");
        page.Items[indexes[1]].Navigation.MessageId.Should().Be(mentionId);
        page.Items[indexes[1]].Navigation.ChannelId.Should().Be(ChannelId);
        page.Counts.Total.Should().BeGreaterThanOrEqualTo(5);

        var marked = await bob.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/inbox/read",
            new { itemKey = keys[1] });
        marked.StatusCode.Should().Be(HttpStatusCode.OK);
        var again = await bob.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/inbox/read",
            new { itemKey = keys[1] });
        again.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var afterRead = await GetInboxAsync(bob, "open");
        IndexOf(afterRead, keys[1]).Should().Be(-1);

        var unread = await bob.PutAsJsonAsync(
            $"/api/v1/channels/{ChannelId}/read-cursor",
            new { lastReadSequence = mentionDto.Sequence - 1, allowRetrograde = true });
        unread.StatusCode.Should().Be(HttpStatusCode.OK);
        var restored = await GetInboxAsync(bob, "open");
        IndexOf(restored, keys[1]).Should().BeGreaterThanOrEqualTo(0);

        var until = DateTimeOffset.UtcNow.AddHours(4);
        var snooze = await bob.PutAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/inbox/snooze",
            new { itemKey = keys[1], until });
        snooze.StatusCode.Should().Be(HttpStatusCode.OK);
        var repeat = await bob.PutAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/inbox/snooze",
            new { itemKey = keys[1], until });
        repeat.StatusCode.Should().Be(HttpStatusCode.OK);

        IndexOf(await GetInboxAsync(bob, "open"), keys[1]).Should().Be(-1);
        var snoozed = await GetInboxAsync(bob, "snoozed");
        var snoozedItem = snoozed.Items.Should().ContainSingle(item => item.ItemKey == keys[1]).Subject;
        snoozedItem.SnoozedUntil.Should().NotBeNull();

        await using (var db = factory.CreateMigratorDbContext())
        {
            var row = await db.InboxSnoozes.IgnoreQueryFilters()
                .SingleAsync(item => item.UserId == SeedData.BobUserId && item.ItemKey == keys[1]);
            row.SnoozeUntil = DateTimeOffset.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }

        IndexOf(await GetInboxAsync(bob, "open"), keys[1]).Should().BeGreaterThanOrEqualTo(0);
        IndexOf(await GetInboxAsync(bob, "snoozed"), keys[1]).Should().Be(-1);

        var savedRead = await bob.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/inbox/read",
            new { itemKey = keys[4] });
        savedRead.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Page_cursor_does_not_repeat_an_item_when_a_newer_mention_arrives()
    {
        using var alice = Client("alice");
        using var bob = Client("bob");

        var first = await MentionAsync(alice, bob);
        var second = await MentionAsync(alice, bob);
        var third = await MentionAsync(alice, bob);

        var page = await bob.GetFromJsonAsync<InboxPageDto>(
            $"/api/v1/workspaces/{WorkspaceId}/inbox?type=mention&limit=1",
            JsonOptions);
        page!.Items.Should().ContainSingle();
        page.Items[0].ItemKey.Should().Be(InboxPolicies.ItemKey(InboxItemKind.Mention, third));
        page.NextCursor.Should().NotBeNullOrWhiteSpace();

        var fourth = await MentionAsync(alice, bob);
        var next = await bob.GetFromJsonAsync<InboxPageDto>(
            $"/api/v1/workspaces/{WorkspaceId}/inbox?type=mention&limit=10&cursor={Uri.EscapeDataString(page.NextCursor!)}",
            JsonOptions);
        var keys = next!.Items.Select(item => item.ItemKey).ToArray();
        keys.Should().Contain(InboxPolicies.ItemKey(InboxItemKind.Mention, second));
        keys.Should().Contain(InboxPolicies.ItemKey(InboxItemKind.Mention, first));
        keys.Should().NotContain(InboxPolicies.ItemKey(InboxItemKind.Mention, third));
        keys.Should().NotContain(InboxPolicies.ItemKey(InboxItemKind.Mention, fourth));
        keys.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Revoked_channel_access_drops_the_item_without_its_preview()
    {
        using var alice = Client("alice");
        using var bob = Client("bob");
        var name = $"sec-{Guid.NewGuid():N}"[..18];
        var channelId = await CreateChannelAsync(alice, name, "Private");
        var add = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels/{channelId}/members",
            new { userId = SeedData.BobUserId.Value });
        add.StatusCode.Should().Be(HttpStatusCode.OK);

        var messageId = await MentionAsync(alice, bob, channelId);
        var key = InboxPolicies.ItemKey(InboxItemKind.Mention, messageId);
        var visible = await GetInboxAsync(bob, "open");
        var item = visible.Items.Should().ContainSingle(row => row.ItemKey == key).Subject;
        item.ChannelName.Should().Be(name);
        item.BodyPreview.Should().NotBeNullOrWhiteSpace();

        var remove = await alice.DeleteAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels/{channelId}/members/{SeedData.BobUserId.Value}");
        remove.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var body = new StringBuilder();
        string? cursor = null;
        for (var pageIndex = 0; pageIndex < 6; pageIndex++)
        {
            var url = $"/api/v1/workspaces/{WorkspaceId}/inbox?state=all&limit=50";
            if (!string.IsNullOrEmpty(cursor))
            {
                url += $"&cursor={Uri.EscapeDataString(cursor)}";
            }

            var hidden = await bob.GetAsync(url);
            hidden.EnsureSuccessStatusCode();
            var pageBody = await hidden.Content.ReadAsStringAsync();
            body.Append(pageBody);
            using var document = JsonDocument.Parse(pageBody);
            if (!document.RootElement.TryGetProperty("nextCursor", out var next) || next.ValueKind == JsonValueKind.Null)
            {
                break;
            }

            cursor = next.GetString();
            if (string.IsNullOrEmpty(cursor))
            {
                break;
            }
        }

        var serialized = body.ToString();
        serialized.Should().NotContain(key);
        serialized.Should().NotContain(name);
        serialized.Should().NotContain(messageId.ToString());
    }

    private static async Task<Guid> MentionAsync(HttpClient alice, HttpClient bob, Guid? channelId = null)
    {
        _ = bob;
        var messageId = Guid.NewGuid();
        var send = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{channelId ?? ChannelId}/messages",
            new SendMessageRequest(
                messageId,
                $"idem-{messageId:N}",
                $"ping-{messageId:N} {MentionTokens.UserBodyToken(SeedData.BobUserId)}",
                null,
                null));
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);
        return messageId;
    }

    private static async Task<Guid> CreateChannelAsync(HttpClient client, string name, string type)
    {
        var create = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels",
            new { name, type });
        create.EnsureSuccessStatusCode();
        var channel = (await create.Content.ReadFromJsonAsync<IdDto>(JsonOptions))!;
        return channel.Id;
    }

    private static async Task<InboxPageDto> GetInboxAsync(HttpClient client, string state)
    {
        var items = new List<InboxItemDto>();
        string? cursor = null;
        InboxCountsDto counts = new(0);
        for (var pageIndex = 0; pageIndex < 6; pageIndex++)
        {
            var url = $"/api/v1/workspaces/{WorkspaceId}/inbox?state={state}&limit=50";
            if (!string.IsNullOrEmpty(cursor))
            {
                url += $"&cursor={Uri.EscapeDataString(cursor)}";
            }

            var page = (await client.GetFromJsonAsync<InboxPageDto>(url, JsonOptions))!;
            items.AddRange(page.Items);
            counts = page.Counts;
            if (string.IsNullOrEmpty(page.NextCursor))
            {
                break;
            }

            cursor = page.NextCursor;
        }

        return new InboxPageDto(items.ToArray(), null, counts);
    }

    private static int IndexOf(InboxPageDto page, string key)
    {
        for (var i = 0; i < page.Items.Length; i++)
        {
            if (page.Items[i].ItemKey == key)
            {
                return i;
            }
        }

        return -1;
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private sealed record SendMessageRequest(
        Guid MessageId,
        string IdempotencyKey,
        string Body,
        Guid? ReplyToMessageId,
        Guid? ThreadId,
        bool RequiresAcknowledgement = false);

    private sealed record IdDto(Guid Id);

    private sealed record MessageDto(Guid Id, long Sequence);

    private sealed record InboxPageDto(InboxItemDto[] Items, string? NextCursor, InboxCountsDto Counts);

    private sealed record InboxCountsDto(int Total);

    private sealed record InboxItemDto(
        string ItemKey,
        string Reason,
        string ChannelName,
        string BodyPreview,
        DateTimeOffset? SnoozedUntil,
        InboxNavigationDto Navigation);

    private sealed record InboxNavigationDto(Guid ChannelId, Guid? MessageId);
}
