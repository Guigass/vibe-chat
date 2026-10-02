using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VibeChat.Directory;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

/// <summary>B-166 — department writes are admin-only; personal groups and other tenants do not leak.</summary>
[Collection(SecurityCollection.Name)]
public sealed class ContactGroupSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Member_cannot_create_or_edit_a_department()
    {
        using var alice = Client("alice");
        using var demo = Client("demo");
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var create = await alice.PostAsJsonAsync(Groups(), new { name = $"Vendas {suffix}", kind = "department" });
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var department = await CreateAsync(demo, $"Vendas {suffix}", "department");
        var rename = await alice.PatchAsJsonAsync($"{Groups()}/{department.Id}", new { name = $"Renomeado {suffix}" });
        rename.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var members = await alice.PutAsJsonAsync($"{Groups()}/{department.Id}/members", new { userIds = new[] { SeedData.AliceUserId.Value } });
        members.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var delete = await alice.DeleteAsync($"{Groups()}/{department.Id}");
        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Another_members_personal_group_is_not_found()
    {
        using var alice = Client("alice");
        using var bob = Client("bob");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var personal = await CreateAsync(alice, $"Favoritos {suffix}", "personal");

        var read = await bob.GetAsync(Groups());
        read.EnsureSuccessStatusCode();
        var listed = await read.Content.ReadFromJsonAsync<GroupDto[]>(Json);
        listed!.Select(x => x.Id).Should().NotContain(personal.Id);

        var patch = await bob.PatchAsJsonAsync($"{Groups()}/{personal.Id}", new { name = "invasao" });
        patch.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var replace = await bob.PutAsJsonAsync($"{Groups()}/{personal.Id}/members", new { userIds = Array.Empty<Guid>() });
        replace.StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await bob.DeleteAsync($"{Groups()}/{personal.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var outsider = await alice.PutAsJsonAsync(
            $"{Groups()}/{personal.Id}/members",
            new { userIds = new[] { Guid.NewGuid() } });
        outsider.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cross_tenant_contact_group_is_invisible()
    {
        var foreignId = Guid.NewGuid();
        await using (var db = factory.CreateMigratorDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            var workspaceId = new WorkspaceId(Guid.NewGuid());
            var tenantId = new TenantId(workspaceId.Value);
            db.Workspaces.Add(new Workspace
            {
                Id = workspaceId,
                TenantId = tenantId,
                Name = "Foreign contacts",
                Slug = $"cg-{workspaceId.Value:N}"[..32],
                CreatedAt = now
            });
            db.ContactGroups.Add(new ContactGroup
            {
                Id = foreignId,
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                Kind = ContactGroupKind.Department,
                Name = "Secreto",
                Order = 0,
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
        }

        using var alice = Client("alice");
        var missing = await alice.GetAsync($"/api/v1/workspaces/{Guid.NewGuid()}/contact-groups");
        missing.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var own = await alice.GetFromJsonAsync<GroupDto[]>(Groups(), Json);
        own!.Select(x => x.Id).Should().NotContain(foreignId);

        await using var conn = new NpgsqlConnection(factory.RuntimeDatabaseConnectionString);
        await conn.OpenAsync();
        await using (var clear = conn.CreateCommand())
        {
            clear.CommandText = "SELECT set_config('app.tenant_id', '', false)";
            await clear.ExecuteNonQueryAsync();
        }

        await using (var count = conn.CreateCommand())
        {
            count.CommandText = """SELECT count(*)::int FROM directory.contact_groups""";
            var rows = (int)(await count.ExecuteScalarAsync() ?? -1);
            rows.Should().Be(0);
        }

        await using (var scoped = conn.CreateCommand())
        {
            scoped.CommandText = "SELECT set_config('app.tenant_id', @tenant, false)";
            scoped.Parameters.AddWithValue("tenant", SeedData.DemoTenantId.Value.ToString());
            await scoped.ExecuteNonQueryAsync();
        }

        await using (var hidden = conn.CreateCommand())
        {
            hidden.CommandText = """SELECT count(*)::int FROM directory.contact_groups WHERE "Id" = @id""";
            hidden.Parameters.AddWithValue("id", foreignId);
            var rows = (int)(await hidden.ExecuteScalarAsync() ?? -1);
            rows.Should().Be(0, "demo tenant context must not read another tenant's contact group");
        }
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private static string Groups() => $"/api/v1/workspaces/{WorkspaceId}/contact-groups";

    private static async Task<GroupDto> CreateAsync(HttpClient client, string name, string kind)
    {
        var response = await client.PostAsJsonAsync(Groups(), new { name, kind });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<GroupDto>(Json))!;
    }

    private sealed record GroupDto(Guid Id, string Kind, string Name);
}
