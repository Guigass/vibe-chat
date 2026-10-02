using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

[Collection(SecurityCollection.Name)]
public sealed class ScheduleSecurityTests(VibeChatApiFactory factory)
{
    [Fact]
    public async Task Cross_tenant_cannot_list_or_create_schedule()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        var foreignWorkspaceId = await SeedForeignWorkspaceAsync();

        var list = await alice.GetAsync($"/api/v1/workspaces/{foreignWorkspaceId}/schedule");
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var reminder = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{foreignWorkspaceId}/reminders",
            new
            {
                idempotencyKey = Guid.NewGuid().ToString("N"),
                targetKind = "Time",
                remindAtLocal = "2027-08-01T09:00:00",
                timeZone = "Etc/UTC",
                note = "vazamento"
            });
        reminder.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<Guid> SeedForeignWorkspaceAsync()
    {
        await using var db = factory.CreateMigratorDbContext();
        var now = DateTimeOffset.UtcNow;
        var workspaceId = WorkspaceId.New();
        var tenantId = new TenantId(workspaceId.Value);
        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = tenantId,
            Name = "Foreign schedule",
            Slug = $"sec-sched-{workspaceId.Value:N}"[..32],
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
        await db.SaveChangesAsync();
        return workspaceId.Value;
    }
}
