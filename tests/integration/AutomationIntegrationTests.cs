using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class AutomationIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid ChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Retry_does_not_duplicate_disable_blocks_and_logs_omit_secrets()
    {
        using var demo = Client("demo");
        var marker = $"aviso-{Guid.NewGuid():N}"[..18];
        var secretBody = $"segredo-{Guid.NewGuid():N}";
        var created = await CreateAsync(demo, "Aviso", PostMessage(marker));
        var published = await PostOk<AutomationDto>(demo, $"{Path(created.Id)}/publish");
        published.IdentityUserId.Should().NotBeNull();
        published.WebhookToken.Should().BeNull();
        published.Capabilities.Should().Contain(Permissions.Message.Send);
        await DrainBacklogAsync();
        await PostOk<AutomationDto>(demo, $"{Path(created.Id)}/enable");

        using var alice = Client("alice");
        var messageId = Guid.NewGuid();
        var sent = await alice.PostAsJsonAsync($"/api/v1/channels/{ChannelId}/messages", new
        {
            messageId,
            idempotencyKey = $"msg-{messageId:N}",
            body = secretBody,
        });
        sent.StatusCode.Should().Be(HttpStatusCode.Accepted, await sent.Content.ReadAsStringAsync());

        await DrainAsync();

        await using var db = factory.CreateMigratorDbContext();
        var runs = await db.AutomationRuns.IgnoreQueryFilters().Where(row => row.AutomationId == created.Id).ToListAsync();
        runs.Should().ContainSingle();
        runs[0].EventJson.Should().NotContain(secretBody);
        runs[0].Status.Should().Be(AutomationRunStatus.Succeeded);
        var steps = await db.AutomationSteps.IgnoreQueryFilters().Where(row => row.RunId == runs[0].Id).ToListAsync();
        steps.Should().ContainSingle();
        steps[0].LogJson.Should().NotContain(secretBody);
        steps[0].LogJson.Should().NotContain("vc_auto_");
        var copies = await db.Messages.IgnoreQueryFilters().CountAsync(row => row.Body == marker);
        copies.Should().Be(1);

        var again = await demo.PostAsJsonAsync($"{Path(created.Id)}/trigger", new
        {
            eventId = messageId.ToString("D"),
            type = "message",
            eventName = "created",
            channelId = ChannelId,
            sourceId = messageId,
            state = "created",
        });
        again.StatusCode.Should().Be(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
        (await db.Messages.IgnoreQueryFilters().CountAsync(row => row.Body == marker)).Should().Be(1);

        await PostOk<AutomationDto>(demo, $"{Path(created.Id)}/disable");
        var blocked = await demo.PostAsJsonAsync($"{Path(created.Id)}/trigger", new
        {
            eventId = $"off-{Guid.NewGuid():N}"[..20],
            type = "message",
            eventName = "created",
            channelId = ChannelId,
            sourceId = messageId,
            state = "created",
        });
        blocked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await blocked.Content.ReadAsStringAsync()).Should().Contain("AutomationDisabled");
        (await db.AutomationRuns.IgnoreQueryFilters().CountAsync(row => row.AutomationId == created.Id && row.Mode == AutomationRunMode.Live)).Should().Be(1);
    }

    [Fact]
    public async Task Loop_is_stopped_and_missing_capability_does_not_post()
    {
        using var demo = Client("demo");
        var marker = $"eco-{Guid.NewGuid():N}"[..16];
        var created = await CreateAsync(demo, "Eco", PostMessage(marker));
        await PostOk<AutomationDto>(demo, $"{Path(created.Id)}/publish");
        await DrainBacklogAsync();
        await PostOk<AutomationDto>(demo, $"{Path(created.Id)}/enable");

        await using (var db = factory.CreateMigratorDbContext())
        {
            var version = await db.AutomationVersions.IgnoreQueryFilters().SingleAsync(row => row.AutomationId == created.Id);
            version.CapabilitiesJson = "[]";
            await db.SaveChangesAsync();
        }

        using var alice = Client("alice");
        var deniedId = Guid.NewGuid();
        (await alice.PostAsJsonAsync($"/api/v1/channels/{ChannelId}/messages", new
        {
            messageId = deniedId,
            idempotencyKey = $"deny-{deniedId:N}",
            body = "sem-capability",
        })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await DrainAsync();
        await using (var db = factory.CreateMigratorDbContext())
        {
            (await db.Messages.IgnoreQueryFilters().CountAsync(row => row.Body == marker)).Should().Be(0);
            var deniedRun = await db.AutomationRuns.IgnoreQueryFilters()
                .SingleAsync(row => row.AutomationId == created.Id && row.EventId == deniedId.ToString("D"));
            var step = await db.AutomationSteps.IgnoreQueryFilters().SingleAsync(row => row.RunId == deniedRun.Id);
            step.ErrorCode.Should().Be("CapabilityDenied");
            step.EffectId.Should().BeNull();
            var version = await db.AutomationVersions.IgnoreQueryFilters().SingleAsync(row => row.AutomationId == created.Id);
            version.CapabilitiesJson = JsonSerializer.Serialize(new[] { Permissions.Message.Send });
            await db.SaveChangesAsync();
        }

        var startId = Guid.NewGuid();
        (await alice.PostAsJsonAsync($"/api/v1/channels/{ChannelId}/messages", new
        {
            messageId = startId,
            idempotencyKey = $"start-{startId:N}",
            body = "inicio-loop",
        })).StatusCode.Should().Be(HttpStatusCode.Accepted);

        for (var i = 0; i < 6; i++)
        {
            await DrainAsync();
        }

        await using var after = factory.CreateMigratorDbContext();
        var echoes = await after.Messages.IgnoreQueryFilters().CountAsync(row => row.Body == marker);
        echoes.Should().BeLessThanOrEqualTo(AutomationPolicies.MaxDepth);
        echoes.Should().BeGreaterThan(0);
        (await after.AuditEvents.IgnoreQueryFilters().CountAsync(row => row.Action == AutomationPolicies.LoopStopped && row.EntityId == created.Id.ToString()))
            .Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Disabled_flag_does_not_write()
    {
        using var disabledHost = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Automation:Enabled", "false"));
        using var disabled = disabledHost.CreateClient();
        disabled.DefaultRequestHeaders.Add("X-Dev-User", "demo");
        var off = await disabled.PostAsJsonAsync($"/api/v1/workspaces/{WorkspaceId}/automations", new
        {
            idempotencyKey = $"off-{Guid.NewGuid():N}"[..24],
            name = "Nada",
            definition = JsonSerializer.Deserialize<JsonElement>(PostMessage("nao")),
        });
        off.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await off.Content.ReadAsStringAsync()).Should().Contain("AutomationDisabled");
    }

    private async Task DrainBacklogAsync()
    {
        using var scope = factory.Services.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
        // Shared fixture: older rows fill the outbox page of 20 and hide the
        // message sent after enable. Drain them while this automation is still off.
        for (var i = 0; i < 25; i++)
        {
            var taken = await outbox.ProcessBatchAsync(CancellationToken.None);
            if (taken < 20)
            {
                return;
            }
        }
    }

    private async Task DrainAsync()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<AutomationService>().ProcessBatchAsync(CancellationToken.None);
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private async Task<AutomationDto> CreateAsync(HttpClient client, string name, string definition)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/workspaces/{WorkspaceId}/automations", new
        {
            idempotencyKey = $"auto-{Guid.NewGuid():N}"[..24],
            name,
            definition = JsonSerializer.Deserialize<JsonElement>(definition),
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AutomationDto>(JsonOptions))!;
    }

    private static async Task<T> PostOk<T>(HttpClient client, string path)
    {
        var response = await client.PostAsync(path, null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private static string Path(Guid id) => $"/api/v1/workspaces/{WorkspaceId}/automations/{id}";

    private static string PostMessage(string body) =>
        $$"""
        {
          "contractVersion": "automation.definition.v1",
          "trigger": { "type": "message", "channelIds": [], "formIds": [], "event": "created", "intervalMinutes": 0 },
          "conditions": [],
          "actions": [ {
            "type": "post_message",
            "capability": "message.send",
            "channelId": "{{ChannelId}}",
            "body": "{{body}}",
            "title": "",
            "taskId": null,
            "state": "",
            "userIds": [],
            "connectorId": null
          } ],
          "channelIds": [ "{{ChannelId}}" ],
          "secretRefs": []
        }
        """;

    private sealed record AutomationDto(Guid Id, Guid? IdentityUserId, string? WebhookToken, string[] Capabilities);
}
