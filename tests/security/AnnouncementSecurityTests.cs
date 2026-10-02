using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using VibeChat.Conversations;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

[Collection(SecurityCollection.Name)]
public sealed class AnnouncementSecurityTests(VibeChatApiFactory factory)
{
    [Fact]
    public async Task Member_reads_but_cannot_publish_or_report()
    {
        using var demo = factory.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var create = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels",
            new { name = $"nope-{Guid.NewGuid():N}"[..20], type = "Announcement" });
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var name = $"sec-anuncio-{Guid.NewGuid():N}"[..24];
        var channelResponse = await demo.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels",
            new { name, type = "Announcement" });
        channelResponse.EnsureSuccessStatusCode();
        var channel = await channelResponse.Content.ReadFromJsonAsync<ChannelDto>();

        var messageId = Guid.NewGuid();
        var published = await demo.PostAsJsonAsync(
            $"/api/v1/channels/{channel!.Id}/messages",
            new SendMessageRequest(messageId, $"sec-ann-{messageId:N}", "oficial", null, null, RequiresAcknowledgement: true));
        published.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var read = await alice.GetAsync($"/api/v1/channels/{channel.Id}/messages");
        read.StatusCode.Should().Be(HttpStatusCode.OK);

        var publish = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{channel.Id}/messages",
            new SendMessageRequest(Guid.NewGuid(), $"sec-ann-deny-{Guid.NewGuid():N}", "nao", null, null));
        publish.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var report = await alice.GetAsync($"/api/v1/channels/{channel.Id}/messages/{messageId}/acknowledgements");
        report.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Report_does_not_cross_tenant()
    {
        using var warmup = factory.CreateClient();
        (await warmup.GetAsync("/health")).EnsureSuccessStatusCode();
        var (foreignChannelId, foreignMessageId) = await SeedForeignAnnouncementAsync();

        using var demo = factory.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var aliceReport = await alice.GetAsync(
            $"/api/v1/channels/{foreignChannelId}/messages/{foreignMessageId}/acknowledgements");
        aliceReport.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var aliceAck = await alice.PostAsync(
            $"/api/v1/channels/{foreignChannelId}/messages/{foreignMessageId}/acknowledgements",
            null);
        aliceAck.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var demoReport = await demo.GetAsync(
            $"/api/v1/channels/{foreignChannelId}/messages/{foreignMessageId}/acknowledgements");
        demoReport.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<(Guid ChannelId, Guid MessageId)> SeedForeignAnnouncementAsync()
    {
        await using var db = factory.CreateMigratorDbContext();
        var now = DateTimeOffset.UtcNow;
        var workspaceId = WorkspaceId.New();
        var tenantId = new TenantId(workspaceId.Value);
        var channelId = ChannelId.New();
        var messageId = MessageId.New();
        var owner = UserId.New();

        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = tenantId,
            Name = "Foreign announcements",
            Slug = $"ann-{workspaceId.Value:N}"[..32],
            CreatedAt = now
        });
        db.UserProfiles.Add(new UserProfile
        {
            Id = owner,
            Subject = $"foreign-ann-{owner.Value:N}",
            Email = $"{owner.Value:N}@example.test",
            DisplayName = "Foreign",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            UserId = owner,
            Role = Role.WorkspaceOwner,
            JoinedAt = now
        });
        db.Channels.Add(new Channel
        {
            Id = channelId,
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            Name = "anuncios",
            Type = ChannelType.Announcement,
            CreatedAt = now,
            CreatedBy = owner
        });
        db.Messages.Add(new Message
        {
            Id = messageId,
            TenantId = tenantId,
            ConversationId = channelId,
            Sequence = 1,
            AuthorId = owner,
            Body = "secreto",
            CreatedAt = now
        });
        db.Announcements.Add(new Announcement
        {
            MessageId = messageId,
            TenantId = tenantId,
            ChannelId = channelId,
            CreatedByUserId = owner,
            RequiresAcknowledgement = true,
            CreatedAt = now
        });
        db.AnnouncementAcknowledgements.Add(new AnnouncementAcknowledgement
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            MessageId = messageId,
            ChannelId = channelId,
            UserId = owner,
            AcknowledgedAt = now
        });
        await db.SaveChangesAsync();
        return (channelId.Value, messageId.Value);
    }

    private sealed record ChannelDto(Guid Id);
}
