using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

[Collection(SecurityCollection.Name)]
public sealed class FormSecurityTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid ChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Outsider_cannot_approve_and_other_tenants_are_forbidden()
    {
        using var alice = Client("alice");
        var created = await alice.PostAsJsonAsync($"/api/v1/workspaces/{WorkspaceId}/forms", new
        {
            idempotencyKey = $"sec-{Guid.NewGuid():N}",
            channelId = ChannelId,
            title = "Sigilo",
            schema = new
            {
                contractVersion = "form.schema.v1",
                fields = new[] { new { key = "reason", type = "text", label = "Motivo", required = true } },
                approval = new { mode = "single", required = 1, reviewerUserIds = new[] { SeedData.BobUserId.Value } },
                onApproved = "none",
            },
        });
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var formId = (await created.Content.ReadFromJsonAsync<FormDto>())!.Id;
        (await alice.PostAsync($"/api/v1/workspaces/{WorkspaceId}/forms/{formId}/publish", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var submitted = await alice.PostAsJsonAsync($"/api/v1/workspaces/{WorkspaceId}/forms/{formId}/submissions", new
        {
            idempotencyKey = $"ans-{Guid.NewGuid():N}",
            answers = new { reason = "secreto" },
        });
        submitted.StatusCode.Should().Be(HttpStatusCode.OK, await submitted.Content.ReadAsStringAsync());
        var submissionId = (await submitted.Content.ReadFromJsonAsync<SubmissionDto>())!.Id;

        var outsider = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/forms/{formId}/submissions/{submissionId}/decisions",
            new { idempotencyKey = $"no-{Guid.NewGuid():N}", decision = "approve" });
        outsider.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var db = factory.CreateMigratorDbContext();
        var foreignId = VibeChat.SharedKernel.WorkspaceId.New();
        db.Workspaces.Add(new Workspace
        {
            Id = foreignId,
            TenantId = new TenantId(foreignId.Value),
            Name = "Forms sec",
            Slug = $"forms-{foreignId.Value:N}"[..20],
            AiEnabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var cross = await alice.GetAsync($"/api/v1/workspaces/{foreignId.Value}/forms?channelId={ChannelId}");
        cross.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await db.FormDecisions.IgnoreQueryFilters().CountAsync(row => row.SubmissionId == submissionId)).Should().Be(0);
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private sealed record FormDto(Guid Id);

    private sealed record SubmissionDto(Guid Id);
}
