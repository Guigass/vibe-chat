using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using VibeChat.BuildingBlocks;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ScimIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Provision_is_idempotent_deprovision_revokes_access_and_mapping_stays_allowlisted()
    {
        using var admin = Client("demo");
        var created = await admin.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/scim/tokens",
            new { name = "IdP" });
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokenJson = await created.Content.ReadFromJsonAsync<JsonElement>();
        var secret = tokenJson.GetProperty("secret").GetString();
        secret.Should().StartWith(ScimPolicies.TokenPrefix);
        var tokenId = tokenJson.GetProperty("id").GetGuid();

        var listed = await admin.GetAsync($"/api/v1/admin/workspaces/{WorkspaceId}/scim");
        var listBody = await listed.Content.ReadAsStringAsync();
        listBody.Should().NotContain(secret);
        listBody.Should().Contain(tokenJson.GetProperty("last4").GetString());

        await using (var db = factory.CreateMigratorDbContext())
        {
            var row = await db.ScimTokens.IgnoreQueryFilters().SingleAsync(x => x.Id == tokenId);
            row.TokenHash.Should().NotBe(secret);
            row.TokenHash.Should().Be(ScimTokenCodec.Hash(secret!));
        }

        var externalId = "ext-" + Guid.NewGuid().ToString("N");
        var userName = $"carol-{Guid.NewGuid():N}@corp.test";
        using var scim = ScimClient(secret!);
        var first = await scim.PostAsJsonAsync("/scim/v2/Users", UserBody(userName, externalId, "Carol"));
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var user = await first.Content.ReadFromJsonAsync<JsonElement>();
        var principalId = user.GetProperty("id").GetGuid();
        var second = await scim.PostAsJsonAsync("/scim/v2/Users", UserBody(userName, externalId, "Carol"));
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid().Should().Be(principalId);

        var internalUser = $"internal-{Guid.NewGuid():N}@corp.test";
        var internalExternal = "int-" + Guid.NewGuid().ToString("N");
        var viaAdmin = await admin.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/scim/provisioning/users",
            new { userName = internalUser, externalId = internalExternal, displayName = "Interno", active = true });
        viaAdmin.StatusCode.Should().Be(HttpStatusCode.OK);
        var internalId = (await viaAdmin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var found = await scim.GetAsync($"/scim/v2/Users?filter={Uri.EscapeDataString($"externalId eq \"{internalExternal}\"")}");
        found.StatusCode.Should().Be(HttpStatusCode.OK);
        var listedUsers = await found.Content.ReadFromJsonAsync<JsonElement>();
        listedUsers.GetProperty("totalResults").GetInt32().Should().Be(1);
        listedUsers.GetProperty("Resources")[0].GetProperty("id").GetString().Should().Be(internalId.ToString());

        var aliceTakeover = await scim.PostAsJsonAsync(
            "/scim/v2/Users",
            UserBody("alice@vibechat.local", "alice-ext-" + Guid.NewGuid().ToString("N"), "Not Alice"));
        aliceTakeover.StatusCode.Should().Be(HttpStatusCode.Created);
        var aliceCopy = (await aliceTakeover.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using (var db = factory.CreateMigratorDbContext())
        {
            var alice = await db.UserProfiles.SingleAsync(x => x.Id == SeedData.AliceUserId);
            alice.Subject.Should().Be("dev:alice");
            (await db.WorkspaceMembers.IgnoreQueryFilters().SingleAsync(x => x.UserId == SeedData.AliceUserId)).Role
                .Should().Be(Role.Member);
            var copy = await db.ScimPrincipals.IgnoreQueryFilters().SingleAsync(x => x.Id == aliceCopy);
            copy.UserId.Should().NotBe(SeedData.AliceUserId);
        }

        var group = await scim.PostAsJsonAsync("/scim/v2/Groups", new
        {
            displayName = "Engenharia",
            externalId = "grp-" + Guid.NewGuid().ToString("N"),
            members = new[] { new { value = principalId } }
        });
        group.StatusCode.Should().Be(HttpStatusCode.Created);
        var groupId = (await group.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var forbidden = await admin.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/scim/mappings",
            new { groupId, role = "PlatformOwner" });
        forbidden.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var mapped = await admin.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/scim/mappings",
            new { groupId, role = "Member" });
        mapped.StatusCode.Should().Be(HttpStatusCode.OK);
        Guid userId;
        await using (var db = factory.CreateMigratorDbContext())
        {
            var principal = await db.ScimPrincipals.IgnoreQueryFilters().SingleAsync(x => x.Id == principalId);
            userId = principal.UserId.Value;
            (await db.WorkspaceMembers.IgnoreQueryFilters().SingleAsync(
                x => x.UserId == principal.UserId && x.WorkspaceId == SeedData.DemoWorkspaceId)).Role
                .Should().Be(Role.Member);
            principal.UserId.Should().NotBe(UserId.Empty);
            var profile = await db.UserProfiles.SingleAsync(x => x.Id == principal.UserId);
            profile.Subject = "dev:scimcarol";
            await db.SaveChangesAsync();
        }

        var patch = await scim.PatchAsJsonAsync($"/scim/v2/Users/{principalId}", new
        {
            schemas = new[] { "urn:ietf:params:scim:api:messages:2.0:PatchOp" },
            Operations = new[] { new { op = "replace", path = "active", value = false } }
        });
        var patchBody = await patch.Content.ReadAsStringAsync();
        patch.StatusCode.Should().Be(HttpStatusCode.OK, patchBody);

        await using (var db = factory.CreateMigratorDbContext())
        {
            (await db.WorkspaceMembers.IgnoreQueryFilters().AnyAsync(x => x.UserId == new UserId(userId))).Should().BeFalse();
            (await db.AccessRevocations.IgnoreQueryFilters().AnyAsync(x => x.UserId == new UserId(userId))).Should().BeTrue();
        }

        using var revoked = Client("scimcarol", userName);
        var me = await revoked.GetAsync("/api/v1/me");
        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var members = await revoked.GetAsync($"/api/v1/workspaces/{WorkspaceId}/members");
        members.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var bulk = await scim.PostAsJsonAsync("/scim/v2/Bulk", new
        {
            schemas = new[] { "urn:ietf:params:scim:api:messages:2.0:BulkRequest" },
            Operations = new object[]
            {
                new { method = "POST", path = "/Users", bulkId = "a", data = UserBody($"bulk-{Guid.NewGuid():N}@corp.test", "bulk-" + Guid.NewGuid().ToString("N"), "Bulk") },
                new { method = "POST", path = "/Users", bulkId = "b", data = UserBody(internalUser, internalExternal, "Interno") }
            }
        });
        bulk.StatusCode.Should().Be(HttpStatusCode.OK);
        var bulkBody = await bulk.Content.ReadFromJsonAsync<JsonElement>();
        bulkBody.GetProperty("Operations")[1].GetProperty("status").GetString().Should().Be("200");
    }

    [Fact]
    public async Task Other_tenant_token_cannot_enumerate_and_member_cannot_mint_tokens()
    {
        using var alice = Client("alice");
        var denied = await alice.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/scim/tokens",
            new { name = "nope" });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var admin = Client("demo");
        var created = await admin.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/scim/tokens",
            new { name = "Tenant A" });
        var secretA = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString();
        var userName = $"iso-{Guid.NewGuid():N}@corp.test";
        using var scimA = ScimClient(secretA!);
        var user = await scimA.PostAsJsonAsync("/scim/v2/Users", UserBody(userName, "iso-" + Guid.NewGuid().ToString("N"), "Iso"));
        user.StatusCode.Should().Be(HttpStatusCode.Created);
        var principalId = (await user.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var rawB = ScimTokenCodec.CreateRaw();
        var tenantB = new TenantId(Guid.NewGuid());
        await using (var db = factory.CreateMigratorDbContext())
        {
            db.Workspaces.Add(new Workspace
            {
                Id = new WorkspaceId(tenantB.Value),
                TenantId = tenantB,
                Name = "Other",
                Slug = "other-" + Guid.NewGuid().ToString("N")[..8],
                CreatedAt = DateTimeOffset.UtcNow
            });
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

        using var scimB = ScimClient(rawB);
        var list = await scimB.GetAsync("/scim/v2/Users");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalResults").GetInt32().Should().Be(0);
        var read = await scimB.GetAsync($"/scim/v2/Users/{principalId}");
        read.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Kill_switch_hides_admin_and_protocol()
    {
        using var disabled = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Identity:Scim:Enabled", "false");
            builder.UseSetting("Identity:Scim:KeycloakAdapter", "false");
        });
        using var admin = disabled.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Dev-User", "demo");
        var list = await admin.GetAsync($"/api/v1/admin/workspaces/{WorkspaceId}/scim");
        list.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scim = disabled.CreateClient();
        scim.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "vc_scim_deadbeef");
        var users = await scim.GetAsync("/scim/v2/Users");
        users.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static object UserBody(string userName, string externalId, string displayName) => new
    {
        schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:User" },
        userName,
        externalId,
        active = true,
        name = new { formatted = displayName }
    };

    private HttpClient Client(string user, string? email = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        if (email is not null)
        {
            client.DefaultRequestHeaders.Add("X-Dev-Email", email);
        }

        return client;
    }

    private HttpClient ScimClient(string secret)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return client;
    }
}
