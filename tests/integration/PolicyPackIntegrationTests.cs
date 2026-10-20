using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Administration;
using VibeChat.AI;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-133 — preview matches apply, retry is idempotent, drift is visible, rollback restores.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class PolicyPackIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Preview_matches_apply_retry_drift_and_rollback()
    {
        using var demo = Client("demo");
        await GrantAsync(demo);
        using var alice = Client("alice");
        var before = await SnapshotAsync();
        try
        {
            var body = new { packId = PolicyPackIds.SmallTeam, version = 1, confirm = true, overrides = new { } };
            var baseline = await SendAsync(alice, Path("apply"), body, Guid.NewGuid().ToString("N"));
            baseline.StatusCode.Should().Be(HttpStatusCode.OK);

            var overrides = new { packId = PolicyPackIds.SmallTeam, version = 1, confirm = true, overrides = new Dictionary<string, int> { ["uploads.maxMb"] = 8 } };
            var preview = await alice.PostAsJsonAsync(Path("preview"), overrides);
            preview.StatusCode.Should().Be(HttpStatusCode.OK);
            var previewJson = await preview.Content.ReadAsStringAsync();

            var key = Guid.NewGuid().ToString("N");
            var applied = await SendAsync(alice, Path("apply"), overrides, key);
            applied.StatusCode.Should().Be(HttpStatusCode.OK);
            var appliedJson = await applied.Content.ReadAsStringAsync();
            JsonNode.DeepEquals(JsonNode.Parse(previewJson), JsonNode.Parse(appliedJson)).Should().BeTrue();
            appliedJson.Should().Contain("\"uploads.maxMb\"").And.Contain("8");

            var replay = await SendAsync(alice, Path("apply"), overrides, key);
            replay.StatusCode.Should().Be(HttpStatusCode.OK);
            JsonNode.DeepEquals(JsonNode.Parse(appliedJson), JsonNode.Parse(await replay.Content.ReadAsStringAsync())).Should().BeTrue();

            var conflict = await SendAsync(alice, Path("apply"), body, key);
            conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);

            await using (var db = factory.CreateMigratorDbContext())
            {
                var files = await db.TenantFilesSettings.IgnoreQueryFilters().SingleAsync(x => x.TenantId == SeedData.DemoTenantId);
                files.MaxSizeBytes.Should().Be(8L * 1024 * 1024);
                files.MaxSizeBytes = 10L * 1024 * 1024;
                await db.SaveChangesAsync();
            }

            var drift = await alice.GetFromJsonAsync<DriftDto>(Path("drift"));
            drift!.Drifted.Should().BeTrue();
            drift.Controls.Should().Contain(x => x.Key == PolicyPackControls.UploadsMaxMb && x.Drifted);

            var rolled = await SendAsync(alice, Path("rollback"), new { confirm = true }, Guid.NewGuid().ToString("N"));
            rolled.StatusCode.Should().Be(HttpStatusCode.OK);
            var rolledJson = await rolled.Content.ReadAsStringAsync();
            rolledJson.Should().Contain("\"uploads.maxMb\"");
            PolicyPackRules.ReadControls(rolledJson).Single(x => x.Key == PolicyPackControls.UploadsMaxMb).Number.Should().Be(10);

            var export = await alice.GetStringAsync(Path("export"));
            export.Should().NotContain("secret").And.NotContain("tenantId").And.Contain(PolicyPackIds.SmallTeam);

            var regulated = await SendAsync(alice, Path("apply"), new
            {
                packId = PolicyPackIds.EnterpriseBaseline,
                version = 1,
                confirm = true
            }, Guid.NewGuid().ToString("N"));
            regulated.StatusCode.Should().Be(HttpStatusCode.OK);
            var invite = await demo.PostAsJsonAsync(
                $"/api/v1/workspaces/{WorkspaceId}/channels/{SeedData.DemoChannelId.Value}/invites",
                new { email = (string?)null, expiresInDays = 7 });
            invite.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await invite.Content.ReadAsStringAsync()).Should().Contain(PolicyPackErrors.GuestsBlocked);
        }
        finally
        {
            await RestoreAsync(before);
        }
    }

    private async Task<Snapshot> SnapshotAsync()
    {
        await using var db = factory.CreateMigratorDbContext();
        var ai = await db.AiSettings.IgnoreQueryFilters().SingleAsync(x => x.WorkspaceId == SeedData.DemoWorkspaceId);
        var retention = await db.MessageRetentionSettings.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TenantId == SeedData.DemoTenantId);
        var files = await db.TenantFilesSettings.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TenantId == SeedData.DemoTenantId);
        var dlp = await db.DlpPolicies.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.WorkspaceId == SeedData.DemoWorkspaceId);
        return new Snapshot(ai.Enabled, retention?.Enabled, retention?.RetentionDays, files?.MaxSizeBytes, dlp?.Simulation);
    }

    private async Task RestoreAsync(Snapshot before)
    {
        await using var db = factory.CreateMigratorDbContext();
        var ai = await db.AiSettings.IgnoreQueryFilters().SingleAsync(x => x.WorkspaceId == SeedData.DemoWorkspaceId);
        ai.Enabled = before.AiEnabled;
        var retention = await db.MessageRetentionSettings.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TenantId == SeedData.DemoTenantId);
        if (before.RetentionEnabled is null)
        {
            if (retention is not null)
            {
                db.MessageRetentionSettings.Remove(retention);
            }
        }
        else if (retention is not null)
        {
            retention.Enabled = before.RetentionEnabled.Value;
            retention.RetentionDays = before.RetentionDays ?? MessageRetentionSettings.DefaultRetentionDays;
        }

        var files = await db.TenantFilesSettings.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TenantId == SeedData.DemoTenantId);
        if (before.MaxSizeBytes is null)
        {
            if (files is not null)
            {
                db.TenantFilesSettings.Remove(files);
            }
        }
        else if (files is not null)
        {
            files.MaxSizeBytes = before.MaxSizeBytes.Value;
        }

        var dlp = await db.DlpPolicies.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.WorkspaceId == SeedData.DemoWorkspaceId);
        if (dlp is not null && before.DlpSimulation is not null)
        {
            dlp.Simulation = before.DlpSimulation.Value;
        }

        var assignment = await db.PolicyPackAssignments.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.WorkspaceId == SeedData.DemoWorkspaceId);
        if (assignment is not null)
        {
            db.PolicyPackAssignments.Remove(assignment);
        }

        await db.SaveChangesAsync();
    }

    private async Task GrantAsync(HttpClient demo)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/workspaces/{WorkspaceId}/compliance/grants")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { userId = SeedData.AliceUserId.Value, capability = "policy" }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "policy-grant-" + Guid.NewGuid().ToString("N"));
        var response = await demo.SendAsync(request);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string url, object body, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static string Path(string suffix) => $"/api/v1/workspaces/{WorkspaceId}/policy-packs/{suffix}";

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private sealed record Snapshot(bool AiEnabled, bool? RetentionEnabled, int? RetentionDays, long? MaxSizeBytes, bool? DlpSimulation);

    private sealed record DriftDto(bool Drifted, DriftControl[] Controls);

    private sealed record DriftControl(string Key, bool Drifted);
}
