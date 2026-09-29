using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Audit;
using VibeChat.Infrastructure;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-040 — channel guest invites: create, accept, revoke, expiry, single use.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class GuestInviteIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid ChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Admin_creates_invite_guest_accepts_and_sends_then_revoke_blocks()
    {
        using var admin = Client("demo");
        var created = await CreateInviteAsync(admin);

        using var guest = GuestClient("guest-ok");
        var accept = await guest.PostAsync($"/api/v1/invites/{created.Token}/accept", null);
        accept.StatusCode.Should().Be(HttpStatusCode.OK);
        var accepted = (await accept.Content.ReadFromJsonAsync<AcceptDto>(Json))!;
        accepted.ChannelId.Should().Be(ChannelId);

        var me = await guest.GetFromJsonAsync<MeDto>("/api/v1/me", Json);
        me!.Roles.Should().Contain("Guest");

        var workspaces = await guest.GetFromJsonAsync<WorkspaceDto[]>("/api/v1/workspaces", Json);
        workspaces.Should().ContainSingle(x => x.Id == WorkspaceId && x.Role == "Guest");

        var channels = await guest.GetFromJsonAsync<ChannelDto[]>(
            $"/api/v1/workspaces/{WorkspaceId}/channels", Json);
        channels.Should().ContainSingle(x => x.Id == ChannelId);
        channels![0].HasGuests.Should().BeTrue();

        var messageId = Guid.NewGuid();
        var send = await guest.PostAsJsonAsync(
            $"/api/v1/channels/{ChannelId}/messages",
            new SendDto(messageId, $"idem-guest-{messageId:N}", "olá do convidado", null, null));
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var react = await guest.PutAsJsonAsync(
            $"/api/v1/channels/{ChannelId}/messages/{messageId}/reactions",
            new { emoji = "👍" });
        react.EnsureSuccessStatusCode();

        var reuse = await guest.PostAsync($"/api/v1/invites/{created.Token}/accept", null);
        reuse.StatusCode.Should().Be(HttpStatusCode.Gone);

        var revoke = await admin.DeleteAsync($"/api/v1/invites/{created.Id}");
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await guest.GetAsync($"/api/v1/channels/{ChannelId}/messages");
        after.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var db = factory.CreateMigratorDbContext();
        var actions = await db.AuditEvents.IgnoreQueryFilters()
            .Where(x => x.EntityId == created.Id.ToString())
            .Select(x => x.Action)
            .ToListAsync();
        actions.Should().Contain(AuditActions.GuestInvite);
        actions.Should().Contain(AuditActions.GuestAccept);
        actions.Should().Contain(AuditActions.GuestRevoke);
    }

    [Fact]
    public async Task Expired_and_unknown_tokens_return_the_same_410()
    {
        using var admin = Client("demo");
        var created = await CreateInviteAsync(admin);
        await using var db = factory.CreateMigratorDbContext();

        var invite = await db.ChannelInvites.IgnoreQueryFilters()
            .FirstAsync(x => x.Id == created.Id);
        invite.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        using var guest = GuestClient("guest-exp");
        var expired = await guest.PostAsync($"/api/v1/invites/{created.Token}/accept", null);
        var unknown = await guest.PostAsync($"/api/v1/invites/{new string('a', 64)}/accept", null);
        expired.StatusCode.Should().Be(HttpStatusCode.Gone);
        unknown.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await expired.Content.ReadAsStringAsync()).Should().Be(await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Invite_list_never_includes_the_raw_token()
    {
        using var admin = Client("demo");
        var created = await CreateInviteAsync(admin);
        var list = await admin.GetFromJsonAsync<InvitesPageDto>(
            $"/api/v1/workspaces/{WorkspaceId}/channels/{ChannelId}/invites", Json);
        list!.Invites.Should().Contain(x => x.Id == created.Id);
        var raw = await admin.GetStringAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels/{ChannelId}/invites");
        raw.Should().NotContain(created.Token);
        raw.ToLowerInvariant().Should().NotContain("tokenhash");
        created.Url.Should().StartWith("/invite/");
    }

    private async Task<CreatedInvite> CreateInviteAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels/{ChannelId}/invites",
            new { email = (string?)null, expiresInDays = 7 });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = (await response.Content.ReadFromJsonAsync<CreatedDto>(Json))!;
        var token = body.Url.Split('/').Last();
        return new CreatedInvite(body.Id, body.Url, token);
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private HttpClient GuestClient(string suffix)
    {
        var client = factory.CreateClient();
        var id = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Dev-User", $"guest-{suffix}-{id:N}");
        client.DefaultRequestHeaders.Add("X-Dev-Email", $"guest-{suffix}-{id:N}@vibechat.local");
        return client;
    }

    private sealed record CreatedInvite(Guid Id, string Url, string Token);
    private sealed record CreatedDto(Guid Id, string Url, DateTimeOffset ExpiresAt);
    private sealed record AcceptDto(Guid ChannelId, Guid WorkspaceId, string ChannelName);
    private sealed record MeDto(Guid Id, string[] Roles);
    private sealed record WorkspaceDto(Guid Id, string Name, string Slug, string Role);
    private sealed record ChannelDto(Guid Id, string Name, string Type, bool HasGuests);
    private sealed record InvitesPageDto(InviteRow[] Invites);
    private sealed record InviteRow(Guid Id, string? Email, string Status);
    private sealed record SendDto(Guid MessageId, string IdempotencyKey, string Body, Guid[]? AttachmentIds, Guid? ReplyToMessageId);
}
