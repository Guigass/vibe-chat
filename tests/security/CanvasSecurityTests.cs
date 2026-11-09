using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using VibeChat.Infrastructure;
using VibeChat.Infrastructure.Messaging;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

[Collection(SecurityCollection.Name)]
public sealed class CanvasSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Another_tenant_cannot_read_canvas_state()
    {
        using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Features:CanvasRealtime:Enabled", "true"));
        using var alice = Client(host, "alice");
        var page = await CreatePageAsync(alice, "segredo do canvas");
        await alice.GetAsync($"/api/v1/workspaces/{WorkspaceId}/knowledge/pages/{page.Id}/canvas");

        var foreign = Guid.NewGuid();
        var read = await alice.GetAsync($"/api/v1/workspaces/{foreign}/knowledge/pages/{page.Id}/canvas");
        read.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await read.Content.ReadAsStringAsync()).Should().NotContain("segredo do canvas");
    }

    [Fact]
    public async Task Removed_channel_member_cannot_publish()
    {
        using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Features:CanvasRealtime:Enabled", "true"));
        using var alice = Client(host, "alice");
        using var bob = Client(host, "bob");
        var created = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels",
            new { name = $"cv-{Guid.NewGuid():N}"[..12], type = "Private" });
        created.EnsureSuccessStatusCode();
        var channelId = (await created.Content.ReadFromJsonAsync<IdDto>(JsonOptions))!.Id;
        (await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels/{channelId}/members",
            new { userId = SeedData.BobUserId.Value })).StatusCode.Should().Be(HttpStatusCode.OK);

        var collection = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/collections",
            new
            {
                idempotencyKey = $"c-{Guid.NewGuid():N}",
                title = $"priv-{Guid.NewGuid():N}"[..12],
                scope = "channel",
                scopeId = channelId,
            });
        collection.StatusCode.Should().Be(HttpStatusCode.OK, await collection.Content.ReadAsStringAsync());
        var collectionId = (await collection.Content.ReadFromJsonAsync<IdDto>(JsonOptions))!.Id;
        var pageResponse = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/pages",
            new { idempotencyKey = $"p-{Guid.NewGuid():N}", collectionId, title = "Sala", body = "base" });
        pageResponse.EnsureSuccessStatusCode();
        var page = (await pageResponse.Content.ReadFromJsonAsync<IdDto>(JsonOptions))!;
        var opened = await bob.GetFromJsonAsync<StateDto>(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/pages/{page.Id}/canvas", JsonOptions);
        opened!.Body.Should().Be("base");

        (await alice.DeleteAsync($"/api/v1/workspaces/{WorkspaceId}/channels/{channelId}/members/{SeedData.BobUserId.Value}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var origin = Convert.FromBase64String(opened.StateBase64);
        var update = Convert.ToBase64String(CanvasCrdt.Insert(origin, 1004, (uint)"base".Length, " vazou"));
        var posted = await bob.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/pages/{page.Id}/canvas/updates",
            new { wireVersion = CanvasPolicies.WireVersion, idempotencyKey = "removed", epoch = opened.Epoch, updateBase64 = update });
        posted.IsSuccessStatusCode.Should().BeFalse();
        (await posted.Content.ReadAsStringAsync()).Should().NotContain("vazou");

        var current = await alice.GetFromJsonAsync<StateDto>(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/pages/{page.Id}/canvas", JsonOptions);
        current!.Body.Should().Be("base");
    }

    private static HttpClient Client(WebApplicationFactory<Program> host, string user)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private static async Task<IdDto> CreatePageAsync(HttpClient client, string body)
    {
        var collection = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/collections",
            new { idempotencyKey = $"c-{Guid.NewGuid():N}", title = $"sec-{Guid.NewGuid():N}"[..12], scope = "workspace" });
        collection.EnsureSuccessStatusCode();
        var collectionId = (await collection.Content.ReadFromJsonAsync<IdDto>(JsonOptions))!.Id;
        var page = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/pages",
            new { idempotencyKey = $"p-{Guid.NewGuid():N}", collectionId, title = "Sec", body });
        page.EnsureSuccessStatusCode();
        return (await page.Content.ReadFromJsonAsync<IdDto>(JsonOptions))!;
    }

    private sealed record IdDto(Guid Id, int Epoch = 0, string StateBase64 = "", string Body = "");

    private sealed record StateDto(int Epoch, string StateBase64, string Body);
}
