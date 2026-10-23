using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using VibeChat.Audit;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-137 — signed offline registry: install, update, rollback, revoke.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class PluginRegistryIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Mirror_installs_without_a_public_service_and_replay_hides_the_token()
    {
        var id = Id();
        try
        {
            using var admin = Client("demo");
            var index = Index(Manifest(id, "1.0.0"));
            var created = await PostAsync(admin, "install", index, id, "1.0.0", "registry-install-1-" + id);
            created.Status.Should().Be(HttpStatusCode.Created);
            created.Body.Publisher.Should().Be("Lab Publisher");
            created.Body.Source.Should().Be("mirror");
            created.Body.SignatureKeyId.Should().Be("lab");
            created.Body.SignatureFingerprint.Should().NotBeNullOrWhiteSpace();
            created.Body.Risk.Should().Be("low");
            created.Body.Token.Should().StartWith("vc_int_");
            created.Body.Version.Should().Be("1.0.0");

            var replay = await PostAsync(admin, "install", index, id, "1.0.0", "registry-install-1-" + id);
            replay.Status.Should().Be(HttpStatusCode.OK);
            replay.Body.Token.Should().BeNull();

            var listed = await admin.GetStringAsync($"/api/v1/admin/workspaces/{WorkspaceId}/plugin-registry");
            listed.Should().Contain(id).And.Contain("Lab Publisher").And.NotContain("vc_int_").And.NotContain(PluginRegistryTestKeys.PrivatePem);

            await using var db = factory.CreateMigratorDbContext();
            var audits = await db.AuditEvents.IgnoreQueryFilters()
                .Where(item => item.Action == AuditActions.PluginRegistryInstall && item.MetadataJson.Contains(id))
                .Select(item => item.MetadataJson)
                .ToListAsync();
            audits.Should().NotBeEmpty();
            audits.Should().OnlyContain(item => !item.Contains("vc_int_", StringComparison.Ordinal) && !item.Contains(PluginRegistryTestKeys.PrivatePem, StringComparison.Ordinal));
        }
        finally
        {
            await CleanupAsync(id);
        }
    }

    [Fact]
    public async Task Invalid_signature_checksum_grant_and_remote_catalog_block_install()
    {
        var id = Id();
        using var admin = Client("demo");
        var index = Index(Manifest(id, "1.0.0"));
        var tampered = index.Replace("Lab Publisher", "Lab PublisheX", StringComparison.Ordinal);
        var forged = await PostAsync(admin, "install", tampered, id, "1.0.0", "registry-forge-" + id);
        forged.Status.Should().Be(HttpStatusCode.BadRequest);
        forged.Raw.Should().Contain(PluginRegistryErrors.InvalidSignature);

        var payload = Build(Manifest(id, "1.0.0")) with { Checksum = "sha256=" + new string('0', 64) };
        var badPayload = PluginRegistryRules.CanonicalPayload(payload);
        var badIndex = PluginRegistryRules.WrapIndex([(badPayload, PluginRegistryRules.Sign(PluginRegistryTestKeys.PrivatePem, badPayload))]);
        var checksum = await PostAsync(admin, "install", badIndex, id, "1.0.0", "registry-sum-" + id);
        checksum.Raw.Should().Contain(PluginRegistryErrors.InvalidChecksum);

        var missingGrant = await admin.PostAsJsonAsync(Url("install"), new
        {
            index,
            pluginId = id,
            version = "1.0.0",
            grantCapabilities = Array.Empty<string>(),
            idempotencyKey = "registry-grant-" + id
        });
        (await missingGrant.Content.ReadAsStringAsync()).Should().Contain(PluginRegistryErrors.GrantRequired);

        var remote = await admin.PostAsJsonAsync(Url("preview"), new
        {
            index,
            catalogUrl = "http://169.254.169.254/latest/meta-data"
        });
        remote.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await remote.Content.ReadAsStringAsync()).Should().Contain(PluginRegistryErrors.EgressBlocked);

        var https = Build(Manifest(id, "1.0.0")) with { ArtifactRef = "https://169.254.169.254/latest" };
        PluginRegistryRules.TryBuildIndex(PluginRegistryTestKeys.PrivatePem, [https], out var httpsIndex, out _).Should().BeTrue();
        var preview = await admin.PostAsJsonAsync(Url("preview"), new { index = httpsIndex });
        (await preview.Content.ReadAsStringAsync()).Should().Contain(PluginRegistryErrors.EgressBlocked);

        await using var db = factory.CreateMigratorDbContext();
        (await db.PluginRegistryInstalls.IgnoreQueryFilters().CountAsync(item => item.PluginId == id)).Should().Be(0);
    }

    [Fact]
    public async Task Update_requires_capability_confirmation_rollback_restores_and_revoke_disables()
    {
        var id = Id();
        try
        {
            using var admin = Client("demo");
            var first = Index(Manifest(id, "1.0.0"));
            (await PostAsync(admin, "install", first, id, "1.0.0", "registry-life-install-" + id)).Status.Should().Be(HttpStatusCode.Created);

            var second = Index(Manifest(id, "1.1.0", extra: true));
            var blocked = await PostAsync(admin, "update", second, id, "1.1.0", "registry-life-update-" + id, confirm: []);
            blocked.Status.Should().Be(HttpStatusCode.Conflict);
            blocked.Raw.Should().Contain(PluginRegistryErrors.CapabilityConfirmation);

            var older = Index(Manifest(id, "0.9.0"));
            var downgrade = await PostAsync(admin, "update", older, id, "0.9.0", "registry-life-down-" + id, confirm: ["messages.send"]);
            downgrade.Raw.Should().Contain(PluginRegistryErrors.DowngradeRejected);

            await using (var before = factory.CreateMigratorDbContext())
            {
                var row = await before.PluginRegistryInstalls.IgnoreQueryFilters().SingleAsync(item => item.PluginId == id);
                row.Version.Should().Be("1.0.0");
            }

            var updated = await PostAsync(admin, "update", second, id, "1.1.0", "registry-life-confirm-" + id, confirm: ["history.read"]);
            updated.Status.Should().Be(HttpStatusCode.OK);
            updated.Body.Version.Should().Be("1.1.0");
            updated.Body.Capabilities.Should().Contain("history.read");
            updated.Body.Token.Should().BeNull();

            var rolled = await admin.PostAsJsonAsync(Url("rollback"), new { pluginId = id, idempotencyKey = "registry-life-rollback-" + id });
            rolled.StatusCode.Should().Be(HttpStatusCode.OK);
            var rolledBody = await rolled.Content.ReadFromJsonAsync<RegistryBody>();
            rolledBody!.Version.Should().Be("1.0.0");
            rolledBody.Capabilities.Should().NotContain("history.read");

            var revoked = await admin.PostAsJsonAsync(Url("revoke"), new { pluginId = id, version = "1.0.0", idempotencyKey = "registry-life-revoke-" + id });
            revoked.StatusCode.Should().Be(HttpStatusCode.OK);
            await using (var db = factory.CreateMigratorDbContext())
            {
                var plugin = await db.InstalledPlugins.IgnoreQueryFilters().SingleAsync(item => item.PluginId == id);
                plugin.Enabled.Should().BeFalse();
                plugin.Version.Should().Be("1.0.0");
            }

            var again = await PostAsync(admin, "install", first, id, "1.0.0", "registry-life-reinstall-" + id);
            again.Raw.Should().Contain(PluginRegistryErrors.VersionRevoked);
        }
        finally
        {
            await CleanupAsync(id);
        }
    }

    [Fact]
    public async Task Missing_dependency_blocks_until_the_dependency_is_installed()
    {
        var root = Id();
        var child = Id();
        try
        {
            using var admin = Client("demo");
            var childPayload = Build(Manifest(child, "1.0.0")) with { DependsOn = [root + "@1.0.0"] };
            PluginRegistryRules.TryBuildIndex(PluginRegistryTestKeys.PrivatePem, [childPayload], out var childAlone, out _).Should().BeTrue();
            var incomplete = await PostAsync(admin, "install", childAlone, child, "1.0.0", "registry-dep-incomplete-" + child);
            incomplete.Raw.Should().Contain(PluginRegistryErrors.InvalidIndex);

            PluginRegistryRules.TryBuildIndex(
                PluginRegistryTestKeys.PrivatePem,
                [Build(Manifest(root, "1.0.0")), childPayload],
                out var graph,
                out _).Should().BeTrue();
            var before = await PostAsync(admin, "install", graph, child, "1.0.0", "registry-dep-before-" + child);
            before.Raw.Should().Contain(PluginRegistryErrors.DependencyMissing);

            (await PostAsync(admin, "install", graph, root, "1.0.0", "registry-dep-root-" + root)).Status.Should().Be(HttpStatusCode.Created);
            var after = await PostAsync(admin, "install", graph, child, "1.0.0", "registry-dep-after-" + child);
            after.Status.Should().Be(HttpStatusCode.Created);
            after.Body.Version.Should().Be("1.0.0");
        }
        finally
        {
            await CleanupAsync(child);
            await CleanupAsync(root);
        }
    }

    [Fact]
    public async Task Kill_switch_hides_the_registry()
    {
        using var disabled = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Integrations:PluginRegistry:Enabled", "false"));
        using var client = disabled.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "demo");
        var response = await client.GetAsync($"/api/v1/admin/workspaces/{WorkspaceId}/plugin-registry");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain(PluginRegistryErrors.Disabled);
    }

    private static string Id() => "registry-" + Guid.NewGuid().ToString("N")[..12];

    private static string Manifest(string id, string version, bool extra = false) => extra
        ? "{\"schema\":\"vibechat.plugin.manifest.v1\",\"id\":\"" + id + "\",\"name\":\"Registry Note\",\"version\":\"" + version + "\",\"capabilities\":[\"history.read\",\"messages.send\"]}"
        : "{\"schema\":\"vibechat.plugin.manifest.v1\",\"id\":\"" + id + "\",\"name\":\"Registry Note\",\"version\":\"" + version + "\",\"capabilities\":[\"messages.send\"]}";

    private static RegistryPayload Build(string manifest)
    {
        PluginManifestRules.TryParse(manifest, out var parsed, out var error).Should().BeTrue(error);
        return new RegistryPayload(
            "lab", "mirror", "mirror", parsed!.Id, parsed.Version, "Lab Publisher",
            "mirror://lab/" + parsed.Id + "/" + parsed.Version, "sha256=" + new string('a', 64),
            "built-in-mirror", "1", "1", [], "low", false, null, parsed.Capabilities, parsed.CanonicalJson);
    }

    private static string Index(string manifest)
    {
        PluginRegistryRules.TryBuildIndex(PluginRegistryTestKeys.PrivatePem, [Build(manifest)], out var index, out var error).Should().BeTrue(error);
        return index;
    }

    private static string Url(string action) => $"/api/v1/admin/workspaces/{WorkspaceId}/plugin-registry/{action}";

    private async Task<Call> PostAsync(HttpClient admin, string action, string index, string pluginId, string version, string key, string[]? confirm = null)
    {
        var response = await admin.PostAsJsonAsync(Url(action), new
        {
            index,
            pluginId,
            version,
            grantCapabilities = new[] { "messages.send", "commands.slash" },
            confirmCapabilities = confirm,
            channelIds = Array.Empty<Guid>(),
            allowDms = false,
            idempotencyKey = key
        });
        var raw = await response.Content.ReadAsStringAsync();
        var body = response.IsSuccessStatusCode ? JsonSerializer.Deserialize<RegistryBody>(raw, Json) ?? new RegistryBody() : new RegistryBody();
        return new Call(response.StatusCode, raw, body);
    }

    private async Task CleanupAsync(string pluginId)
    {
        using var admin = Client("demo");
        await using var db = factory.CreateMigratorDbContext();
        var plugin = await db.InstalledPlugins.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.PluginId == pluginId && item.WorkspaceId == new WorkspaceId(WorkspaceId));
        if (plugin is not null)
        {
            await admin.DeleteAsync($"/api/v1/admin/workspaces/{WorkspaceId}/plugins/{plugin.Id}");
        }

        var rows = await db.PluginRegistryInstalls.IgnoreQueryFilters().Where(item => item.PluginId == pluginId).ToListAsync();
        var quarantine = await db.PluginRegistryQuarantines.IgnoreQueryFilters().Where(item => item.PluginId == pluginId).ToListAsync();
        db.PluginRegistryInstalls.RemoveRange(rows);
        db.PluginRegistryQuarantines.RemoveRange(quarantine);
        await db.SaveChangesAsync();
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Call(HttpStatusCode Status, string Raw, RegistryBody Body);

    private sealed class RegistryBody
    {
        public string? Version { get; set; }
        public string? Publisher { get; set; }
        public string? Source { get; set; }
        public string? SignatureKeyId { get; set; }
        public string? SignatureFingerprint { get; set; }
        public string[]? Capabilities { get; set; }
        public string? Risk { get; set; }
        public string? Token { get; set; }
    }
}
