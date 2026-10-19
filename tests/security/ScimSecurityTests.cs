using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

[Collection(SecurityCollection.Name)]
public sealed class ScimSecurityTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Tenant_token_cannot_read_another_tenant_and_mapping_cannot_grant_admin()
    {
        using var alice = Client("alice");
        (await alice.PostAsJsonAsync($"/api/v1/admin/workspaces/{WorkspaceId}/scim/tokens", new { name = "nope" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var admin = Client("demo");
        var created = await admin.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/scim/tokens",
            new { name = "A" });
        var secret = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString();
        using var scim = Scim(secret!);
        var user = await scim.PostAsJsonAsync("/scim/v2/Users", new
        {
            userName = $"sec-{Guid.NewGuid():N}@corp.test",
            externalId = "sec-" + Guid.NewGuid().ToString("N"),
            name = new { formatted = "Sec" },
            active = true
        });
        user.StatusCode.Should().Be(HttpStatusCode.Created);
        var principalId = (await user.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var group = await scim.PostAsJsonAsync("/scim/v2/Groups", new
        {
            displayName = "Sec",
            externalId = "sec-g-" + Guid.NewGuid().ToString("N"),
            members = new[] { new { value = principalId } }
        });
        var groupId = (await group.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await using (var db = factory.CreateMigratorDbContext())
        {
            var principal = await db.ScimPrincipals.IgnoreQueryFilters().SingleAsync(x => x.Id == principalId);
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.NewGuid(),
                TenantId = SeedData.DemoTenantId,
                WorkspaceId = SeedData.DemoWorkspaceId,
                UserId = principal.UserId,
                Role = Role.Admin,
                JoinedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var dry = await admin.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/scim/mappings/dry-run",
            new { groupId, role = "Member" });
        dry.StatusCode.Should().Be(HttpStatusCode.OK);
        var preview = await dry.Content.ReadFromJsonAsync<JsonElement>();
        preview.GetProperty("conflicts").GetArrayLength().Should().Be(1);
        preview.GetProperty("adds").GetArrayLength().Should().Be(0);

        var apply = await admin.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/scim/mappings",
            new { groupId, role = "Admin" });
        apply.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using (var db = factory.CreateMigratorDbContext())
        {
            var principal = await db.ScimPrincipals.IgnoreQueryFilters().SingleAsync(x => x.Id == principalId);
            (await db.WorkspaceMembers.IgnoreQueryFilters().SingleAsync(x => x.UserId == principal.UserId)).Role
                .Should().Be(Role.Admin);
        }

        var rawB = ScimTokenCodec.CreateRaw();
        var tenantB = new TenantId(Guid.NewGuid());
        await using (var db = factory.CreateMigratorDbContext())
        {
            db.ScimTokens.Add(new ScimToken
            {
                Id = Guid.NewGuid(),
                TenantId = tenantB,
                Name = "B",
                TokenHash = ScimTokenCodec.Hash(rawB),
                Last4 = ScimTokenCodec.Last4(rawB),
                Scopes = ScimPolicies.Scopes,
                CreatedByUserId = SeedData.DemoUserId,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var other = Scim(rawB);
        (await other.GetFromJsonAsync<JsonElement>("/scim/v2/Users")).GetProperty("totalResults").GetInt32().Should().Be(0);
        (await other.GetAsync($"/scim/v2/Users/{principalId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private HttpClient Scim(string secret)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return client;
    }
}
