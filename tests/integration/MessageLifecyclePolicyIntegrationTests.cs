using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class MessageLifecyclePolicyIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Member_can_read_policy_but_not_admin_settings()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var policy = await alice.GetAsync($"/api/v1/channels/{SeedData.DemoChannelId.Value}/messaging-policy");
        policy.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await policy.Content.ReadFromJsonAsync<MessagingPolicyDto>(JsonOptions);
        dto!.EditEnabled.Should().BeTrue();
        dto.EditAllowModeratorOverride.Should().BeFalse();

        var settings = await alice.GetAsync($"/api/v1/admin/settings?workspaceId={SeedData.DemoWorkspaceId.Value}");
        settings.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var put = await alice.PutAsJsonAsync("/api/v1/admin/settings", new
        {
            workspaceId = SeedData.DemoWorkspaceId.Value,
            messaging = new { editEnabled = false, editRoles = new[] { "Admin" }, deleteRoles = new[] { "Admin" } }
        });
        put.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Edit_and_delete_honor_window_role_and_override()
    {
        using var demo = factory.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        using var bob = factory.CreateClient();
        bob.DefaultRequestHeaders.Add("X-Dev-User", "bob");

        try
        {
            (await PutPolicy(demo, new
            {
                editEnabled = true,
                editWindowMinutes = 15,
                editRoles = new[] { "Member", "Moderator", "Admin" },
                editAllowModeratorOverride = false,
                deleteEnabled = true,
                deleteWindowMinutes = 15,
                deleteRoles = new[] { "Member", "Moderator", "Admin" },
                deleteAllowModeratorOverride = true
            })).StatusCode.Should().Be(HttpStatusCode.OK);

            var freshId = await Send(alice, "fresh");
            (await Edit(alice, freshId, "fresh-edited")).StatusCode.Should().Be(HttpStatusCode.OK);

            var staleId = await Send(alice, "stale");
            await Backdate(staleId, TimeSpan.FromMinutes(16));
            var expired = await Edit(alice, staleId, "too-late");
            expired.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await ReadError(expired)).Should().Be(MessageLifecyclePolicyRules.EditWindowExpired);
            var deleteExpired = await alice.DeleteAsync($"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages/{staleId}");
            deleteExpired.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await ReadError(deleteExpired)).Should().Be(MessageLifecyclePolicyRules.DeleteWindowExpired);

            (await PutPolicy(demo, new
            {
                editEnabled = false,
                editRoles = new[] { "Member", "Moderator", "Admin" },
                editAllowModeratorOverride = false,
                deleteEnabled = true,
                clearDeleteWindow = true,
                deleteRoles = new[] { "Admin" },
                deleteAllowModeratorOverride = false
            })).StatusCode.Should().Be(HttpStatusCode.OK);

            var blockedId = await Send(alice, "blocked");
            var disabled = await Edit(alice, blockedId, "nope");
            disabled.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ReadError(disabled)).Should().Be(MessageLifecyclePolicyRules.EditDisabled);

            var roleDenied = await alice.DeleteAsync($"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages/{blockedId}");
            roleDenied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ReadError(roleDenied)).Should().Be(MessageLifecyclePolicyRules.DeleteRoleDenied);

            (await PutPolicy(demo, new
            {
                editEnabled = true,
                editRoles = new[] { "Admin" },
                editAllowModeratorOverride = true,
                deleteEnabled = true,
                deleteRoles = new[] { "Member", "Moderator", "Admin" },
                deleteAllowModeratorOverride = true
            })).StatusCode.Should().Be(HttpStatusCode.OK);

            var promote = await demo.PutAsJsonAsync(
                $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/members/{SeedData.BobUserId.Value}/role",
                new { role = "Moderator" });
            promote.StatusCode.Should().Be(HttpStatusCode.OK);

            var overridden = await Edit(bob, blockedId, "moderator-edit");
            overridden.StatusCode.Should().Be(HttpStatusCode.OK);

            var ownDenied = await Edit(alice, freshId, "member-cannot");
            ownDenied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ReadError(ownDenied)).Should().Be(MessageLifecyclePolicyRules.EditRoleDenied);

            (await PutPolicy(demo, new
            {
                editWindowMinutes = 0,
                editRoles = new[] { "Member" },
                deleteRoles = new[] { "Member" }
            })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            await demo.PutAsJsonAsync(
                $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/members/{SeedData.BobUserId.Value}/role",
                new { role = "Member" });
            await ResetPolicy();
        }
    }

    [Fact]
    public async Task Soft_delete_keeps_body_for_later_audit_snapshot()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        var body = $"keep-body-{Guid.NewGuid():N}";
        var messageId = await Send(alice, body);
        var delete = await alice.DeleteAsync($"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages/{messageId}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = factory.CreateMigratorDbContext();
        var row = await db.Messages.IgnoreQueryFilters().SingleAsync(x => x.Id == new MessageId(messageId));
        row.DeletedAt.Should().NotBeNull();
        row.Body.Should().Be(body);
    }

    private static async Task<Guid> Send(HttpClient client, string body)
    {
        var messageId = Guid.NewGuid();
        var create = await client.PostAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages",
            new SendMessageRequest(messageId, $"idem-policy-{messageId:N}", body, null, null));
        create.StatusCode.Should().Be(HttpStatusCode.Accepted);
        return messageId;
    }

    private static Task<HttpResponseMessage> Edit(HttpClient client, Guid messageId, string body) =>
        client.PutAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages/{messageId}",
            new EditMessageRequest(body));

    private async Task Backdate(Guid messageId, TimeSpan age)
    {
        await using var db = factory.CreateMigratorDbContext();
        var row = await db.Messages.IgnoreQueryFilters().SingleAsync(x => x.Id == new MessageId(messageId));
        row.CreatedAt = DateTimeOffset.UtcNow.Subtract(age);
        await db.SaveChangesAsync();
    }

    private static Task<HttpResponseMessage> PutPolicy(HttpClient demo, object messaging) =>
        demo.PutAsJsonAsync("/api/v1/admin/settings", new
        {
            workspaceId = SeedData.DemoWorkspaceId.Value,
            messaging
        });

    private async Task ResetPolicy()
    {
        await using var db = factory.CreateMigratorDbContext();
        var row = await db.MessageLifecyclePolicies.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == SeedData.DemoTenantId);
        if (row is null)
        {
            return;
        }

        db.MessageLifecyclePolicies.Remove(row);
        await db.SaveChangesAsync();
    }

    private static async Task<string?> ReadError(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return payload.GetProperty("error").GetString();
    }
}
