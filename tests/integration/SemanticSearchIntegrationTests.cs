using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Audit;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class SemanticSearchIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Query_cites_visible_message_and_forgets_it_after_delete()
    {
        await EnableAsync();
        using var alice = Client("alice");
        var messageId = Guid.NewGuid();
        var body = $"postgres backup runbook domingo {messageId:N}";
        var sent = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages",
            new SendMessageRequest(messageId, $"idem-{messageId:N}", body, null, null));
        sent.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await DrainOutboxAsync();

        var cited = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/semantic/query",
            new QueryBody("postgres backup runbook domingo"));
        cited.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await cited.Content.ReadFromJsonAsync<QueryDto>(JsonOptions))!;
        page.Evidence.Should().Be("cited");
        page.Answer.Should().Contain("postgres backup runbook domingo");
        page.Citations.Should().Contain(item => item.SourceId == messageId && item.SourceKind == "message");
        page.Answer.Should().Be(string.Join("\n", page.Citations.Select((item, index) => $"[{index + 1}] {item.Excerpt}")));

        var deleted = await alice.DeleteAsync($"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages/{messageId}");
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await DrainOutboxAsync();
        await using (var db = factory.CreateMigratorDbContext())
        {
            var chunks = await db.SemanticChunks.IgnoreQueryFilters()
                .Where(row => row.SourceId == messageId)
                .ToListAsync();
            chunks.Should().NotBeEmpty();
            chunks.Should().OnlyContain(row => row.DeletedAt != null && row.Text == string.Empty);
        }

        var after = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/semantic/query",
            new QueryBody(body));
        after.StatusCode.Should().Be(HttpStatusCode.OK);
        var forgotten = (await after.Content.ReadFromJsonAsync<QueryDto>(JsonOptions))!;
        forgotten.Citations.Should().NotContain(item => item.SourceId == messageId);
        (forgotten.Answer ?? string.Empty).Should().NotContain(messageId.ToString("N"));
    }

    [Fact]
    public async Task Private_channel_citation_follows_current_membership()
    {
        await EnableAsync();
        using var alice = Client("alice");
        using var bob = Client("bob");
        var created = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels",
            new CreateChannelBody($"sec-{Guid.NewGuid():N}"[..12], "Private", null));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var channel = (await created.Content.ReadFromJsonAsync<ChannelDto>(JsonOptions))!;
        var messageId = Guid.NewGuid();
        var secret = $"segredo privado postgres backup {messageId:N}";
        (await alice.PostAsJsonAsync(
            $"/api/v1/channels/{channel.Id}/messages",
            new SendMessageRequest(messageId, $"idem-{messageId:N}", secret, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        await DrainOutboxAsync();

        var hidden = await QueryAsync(bob, secret);
        hidden.Citations.Should().NotContain(item => item.SourceId == messageId);
        (hidden.Answer ?? string.Empty).Should().NotContain(messageId.ToString("N"));

        (await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels/{channel.Id}/members",
            new AddMemberBody(SeedData.BobUserId.Value))).StatusCode.Should().Be(HttpStatusCode.OK);
        var visible = await QueryAsync(bob, "segredo privado postgres backup");
        visible.Citations.Should().Contain(item => item.SourceId == messageId);

        (await alice.DeleteAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels/{channel.Id}/members/{SeedData.BobUserId.Value}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var revoked = await QueryAsync(bob, secret);
        revoked.Citations.Should().NotContain(item => item.SourceId == messageId);
        (revoked.Answer ?? string.Empty).Should().NotContain(messageId.ToString("N"));
    }

    [Fact]
    public async Task Kill_switch_hides_semantic_search_and_keeps_fts()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("SemanticSearch:Enabled", "false"));
        using var alice = host.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        var messageId = Guid.NewGuid();
        var body = $"fts continua {messageId:N}";
        (await alice.PostAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages",
            new SendMessageRequest(messageId, $"idem-{messageId:N}", body, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var semantic = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/semantic/query",
            new QueryBody("fts continua"));
        semantic.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await semantic.Content.ReadAsStringAsync()).Should().Contain("SemanticSearchDisabled");

        var processor = host.Services.GetRequiredService<OutboxProcessor>();
        await processor.ProcessBatchAsync(CancellationToken.None);
        var fts = await alice.GetAsync($"/api/v1/search/messages?workspaceId={WorkspaceId}&q={Uri.EscapeDataString("fts continua")}");
        fts.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fts.Content.ReadAsStringAsync()).Should().Contain("fts continua");
    }

    [Fact]
    public async Task Budget_and_report_are_audited_without_the_question_text()
    {
        await EnableAsync();
        using var alice = Client("alice");
        await using (var db = factory.CreateMigratorDbContext())
        {
            var settings = await db.SemanticSettings.IgnoreQueryFilters()
                .SingleAsync(row => row.WorkspaceId == SeedData.DemoWorkspaceId);
            settings.TokensUsed = settings.MonthlyTokenBudget;
            await db.SaveChangesAsync();
        }

        var limited = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/semantic/query",
            new QueryBody("postgres backup runbook"));
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await limited.Content.ReadAsStringAsync()).Should().Contain("SemanticBudgetExceeded");

        await using (var db = factory.CreateMigratorDbContext())
        {
            var settings = await db.SemanticSettings.IgnoreQueryFilters()
                .SingleAsync(row => row.WorkspaceId == SeedData.DemoWorkspaceId);
            settings.TokensUsed = 0;
            await db.SaveChangesAsync();
        }

        var messageId = Guid.NewGuid();
        (await alice.PostAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages",
            new SendMessageRequest(messageId, $"idem-{messageId:N}", $"postgres backup runbook domingo {messageId:N}", null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        await DrainOutboxAsync();
        var cited = await QueryAsync(alice, "postgres backup runbook domingo");
        cited.Citations.Should().NotBeEmpty();

        var report = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/semantic/report",
            new ReportBody(cited.Citations.Select(item => item.ChunkId).ToArray()));
        report.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var auditDb = factory.CreateMigratorDbContext();
        var events = await auditDb.AuditEvents.IgnoreQueryFilters()
            .Where(row => row.Action == AuditActions.SemanticQuery || row.Action == AuditActions.SemanticReport)
            .Select(row => row.MetadataJson)
            .ToListAsync();
        events.Should().Contain(json => json.Contains("SemanticBudgetExceeded", StringComparison.Ordinal) || json.Contains("budget", StringComparison.OrdinalIgnoreCase) || json.Contains("citations", StringComparison.Ordinal));
        string.Join('\n', events).Should().NotContain("postgres backup runbook");
    }

    private async Task EnableAsync()
    {
        using var demo = Client("demo");
        var response = await demo.PutAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/semantic/settings",
            new SettingsBody(true));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<QueryDto> QueryAsync(HttpClient client, string query)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/semantic/query",
            new QueryBody(query));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<QueryDto>(JsonOptions))!;
    }

    private async Task DrainOutboxAsync()
    {
        var processor = factory.Services.GetRequiredService<OutboxProcessor>();
        for (var i = 0; i < 5; i++)
        {
            if (await processor.ProcessBatchAsync(CancellationToken.None) == 0)
            {
                return;
            }
        }
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private sealed record SendMessageRequest(Guid MessageId, string IdempotencyKey, string Body, Guid? ReplyToMessageId, Guid? ForwardedFromMessageId);
    private sealed record CreateChannelBody(string Name, string Type, Guid? SpaceId);
    private sealed record AddMemberBody(Guid UserId);
    private sealed record SettingsBody(bool Enabled);
    private sealed record QueryBody(string Q);
    private sealed record ReportBody(Guid[] CitationIds);
    private sealed record ChannelDto(Guid Id);
    private sealed record QueryDto(string Evidence, string? Answer, SemanticCitationDto[] Citations);
    private sealed record SemanticCitationDto(Guid ChunkId, string SourceKind, Guid SourceId, string Excerpt);
}
