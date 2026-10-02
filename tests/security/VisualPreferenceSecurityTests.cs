using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

[Collection(SecurityCollection.Name)]
public sealed class VisualPreferenceSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Caller_saves_reloads_and_resets_appearance_in_the_same_tenant()
    {
        using var alice = Client("alice");

        var put = await alice.PutAsJsonAsync(
            AppearancePath(SeedData.AliceUserId),
            new { chatWallpaperId = "tide", accentColorId = "ocean" });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var me = await alice.GetFromJsonAsync<MeDto>("/api/v1/me", Json);
        me!.ChatWallpaperId.Should().Be("tide");
        me.AccentColorId.Should().Be("ocean");

        var reset = await alice.PutAsJsonAsync(
            AppearancePath(SeedData.AliceUserId),
            new { chatWallpaperId = (string?)null, accentColorId = (string?)null });
        reset.StatusCode.Should().Be(HttpStatusCode.OK);
        var again = await alice.GetFromJsonAsync<MeDto>("/api/v1/me", Json);
        again!.ChatWallpaperId.Should().BeNull();
        again.AccentColorId.Should().BeNull();
    }

    [Fact]
    public async Task Unknown_catalog_id_does_not_save()
    {
        using var alice = Client("alice");
        var before = await alice.GetFromJsonAsync<MeDto>("/api/v1/me", Json);

        var put = await alice.PutAsJsonAsync(
            AppearancePath(SeedData.AliceUserId),
            new { chatWallpaperId = "url(javascript:alert(1))", accentColorId = "ocean" });
        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await put.Content.ReadFromJsonAsync<ErrorDto>(Json);
        body!.Error.Should().Be(VisualPreferenceCatalog.InvalidWallpaper);

        var accent = await alice.PutAsJsonAsync(
            AppearancePath(SeedData.AliceUserId),
            new { chatWallpaperId = "tide", accentColorId = "#ff00ff" });
        accent.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var after = await alice.GetFromJsonAsync<MeDto>("/api/v1/me", Json);
        after!.ChatWallpaperId.Should().Be(before!.ChatWallpaperId);
        after.AccentColorId.Should().Be(before.AccentColorId);
    }

    [Fact]
    public async Task Stored_unknown_id_falls_back_to_the_product_default()
    {
        await using (var db = factory.CreateMigratorDbContext())
        {
            var row = await db.UserVisualPreferences.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.TenantId == SeedData.DemoTenantId && x.UserId == SeedData.BobUserId);
            if (row is null)
            {
                row = new UserVisualPreference
                {
                    Id = Guid.NewGuid(),
                    TenantId = SeedData.DemoTenantId,
                    UserId = SeedData.BobUserId
                };
                db.UserVisualPreferences.Add(row);
            }

            row.ChatWallpaperId = "not-a-wallpaper";
            row.AccentColorId = "purple";
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        using var bob = Client("bob");
        var me = await bob.GetFromJsonAsync<MeDto>("/api/v1/me", Json);
        me!.ChatWallpaperId.Should().BeNull();
        me.AccentColorId.Should().BeNull();
    }

    [Fact]
    public async Task Another_user_including_admin_cannot_update_appearance()
    {
        using var alice = Client("alice");
        using var demo = Client("demo");

        var peer = await alice.PutAsJsonAsync(
            AppearancePath(SeedData.BobUserId),
            new { chatWallpaperId = "slate", accentColorId = "rose" });
        peer.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var admin = await demo.PutAsJsonAsync(
            AppearancePath(SeedData.AliceUserId),
            new { chatWallpaperId = "ember", accentColorId = "amber" });
        admin.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cross_tenant_appearance_stays_invisible_and_unchanged()
    {
        var foreignId = Guid.NewGuid();
        var foreignTenant = new TenantId(Guid.NewGuid());
        await using (var db = factory.CreateMigratorDbContext())
        {
            db.UserVisualPreferences.Add(new UserVisualPreference
            {
                Id = foreignId,
                TenantId = foreignTenant,
                UserId = SeedData.AliceUserId,
                ChatWallpaperId = "ember",
                AccentColorId = "rose",
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var alice = Client("alice");
        var denied = await alice.PutAsJsonAsync(
            AppearancePath(Guid.NewGuid()),
            new { chatWallpaperId = "tide", accentColorId = "ocean" });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var put = await alice.PutAsJsonAsync(
            AppearancePath(SeedData.AliceUserId),
            new { chatWallpaperId = "tide", accentColorId = "ocean" });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var me = await alice.GetFromJsonAsync<MeDto>("/api/v1/me", Json);
        me!.ChatWallpaperId.Should().Be("tide");
        me.AccentColorId.Should().Be("ocean");

        await using var check = factory.CreateMigratorDbContext();
        var foreign = await check.UserVisualPreferences.IgnoreQueryFilters().SingleAsync(x => x.Id == foreignId);
        foreign.ChatWallpaperId.Should().Be("ember");
        foreign.AccentColorId.Should().Be("rose");
        foreign.TenantId.Should().Be(foreignTenant);
    }

    private static string AppearancePath(UserId userId) => $"/api/v1/users/{userId.Value}/appearance";

    private static string AppearancePath(Guid userId) => $"/api/v1/users/{userId}/appearance";

    private HttpClient Client(string devUser)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", devUser);
        return client;
    }

    private sealed record MeDto(
        Guid UserId,
        string Subject,
        string Email,
        string DisplayName,
        string[] Roles,
        string? Locale,
        string? ChatWallpaperId,
        string? AccentColorId);

    private sealed record ErrorDto(string Error);
}
