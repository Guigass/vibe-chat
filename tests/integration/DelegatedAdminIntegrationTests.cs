using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Administration;
using VibeChat.Infrastructure;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-134 — scope lattice, expiry, break-glass alert and concurrent hard limit.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class DelegatedAdminIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Space_scope_expiry_and_break_glass_stay_inside_the_grantor()
    {
        using var demo = Client("demo");
        using var alice = Client("alice");
        try
        {
            var self = await SendAsync(demo, AssignPath(), new
            {
                granteeId = SeedData.DemoUserId.Value,
                scope = DelegatedScopes.Workspace,
                reason = "eu mesmo"
            });
            self.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await self.Content.ReadAsStringAsync()).Should().Contain(DelegatedAdminErrors.SelfGrant);

            var space = await SendAsync(demo, AssignPath(), new
            {
                granteeId = SeedData.AliceUserId.Value,
                scope = DelegatedScopes.Space,
                spaceId = SeedData.DemoSpaceGeralId,
                reason = "cobertura do space"
            });
            space.StatusCode.Should().Be(HttpStatusCode.Created);

            (await alice.GetAsync(SpacePath(SeedData.DemoSpaceGeralId))).StatusCode.Should().Be(HttpStatusCode.OK);
            var other = await alice.GetAsync(SpacePath(SeedData.DemoSpaceEngenhariaId));
            other.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await other.Content.ReadAsStringAsync()).Should().Contain(DelegatedAdminErrors.ScopeExceeded);

            var above = await SendAsync(alice, AssignPath(), new
            {
                granteeId = SeedData.BobUserId.Value,
                scope = DelegatedScopes.Workspace,
                reason = "subir de escopo"
            });
            above.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var foreignSpace = await SendAsync(alice, AssignPath(), new
            {
                granteeId = SeedData.BobUserId.Value,
                scope = DelegatedScopes.Space,
                spaceId = SeedData.DemoSpaceEngenhariaId,
                reason = "outro space"
            });
            foreignSpace.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var same = await SendAsync(alice, AssignPath(), new
            {
                granteeId = SeedData.BobUserId.Value,
                scope = DelegatedScopes.Space,
                spaceId = SeedData.DemoSpaceGeralId,
                reason = "mesmo space"
            });
            same.StatusCode.Should().Be(HttpStatusCode.Created);

            var glass = await SendAsync(demo, AssignPath(), new
            {
                granteeId = SeedData.BobUserId.Value,
                scope = DelegatedScopes.Security,
                reason = "incidente",
                breakGlass = true,
                expiresAt = DateTimeOffset.UtcNow.AddHours(1)
            });
            glass.StatusCode.Should().Be(HttpStatusCode.Created);
            var created = await glass.Content.ReadFromJsonAsync<AssignmentDto>();
            var opened = await demo.GetStringAsync(AlertsPath());
            opened.Should().Contain(DelegatedAdminAlerts.BreakGlass);

            await using (var db = factory.CreateMigratorDbContext())
            {
                var row = await db.ScopedRoleAssignments.IgnoreQueryFilters().SingleAsync(x => x.Id == created!.Id);
                row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
                await db.SaveChangesAsync();
            }

            var list = await demo.GetStringAsync(AssignmentsPath());
            list.Should().NotContain(created!.Id.ToString());
            var expired = await demo.GetStringAsync(AlertsPath());
            expired.Should().Contain(DelegatedAdminAlerts.BreakGlassExpired);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    [Fact]
    public async Task Concurrent_reserve_does_not_pass_the_hard_limit()
    {
        using var demo = Client("demo");
        try
        {
            var put = await demo.PutAsJsonAsync(QuotaPath(DelegatedResources.Automations), new { softLimit = 1, hardLimit = 1 });
            put.StatusCode.Should().Be(HttpStatusCode.OK);

            var first = ReserveAsync(demo);
            var second = ReserveAsync(Client("demo"));
            var results = await Task.WhenAll(first, second);
            results.Count(x => x == HttpStatusCode.OK).Should().Be(1);
            results.Count(x => x == HttpStatusCode.Conflict).Should().Be(1);

            var usage = await demo.GetStringAsync(UsagePath());
            usage.Should().Contain("\"used\":1");
        }
        finally
        {
            await CleanupAsync();
        }
    }

    private async Task CleanupAsync()
    {
        await using var db = factory.CreateMigratorDbContext();
        var alerts = await db.QuotaAlerts.IgnoreQueryFilters().Where(x => x.WorkspaceId == SeedData.DemoWorkspaceId).ToListAsync();
        var counters = await db.QuotaCounters.IgnoreQueryFilters().Where(x => x.WorkspaceId == SeedData.DemoWorkspaceId).ToListAsync();
        var quotas = await db.ResourceQuotas.IgnoreQueryFilters().Where(x => x.WorkspaceId == SeedData.DemoWorkspaceId).ToListAsync();
        var assignments = await db.ScopedRoleAssignments.IgnoreQueryFilters().Where(x => x.WorkspaceId == SeedData.DemoWorkspaceId).ToListAsync();
        db.RemoveRange(alerts);
        db.RemoveRange(counters);
        db.RemoveRange(quotas);
        db.RemoveRange(assignments);
        await db.SaveChangesAsync();
    }

    private async Task<HttpStatusCode> ReserveAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(ReservePath(DelegatedResources.Automations), new { delta = 1 });
        return response.StatusCode;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string url, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private static string AssignPath() => $"/api/v1/workspaces/{WorkspaceId}/delegated-admin/assignments";

    private static string AssignmentsPath() => AssignPath();

    private static string AlertsPath() => $"/api/v1/workspaces/{WorkspaceId}/delegated-admin/alerts";

    private static string SpacePath(Guid spaceId) => $"/api/v1/workspaces/{WorkspaceId}/delegated-admin/spaces/{spaceId}";

    private static string QuotaPath(string resource) => $"/api/v1/workspaces/{WorkspaceId}/quotas/{resource}";

    private static string ReservePath(string resource) => $"/api/v1/workspaces/{WorkspaceId}/quotas/{resource}/reserve";

    private static string UsagePath() => $"/api/v1/workspaces/{WorkspaceId}/quotas/usage";

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private sealed record AssignmentDto(Guid Id);
}
