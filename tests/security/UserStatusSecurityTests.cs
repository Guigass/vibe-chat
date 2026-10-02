using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

[Collection(SecurityCollection.Name)]
public sealed class UserStatusSecurityTests(VibeChatApiFactory factory)
{
    [Fact]
    public async Task Outsider_cannot_read_another_workspaces_status()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        (await alice.GetAsync("/health")).EnsureSuccessStatusCode();
        var foreignWorkspaceId = await SeedForeignStatusAsync("segredo-de-outro-tenant");

        var foreign = await alice.GetAsync($"/api/v1/workspaces/{foreignWorkspaceId}/availability");
        foreign.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var home = await alice.GetFromJsonAsync<MemberDto[]>(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/availability");
        var payload = System.Text.Json.JsonSerializer.Serialize(home);
        payload.Should().NotContain("segredo-de-outro-tenant");
    }

    [Fact]
    public async Task Reconnect_receives_the_current_status_without_agenda()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        await alice.DeleteAsync("/api/v1/me/status");

        await using var hub = CreateHubConnection("bob");
        var received = new TaskCompletionSource<JsonPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<JsonPayload>("UserStatusChanged", payload => received.TrySetResult(payload));
        await hub.StartAsync();
        await hub.InvokeAsync("JoinChannel", SeedData.DemoTenantId.Value, SeedData.DemoChannelId.Value);

        var set = await alice.PutAsJsonAsync("/api/v1/me/status", new { state = "meeting", text = "sala 2" });
        set.EnsureSuccessStatusCode();

        var evt = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        evt.UserId.Should().Be(SeedData.AliceUserId.Value);
        evt.Status!.Text.Should().Be("sala 2");
        evt.Availability.Should().Be("busy");
        var raw = System.Text.Json.JsonSerializer.Serialize(evt);
        raw.Should().NotContain("calendar");
        raw.Should().NotContain("agenda");

        await hub.StopAsync();
        await using var again = CreateHubConnection("bob");
        await again.StartAsync();
        await again.InvokeAsync("JoinChannel", SeedData.DemoTenantId.Value, SeedData.DemoChannelId.Value);
        using var bob = factory.CreateClient();
        bob.DefaultRequestHeaders.Add("X-Dev-User", "bob");
        var list = await bob.GetFromJsonAsync<MemberDto[]>(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/availability");
        list!.Single(x => x.UserId == SeedData.AliceUserId.Value).Status!.Text.Should().Be("sala 2");

        await alice.DeleteAsync("/api/v1/me/status");
    }

    private HubConnection CreateHubConnection(string devUser) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress!, "hubs/chat"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Headers["X-Dev-User"] = devUser;
            })
            .Build();

    private async Task<Guid> SeedForeignStatusAsync(string text)
    {
        await using var db = factory.CreateMigratorDbContext();
        var now = DateTimeOffset.UtcNow;
        var workspaceId = WorkspaceId.New();
        var tenantId = new TenantId(workspaceId.Value);
        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = tenantId,
            Name = $"Status {workspaceId.Value:N}",
            Slug = $"st-{workspaceId.Value:N}",
            AiEnabled = false,
            CreatedAt = now
        });
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            UserId = SeedData.DemoUserId,
            Role = Role.WorkspaceOwner,
            JoinedAt = now
        });
        db.UserStatuses.Add(new UserStatus
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = SeedData.DemoUserId,
            State = UserStatusState.Custom,
            Emoji = "🔒",
            Text = text,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        return workspaceId.Value;
    }

    private sealed record JsonPayload(Guid UserId, string Availability, StatusBody? Status);
    private sealed record StatusBody(string Text);
    private sealed record MemberDto(Guid UserId, string Availability, StatusBody? Status);
}
