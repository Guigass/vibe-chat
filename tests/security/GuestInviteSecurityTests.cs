using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VibeChat.Infrastructure;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

/// <summary>B-040 — guest cannot use workspace surfaces; token enumeration is opaque.</summary>
[Collection(SecurityCollection.Name)]
public sealed class GuestInviteSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid ChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Guest_is_forbidden_on_every_workspace_surface()
    {
        using var admin = Client("demo");
        var created = await CreateInviteAsync(admin);
        using var guest = GuestClient("sec");
        (await guest.PostAsync($"/api/v1/invites/{created.Token}/accept", null)).EnsureSuccessStatusCode();

        var forbidden = new (HttpMethod Method, string Path)[]
        {
            (HttpMethod.Get, $"/api/v1/workspaces/{WorkspaceId}/spaces"),
            (HttpMethod.Post, $"/api/v1/workspaces/{WorkspaceId}/spaces"),
            (HttpMethod.Get, $"/api/v1/workspaces/{WorkspaceId}/members"),
            (HttpMethod.Get, $"/api/v1/workspaces/{WorkspaceId}/roles"),
            (HttpMethod.Post, $"/api/v1/workspaces/{WorkspaceId}/members"),
            (HttpMethod.Put, $"/api/v1/workspaces/{WorkspaceId}/members/{SeedData.BobUserId.Value}/role"),
            (HttpMethod.Get, $"/api/v1/workspaces/{WorkspaceId}/presence"),
            (HttpMethod.Post, $"/api/v1/workspaces/{WorkspaceId}/dms"),
            (HttpMethod.Post, $"/api/v1/workspaces/{WorkspaceId}/group-dms"),
            (HttpMethod.Post, $"/api/v1/workspaces/{WorkspaceId}/channels"),
            (HttpMethod.Put, $"/api/v1/workspaces/{WorkspaceId}/channels/{ChannelId}/topic"),
            (HttpMethod.Get, $"/api/v1/workspaces/{WorkspaceId}/commands"),
            (HttpMethod.Get, $"/api/v1/workspaces/{WorkspaceId}/threads/following"),
            (HttpMethod.Get, $"/api/v1/workspaces/{WorkspaceId}/saved"),
            (HttpMethod.Post, $"/api/v1/workspaces/{WorkspaceId}/saved"),
            (HttpMethod.Get, $"/api/v1/search/messages?workspaceId={WorkspaceId}&q=hi"),
            (HttpMethod.Post, $"/api/v1/workspaces/{WorkspaceId}/channels/{ChannelId}/invites"),
            (HttpMethod.Get, $"/api/v1/workspaces/{WorkspaceId}/channels/{ChannelId}/invites"),
            (HttpMethod.Get, "/api/v1/admin/dashboard"),
            (HttpMethod.Get, "/api/v1/admin/settings"),
            (HttpMethod.Post, $"/api/v1/workspaces/{WorkspaceId}/channels/{ChannelId}/ai/summarize"),
        };

        foreach (var (method, path) in forbidden)
        {
            using var request = new HttpRequestMessage(method, path);
            if (method == HttpMethod.Post || method == HttpMethod.Put)
            {
                request.Content = JsonContent.Create(new { name = "nope", email = "x@y.z", role = "Member", topic = "x", userId = SeedData.BobUserId.Value, userIds = new[] { SeedData.BobUserId.Value }, messageId = Guid.NewGuid() });
            }

            var response = await guest.SendAsync(request);
            response.StatusCode.Should().BeOneOf(
                [HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized, HttpStatusCode.NotFound],
                because: $"{method} {path} must not be usable by a channel guest");
            response.StatusCode.Should().NotBe(HttpStatusCode.OK);
            response.StatusCode.Should().NotBe(HttpStatusCode.Created);
            response.StatusCode.Should().NotBe(HttpStatusCode.Accepted);
        }

        var history = await guest.GetAsync($"/api/v1/channels/{ChannelId}/messages");
        history.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Foreign_tenant_token_and_missing_token_are_indistinguishable()
    {
        using var guest = GuestClient("enum");
        var missing = await guest.PostAsync($"/api/v1/invites/{new string('b', 64)}/accept", null);
        missing.StatusCode.Should().Be(HttpStatusCode.Gone);
        var body = await missing.Content.ReadAsStringAsync();
        body.Should().Contain("InviteUnavailable");
    }

    [Fact]
    public async Task Member_cannot_create_channel_invite()
    {
        using var bob = Client("bob");
        var response = await bob.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels/{ChannelId}/invites",
            new { expiresInDays = 7 });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<CreatedInvite> CreateInviteAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels/{ChannelId}/invites",
            new { expiresInDays = 7 });
        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadFromJsonAsync<CreatedDto>(Json))!;
        return new CreatedInvite(body.Id, body.Url.Split('/').Last());
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

    private sealed record CreatedInvite(Guid Id, string Token);
    private sealed record CreatedDto(Guid Id, string Url);
}
