using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Audit;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class UserStatusIntegrationTests(VibeChatApiFactory factory)
{
    [Fact]
    public async Task Set_clear_and_expired_status_round_trip()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var missing = await alice.GetFromJsonAsync<StatusDto>("/api/v1/me/status");
        missing!.Status.Should().BeNull();

        var created = await alice.PutAsJsonAsync("/api/v1/me/status", new
        {
            state = "focus",
            text = "escrevendo a spec",
            clearAtEndOfDay = false,
            expiresAt = DateTimeOffset.UtcNow.AddHours(2)
        });
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await created.Content.ReadFromJsonAsync<StatusDto>();
        body!.Status.Should().NotBeNull();
        body.Status!.State.Should().Be("focus");
        body.Status.Emoji.Should().Be("🎯");
        body.Status.Text.Should().Be("escrevendo a spec");
        body.Availability.Should().Be("busy");

        await using (var db = factory.CreateMigratorDbContext())
        {
            var row = await db.UserStatuses.IgnoreQueryFilters()
                .SingleAsync(x => x.UserId == SeedData.AliceUserId);
            row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }

        var expired = await alice.GetFromJsonAsync<StatusDto>("/api/v1/me/status");
        expired!.Status.Should().BeNull();

        await using (var db = factory.CreateMigratorDbContext())
        {
            (await db.UserStatuses.IgnoreQueryFilters().AnyAsync(x => x.UserId == SeedData.AliceUserId))
                .Should().BeFalse();
        }
    }

    [Fact]
    public async Task Clear_removes_the_callers_status()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        (await alice.PutAsJsonAsync("/api/v1/me/status", new { state = "custom", emoji = "☕", text = "café" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var cleared = await alice.DeleteAsync("/api/v1/me/status");
        cleared.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await cleared.Content.ReadFromJsonAsync<StatusDto>();
        body!.Status.Should().BeNull();
    }

    [Fact]
    public async Task Workspace_member_sees_peer_status_and_dnd_stays_busy()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        using var bob = factory.CreateClient();
        bob.DefaultRequestHeaders.Add("X-Dev-User", "bob");

        (await alice.PutAsJsonAsync("/api/v1/notifications/preferences", new
        {
            level = "MentionsAndDms",
            hidePreview = false,
            dndEnabled = true,
            dndStart = "00:00:00",
            dndEnd = "23:59:00",
            dndDays = 0,
            timeZone = "UTC",
            digestEnabled = false,
            priorityContactUserIds = Array.Empty<Guid>()
        })).EnsureSuccessStatusCode();

        try
        {
            (await alice.PutAsJsonAsync("/api/v1/me/status", new { state = "custom", text = "parece livre" }))
                .EnsureSuccessStatusCode();

            var list = await bob.GetFromJsonAsync<MemberDto[]>(
                $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/availability");
            var row = list!.Single(x => x.UserId == SeedData.AliceUserId.Value);
            row.Status!.Text.Should().Be("parece livre");
            row.Availability.Should().Be("busy");
            row.Status.Text.Should().NotContain("00:00");
            JsonSerializer.Serialize(row).Should().NotContain("dndStart");
        }
        finally
        {
            await alice.PutAsJsonAsync("/api/v1/notifications/preferences", new
            {
                level = "MentionsAndDms",
                hidePreview = false,
                dndEnabled = false,
                dndStart = (string?)null,
                dndEnd = (string?)null,
                dndDays = 0,
                timeZone = "UTC",
                digestEnabled = false,
                priorityContactUserIds = Array.Empty<Guid>()
            });
            await alice.DeleteAsync("/api/v1/me/status");
        }
    }

    [Fact]
    public async Task Admin_clear_writes_audit_without_status_text()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        using var demo = factory.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");

        const string secret = "texto-que-nao-vai-para-o-audit";
        (await alice.PutAsJsonAsync("/api/v1/me/status", new { state = "custom", text = secret }))
            .EnsureSuccessStatusCode();

        var denied = await bobClear(alice);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var cleared = await demo.DeleteAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/members/{SeedData.AliceUserId.Value}/status");
        cleared.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = factory.CreateMigratorDbContext();
        var audit = await db.AuditEvents.IgnoreQueryFilters()
            .Where(x => x.Action == AuditActions.UserStatusClear && x.EntityId == SeedData.AliceUserId.Value.ToString())
            .OrderByDescending(x => x.OccurredAt)
            .FirstAsync();
        audit.MetadataJson.Should().NotContain(secret);
        audit.ActorUserId.Should().Be(SeedData.DemoUserId);
        (await db.UserStatuses.IgnoreQueryFilters().AnyAsync(x => x.UserId == SeedData.AliceUserId)).Should().BeFalse();
    }

    [Fact]
    public async Task Calendar_hook_is_disabled_and_enabled_mode_has_no_agenda()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        var off = await alice.GetAsync("/api/v1/me/availability/calendar");
        off.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await off.Content.ReadAsStringAsync()).Should().Contain("CalendarIntegrationDisabled");

        using var enabledFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Features:AvailabilityCalendar:Enabled", "true"));
        using var enabled = enabledFactory.CreateClient();
        enabled.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        var on = await enabled.GetAsync("/api/v1/me/availability/calendar");
        on.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await on.Content.ReadAsStringAsync();
        json.Should().Contain("\"connected\":false");
        json.Should().NotContain("events");
        json.Should().NotContain("agenda");
    }

    private static Task<HttpResponseMessage> bobClear(HttpClient alice) =>
        alice.DeleteAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/members/{SeedData.BobUserId.Value}/status");

    private sealed record StatusDto(string Presence, string Availability, StatusBody? Status);
    private sealed record StatusBody(string State, string Emoji, string Text, bool ClearAtEndOfDay, DateTimeOffset? ExpiresAt);
    private sealed record MemberDto(Guid UserId, string Presence, string Availability, StatusBody? Status);
}
