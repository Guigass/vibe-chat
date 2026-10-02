using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Audit;
using VibeChat.Conversations;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-166 — contact groups organize the people list and do not grant access.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class ContactGroupIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Admin_assigns_departments_and_members_see_every_section()
    {
        using var demo = Client("demo");
        using var alice = Client("alice");
        using var bob = Client("bob");
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var vendas = await CreateAsync(demo, $"Vendas {suffix}", "department");
        var estoque = await CreateAsync(demo, $"Estoque {suffix}", "department");
        var ti = await CreateAsync(demo, $"TI {suffix}", "department");

        (await ReplaceAsync(demo, vendas.Id, SeedData.AliceUserId.Value)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReplaceAsync(demo, ti.Id, SeedData.AliceUserId.Value, SeedData.BobUserId.Value)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReplaceAsync(demo, estoque.Id, SeedData.BobUserId.Value)).StatusCode.Should().Be(HttpStatusCode.OK);

        var aliceSections = await ContactsAsync(alice);
        Section(aliceSections, vendas.Id).Members.Select(m => m.UserId).Should().Contain(SeedData.AliceUserId.Value);
        Section(aliceSections, ti.Id).Members.Select(m => m.UserId).Should().Contain(SeedData.AliceUserId.Value);
        Section(aliceSections, estoque.Id).Members.Select(m => m.UserId).Should().Contain(SeedData.BobUserId.Value);
        Section(aliceSections, estoque.Id).Members.Select(m => m.UserId).Should().NotContain(SeedData.AliceUserId.Value);

        var bobSections = await ContactsAsync(bob);
        bobSections.Select(s => s.GroupId).Should().Contain([vendas.Id, estoque.Id, ti.Id]);

        await using var db = factory.CreateMigratorDbContext();
        var actions = await db.AuditEvents.IgnoreQueryFilters()
            .Where(x => x.EntityId == vendas.Id.ToString())
            .Select(x => x.Action)
            .ToListAsync();
        actions.Should().Contain(AuditActions.ContactGroupCreate);
        actions.Should().Contain(AuditActions.ContactGroupMembersReplace);
    }

    [Fact]
    public async Task Personal_group_create_is_idempotent_and_duplicate_department_names_conflict()
    {
        using var demo = Client("demo");
        using var alice = Client("alice");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var key = $"dept-{suffix}";
        demo.DefaultRequestHeaders.Add("Idempotency-Key", key);

        var first = await CreateAsync(demo, $"Financeiro {suffix}", "department");
        var second = await CreateAsync(demo, $"Financeiro {suffix}", "department");
        second.Id.Should().Be(first.Id);

        demo.DefaultRequestHeaders.Remove("Idempotency-Key");
        var conflict = await demo.PostAsJsonAsync(Groups(), new { name = $"financeiro {suffix}", kind = "department" });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var personalKey = $"personal-{suffix}";
        alice.DefaultRequestHeaders.Add("Idempotency-Key", personalKey);
        var personal = await CreateAsync(alice, $"Favoritos {suffix}", "personal");
        var replay = await CreateAsync(alice, $"Favoritos {suffix}", "personal");
        replay.Id.Should().Be(personal.Id);

        var changed = await alice.PostAsJsonAsync(Groups(), new { name = $"Outro {suffix}", kind = "personal" });
        changed.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var outsider = await demo.PostAsJsonAsync(Groups(), new { name = $"Fora {suffix}", kind = "nope" });
        outsider.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Removing_workspace_membership_removes_group_assignments()
    {
        using var demo = Client("demo");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var group = await CreateAsync(demo, $"Campo {suffix}", "department");
        var email = $"cg-{suffix}@example.com";
        var invite = await demo.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/members",
            new { email, displayName = "Campo", role = "Member" });
        invite.StatusCode.Should().Be(HttpStatusCode.Created);
        var member = (await invite.Content.ReadFromJsonAsync<MemberDto>(Json))!;

        (await ReplaceAsync(demo, group.Id, member.UserId)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = factory.CreateMigratorDbContext();
        var membership = await db.WorkspaceMembers.IgnoreQueryFilters()
            .SingleAsync(x => x.WorkspaceId == SeedData.DemoWorkspaceId && x.UserId == new UserId(member.UserId));
        db.WorkspaceMembers.Remove(membership);
        await db.SaveChangesAsync();

        var stillAssigned = await db.ContactGroupMembers.IgnoreQueryFilters()
            .AnyAsync(x => x.WorkspaceId == SeedData.DemoWorkspaceId && x.UserId == new UserId(member.UserId));
        stillAssigned.Should().BeFalse();

        var sections = await ContactsAsync(demo);
        Section(sections, group.Id).Members.Select(m => m.UserId).Should().NotContain(member.UserId);
    }

    [Fact]
    public async Task Department_membership_does_not_open_a_private_channel()
    {
        using var alice = Client("alice");
        using var bob = Client("bob");
        using var demo = Client("demo");

        var create = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels",
            new { name = $"cg-{Guid.NewGuid():N}"[..20], type = "Private" });
        create.EnsureSuccessStatusCode();
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDto>(Json))!;

        var before = await bob.GetAsync($"/api/v1/channels/{channel.Id}/messages");
        before.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var group = await CreateAsync(demo, $"Acesso {Guid.NewGuid():N}"[..16], "department");
        (await ReplaceAsync(demo, group.Id, SeedData.BobUserId.Value)).StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await bob.GetAsync($"/api/v1/channels/{channel.Id}/messages");
        after.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var db = factory.CreateMigratorDbContext();
        var onChannel = await db.ChannelMembers.IgnoreQueryFilters()
            .AnyAsync(x => x.ChannelId == new ChannelId(channel.Id) && x.UserId == SeedData.BobUserId && x.LeftAt == null);
        onChannel.Should().BeFalse();
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

    private static Task<HttpResponseMessage> ReplaceAsync(HttpClient client, Guid groupId, params Guid[] userIds) =>
        client.PutAsJsonAsync($"{Groups()}/{groupId}/members", new { userIds });

    private static async Task<SectionDto[]> ContactsAsync(HttpClient client)
    {
        var response = await client.GetAsync($"/api/v1/workspaces/{WorkspaceId}/contacts?grouped=true");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SectionDto[]>(Json))!;
    }

    private static SectionDto Section(IEnumerable<SectionDto> sections, Guid groupId) =>
        sections.Single(x => x.GroupId == groupId);

    private sealed record GroupDto(Guid Id, string Kind, string Name, Guid[] MemberUserIds);
    private sealed record SectionDto(Guid? GroupId, string? Name, string? Kind, MemberDto[] Members);
    private sealed record MemberDto(Guid UserId, string DisplayName, string Email, string Role);
    private sealed record ChannelDto(Guid Id);
}
