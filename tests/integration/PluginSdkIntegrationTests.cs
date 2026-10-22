using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using VibeChat.Audit;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.Sdk;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-136 — sandbox quickstart, webhook verifier and contract matrix.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class PluginSdkIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid ChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Quickstart_verifies_signatures_and_does_not_persist_a_message()
    {
        using var admin = Client("demo");
        await using var db = factory.CreateMigratorDbContext();
        var before = await db.Messages.IgnoreQueryFilters().CountAsync(message => message.ConversationId == new ChannelId(ChannelId));

        var catalog = await admin.GetStringAsync($"/api/v1/workspaces/{WorkspaceId}/developer/sdk");
        catalog.Should().Contain("\"sdkVersion\":\"1.0.0\"").And.Contain("event-consumer").And.Contain("connector");
        catalog.Should().NotContain(PluginSdkCatalog.SandboxSecret).And.NotContain("vc_int_");
        catalog.Should().Contain("\"compatible\":true");

        const string body = "{\"synthetic\":true}";
        var signature = VibeChatPluginSdk.ComputeSignature(PluginSdkCatalog.SandboxSecret, body);
        var deliveryId = $"sdk-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var accepted = await admin.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/developer/sdk/verify",
            new { body, signature, deliveryId, deliveredAt = now, seenDeliveryIds = Array.Empty<string>() });
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        (await accepted.Content.ReadAsStringAsync()).Should().Contain(PluginSdkErrors.Accepted).And.NotContain(PluginSdkCatalog.SandboxSecret);

        var replay = await admin.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/developer/sdk/verify",
            new { body, signature, deliveryId, deliveredAt = now, seenDeliveryIds = new[] { deliveryId } });
        (await replay.Content.ReadAsStringAsync()).Should().Contain(PluginSdkErrors.ReplayDetected);

        var forged = await admin.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/developer/sdk/verify",
            new { body, signature = "sha256=00", deliveryId = "other-delivery", deliveredAt = now });
        (await forged.Content.ReadAsStringAsync()).Should().Contain(PluginSdkErrors.InvalidSignature);

        var report = JsonNode.Parse(await admin.GetStringAsync($"/api/v1/workspaces/{WorkspaceId}/developer/sdk"))!;
        var contract = await admin.PostAsync($"/api/v1/workspaces/{WorkspaceId}/developer/sdk/contract-tests", null);
        contract.StatusCode.Should().Be(HttpStatusCode.OK);
        var contractBody = await contract.Content.ReadAsStringAsync();
        contractBody.Should().Contain("\"passed\":true").And.NotContain("vc_int_").And.NotContain(PluginSdkCatalog.SandboxSecret);
        ((JsonValue)report["digest"]!).GetValue<string>().Should().Be(PluginSdkCatalog.Digest());

        var key = $"quickstart-{Guid.NewGuid():N}";
        using var request = VibeChatPluginSdk.SandboxSend(WorkspaceId, ChannelId, "first message", key);
        request.Headers.Authorization.Should().BeNull();
        request.Headers.Contains(DeveloperPortalCatalog.TokenHeader).Should().BeFalse();
        var sandbox = await admin.SendAsync(request);
        sandbox.StatusCode.Should().Be(HttpStatusCode.OK);
        var sandboxBody = await sandbox.Content.ReadAsStringAsync();
        sandboxBody.Should().Contain("\"delivered\":false").And.Contain("\"synthetic\":true").And.NotContain("vc_int_");

        var missingKey = await admin.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/developer/sdk/sandbox",
            new { operation = "messages.send", channelId = ChannelId, body = "no-key" });
        missingKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var after = await db.Messages.IgnoreQueryFilters().CountAsync(message => message.ConversationId == new ChannelId(ChannelId));
        after.Should().Be(before);

        var audits = await db.AuditEvents.IgnoreQueryFilters()
            .Where(item => item.Action == AuditActions.DeveloperSdkSandbox)
            .Select(item => item.MetadataJson)
            .ToListAsync();
        audits.Should().NotBeEmpty();
        audits.Should().OnlyContain(item => !item.Contains("vc_int_", StringComparison.Ordinal) && !item.Contains(PluginSdkCatalog.SandboxSecret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Kill_switch_hides_the_sdk()
    {
        using var disabled = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Integrations:PluginSdk:Enabled", "false"));
        using var client = disabled.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "demo");
        var response = await client.GetAsync($"/api/v1/workspaces/{WorkspaceId}/developer/sdk");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain(PluginSdkErrors.Disabled);
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }
}
