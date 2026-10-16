using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

[Collection(SecurityCollection.Name)]
public sealed class AutomationSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid ChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Member_and_other_tenant_cannot_manage_and_webhook_token_is_hashed()
    {
        using var alice = Client("alice");
        var denied = await alice.PostAsJsonAsync($"/api/v1/workspaces/{WorkspaceId}/automations", new
        {
            idempotencyKey = $"sec-{Guid.NewGuid():N}"[..20],
            name = "Negado",
            definition = JsonSerializer.Deserialize<JsonElement>(Definition("message", "post_message", Permissions.Message.Send)),
        });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var demo = Client("demo");
        var created = await demo.PostAsJsonAsync($"/api/v1/workspaces/{WorkspaceId}/automations", new
        {
            idempotencyKey = $"ok-{Guid.NewGuid():N}"[..20],
            name = "Gancho",
            definition = JsonSerializer.Deserialize<JsonElement>(Definition("webhook", "notify", Permissions.Automation.Notify)),
        });
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<AutomationDto>(JsonOptions))!.Id;
        var published = await demo.PostAsync($"/api/v1/workspaces/{WorkspaceId}/automations/{id}/publish", null);
        published.StatusCode.Should().Be(HttpStatusCode.OK, await published.Content.ReadAsStringAsync());
        var token = (await published.Content.ReadFromJsonAsync<AutomationDto>(JsonOptions))!.WebhookToken;
        token.Should().StartWith(AutomationPolicies.TokenPrefix);
        (await demo.PostAsync($"/api/v1/workspaces/{WorkspaceId}/automations/{id}/enable", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = factory.CreateMigratorDbContext();
        var row = await db.AutomationDefinitions.IgnoreQueryFilters().SingleAsync(item => item.Id == id);
        row.WebhookTokenHash.Should().NotBe(token);
        row.WebhookTokenHash.Should().Be(AutomationPolicies.HashToken(token!));
        row.DraftJson.Should().NotContain(token);

        var listed = await demo.GetFromJsonAsync<ListDto>($"/api/v1/workspaces/{WorkspaceId}/automations", JsonOptions);
        listed!.Items.Single(item => item.Id == id).WebhookToken.Should().BeNull();

        var wrong = await alice.PostAsJsonAsync($"/api/v1/workspaces/{WorkspaceId}/automations/{id}/hooks", new { eventId = "hook-1", token });
        wrong.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var hook = factory.CreateClient();
        hook.DefaultRequestHeaders.Add("X-Automation-Token", token);
        var accepted = await hook.PostAsJsonAsync($"/api/v1/workspaces/{WorkspaceId}/automations/{id}/hooks", new
        {
            eventId = "hook-1",
            token = "vc_auto_should_not_stick",
            state = "fired",
        });
        accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var run = await accepted.Content.ReadFromJsonAsync<RunDto>(JsonOptions);
        run!.EventJson.Should().NotContain("vc_auto_");
        run.EventJson.Should().NotContain("should_not_stick");

        var foreignId = VibeChat.SharedKernel.WorkspaceId.New();
        db.Workspaces.Add(new Workspace
        {
            Id = foreignId,
            TenantId = new TenantId(foreignId.Value),
            Name = "Auto sec",
            Slug = $"auto-{foreignId.Value:N}"[..20],
            AiEnabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var cross = await demo.GetAsync($"/api/v1/workspaces/{foreignId.Value}/automations");
        cross.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private static string Definition(string trigger, string action, string capability)
    {
        var channel = trigger == "webhook" ? "" : $"""
            "channelId": "{ChannelId}",
            """;
        var channels = trigger == "webhook" ? "[]" : $"""[ "{ChannelId}" ]""";
        var users = action == "notify" ? $"""[ "{SeedData.DemoUserId.Value}" ]""" : "[]";
        var body = action == "post_message" ? "oi" : "";
        var title = action == "create_task" ? "tarefa" : "";
        return $$"""
        {
          "contractVersion": "automation.definition.v1",
          "trigger": { "type": "{{trigger}}", "channelIds": [], "formIds": [], "event": "", "intervalMinutes": 0 },
          "conditions": [],
          "actions": [ {
            "type": "{{action}}",
            "capability": "{{capability}}",
            {{channel}}
            "body": "{{body}}",
            "title": "{{title}}",
            "taskId": null,
            "state": "",
            "userIds": {{users}},
            "connectorId": null
          } ],
          "channelIds": {{channels}},
          "secretRefs": []
        }
        """;
    }

    private sealed record AutomationDto(Guid Id, string? WebhookToken);

    private sealed record ListDto(ItemDto[] Items);

    private sealed record ItemDto(Guid Id, string? WebhookToken);

    private sealed record RunDto(string EventJson);
}
