using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

[Collection(SecurityCollection.Name)]
public sealed class InboxSecurityTests(VibeChatApiFactory factory)
{
    [Fact]
    public async Task Inbox_of_another_tenant_is_forbidden_and_snooze_stays_private()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        using var bob = factory.CreateClient();
        bob.DefaultRequestHeaders.Add("X-Dev-User", "bob");

        await using var db = factory.CreateMigratorDbContext();
        var foreignWorkspace = await db.Workspaces.IgnoreQueryFilters()
            .Where(workspace => workspace.TenantId != SeedData.DemoTenantId)
            .Select(workspace => workspace.Id.Value)
            .FirstOrDefaultAsync();
        if (foreignWorkspace != Guid.Empty)
        {
            var foreign = await alice.GetAsync($"/api/v1/workspaces/{foreignWorkspace}/inbox");
            foreign.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var messageId = Guid.NewGuid();
        var send = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages",
            new SendMessageRequest(
                messageId,
                $"idem-{messageId:N}",
                $"sec-{messageId:N} {MentionTokens.UserBodyToken(SeedData.BobUserId)}",
                null,
                null));
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var key = InboxPolicies.ItemKey(InboxItemKind.Mention, messageId);
        var snooze = await bob.PutAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/inbox/snooze",
            new { itemKey = key, until = DateTimeOffset.UtcNow.AddHours(2) });
        snooze.StatusCode.Should().Be(HttpStatusCode.OK);

        var aliceInbox = await alice.GetAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/inbox?state=snoozed&limit=50");
        aliceInbox.EnsureSuccessStatusCode();
        var body = await aliceInbox.Content.ReadAsStringAsync();
        body.Should().NotContain(key);

        var row = await db.InboxSnoozes.IgnoreQueryFilters().SingleAsync(item => item.ItemKey == key);
        row.UserId.Should().Be(SeedData.BobUserId);
        row.TenantId.Should().Be(SeedData.DemoTenantId);
    }

    private sealed record SendMessageRequest(
        Guid MessageId,
        string IdempotencyKey,
        string Body,
        Guid? ReplyToMessageId,
        Guid? ThreadId);
}
