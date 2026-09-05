using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Conversations;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class GroupDmIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Create_group_dm_is_idempotent_and_visible_to_three()
    {
        using var alice = Client("alice");
        using var bob = Client("bob");
        using var demo = Client("demo");
        var workspace = SeedData.DemoWorkspaceId.Value;

        var first = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace}/group-dms",
            new { userIds = new[] { SeedData.BobUserId.Value, SeedData.DemoUserId.Value } });
        first.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
        var created = await first.Content.ReadFromJsonAsync<ChannelDto>(Json);
        created.Should().NotBeNull();
        created!.Type.Should().Be("GroupDm");
        created.ParticipantCount.Should().Be(3);

        var again = await bob.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace}/group-dms",
            new { userIds = new[] { SeedData.AliceUserId.Value, SeedData.DemoUserId.Value } });
        again.EnsureSuccessStatusCode();
        var same = await again.Content.ReadFromJsonAsync<ChannelDto>(Json);
        same!.Id.Should().Be(created.Id);

        var messageId = Guid.NewGuid();
        var send = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{created.Id}/messages",
            new SendMessageRequest(messageId, $"gdm-{messageId:N}", "oi grupo", null, null));
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var bobHistory = await bob.GetFromJsonAsync<ChannelMessagesResponseDto>(
            $"/api/v1/channels/{created.Id}/messages?after=0&limit=50", Json);
        bobHistory!.Messages.Should().Contain(m => m.Id == messageId);
        var demoHistory = await demo.GetFromJsonAsync<ChannelMessagesResponseDto>(
            $"/api/v1/channels/{created.Id}/messages?after=0&limit=50", Json);
        demoHistory!.Messages.Should().Contain(m => m.Id == messageId);
    }

    [Fact]
    public async Task Added_member_does_not_see_earlier_history()
    {
        var extra = await SeedWorkspaceUserAsync("gdm-late");
        using var alice = Client("alice");
        var workspace = SeedData.DemoWorkspaceId.Value;

        var open = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace}/group-dms",
            new { userIds = new[] { SeedData.BobUserId.Value, SeedData.DemoUserId.Value }, name = "Janela" });
        open.EnsureSuccessStatusCode();
        var group = await open.Content.ReadFromJsonAsync<ChannelDto>(Json);

        var secretId = Guid.NewGuid();
        (await alice.PostAsJsonAsync(
            $"/api/v1/channels/{group!.Id}/messages",
            new SendMessageRequest(secretId, $"gdm-secret-{secretId:N}", "antes de entrar", null, null)))
            .EnsureSuccessStatusCode();

        var add = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{group.Id}/participants",
            new { userIds = new[] { extra } });
        add.EnsureSuccessStatusCode();

        using var late = factory.CreateClient();
        late.DefaultRequestHeaders.Add("X-Dev-User", extra.ToString("N"));
        late.DefaultRequestHeaders.Add("X-Dev-Email", $"gdm-late-{extra:N}@vibechat.local");

        var history = await late.GetFromJsonAsync<ChannelMessagesResponseDto>(
            $"/api/v1/channels/{group.Id}/messages?after=0&limit=100", Json);
        history!.Messages.Should().NotContain(m => m.Id == secretId);
        history.HasMoreBefore.Should().BeFalse();
        history.Messages.Should().Contain(m => m.Body.Contains("entrou", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Leave_blocks_history_and_ten_participants_are_rejected()
    {
        using var alice = Client("alice");
        var workspace = SeedData.DemoWorkspaceId.Value;
        var extras = new List<Guid>();
        for (var i = 0; i < 8; i++)
        {
            extras.Add(await SeedWorkspaceUserAsync($"gdm-max-{i}"));
        }

        var tooMany = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace}/group-dms",
            new { userIds = extras.Concat([SeedData.BobUserId.Value]).ToArray() });
        tooMany.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var open = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace}/group-dms",
            new { userIds = new[] { SeedData.BobUserId.Value, extras[0] } });
        open.EnsureSuccessStatusCode();
        var group = await open.Content.ReadFromJsonAsync<ChannelDto>(Json);

        var leave = await alice.DeleteAsync($"/api/v1/channels/{group!.Id}/participants/me");
        leave.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var history = await alice.GetAsync($"/api/v1/channels/{group.Id}/messages");
        history.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Rename_updates_title_for_participants()
    {
        using var alice = Client("alice");
        using var bob = Client("bob");
        var workspace = SeedData.DemoWorkspaceId.Value;
        var open = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace}/group-dms",
            new { userIds = new[] { SeedData.BobUserId.Value, SeedData.DemoUserId.Value } });
        var group = await open.Content.ReadFromJsonAsync<ChannelDto>(Json);

        var renamed = await alice.PatchAsJsonAsync(
            $"/api/v1/channels/{group!.Id}",
            new { name = "Ops" });
        renamed.EnsureSuccessStatusCode();
        var after = await renamed.Content.ReadFromJsonAsync<ChannelDto>(Json);
        after!.Name.Should().Be("Ops");

        var list = await bob.GetFromJsonAsync<ChannelDto[]>(
            $"/api/v1/workspaces/{workspace}/channels", Json);
        list!.Should().Contain(c => c.Id == group.Id && c.Name == "Ops");
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private async Task<Guid> SeedWorkspaceUserAsync(string slug)
    {
        await using var db = factory.CreateMigratorDbContext();
        var userId = UserId.New();
        var now = DateTimeOffset.UtcNow;
        db.UserProfiles.Add(new UserProfile
        {
            Id = userId,
            Subject = $"dev:{userId.Value:N}",
            Email = $"{slug}-{userId.Value:N}@vibechat.local",
            DisplayName = slug,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            TenantId = SeedData.DemoTenantId,
            WorkspaceId = SeedData.DemoWorkspaceId,
            UserId = userId,
            Role = Role.Member,
            JoinedAt = now
        });
        await db.SaveChangesAsync();
        return userId.Value;
    }

    private sealed record ChannelDto(
        Guid Id,
        Guid WorkspaceId,
        string Name,
        string Type,
        int? ParticipantCount = null);

    private sealed record ChannelMessagesResponseDto(MessageDto[] Messages, bool HasMoreBefore, bool HasMoreAfter);

    private sealed record MessageDto(Guid Id, string Body, long Sequence);

    private sealed record SendMessageRequest(
        Guid MessageId,
        string IdempotencyKey,
        string Body,
        Guid? ReplyToMessageId,
        Guid? ThreadId);
}
