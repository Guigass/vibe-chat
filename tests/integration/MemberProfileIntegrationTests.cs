using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Audit;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class MemberProfileIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public async Task Owner_updates_card_and_peer_reads_it()
    {
        using var alice = Client("alice");
        using var bob = Client("bob");
        var updated = await alice.PutAsJsonAsync("/api/v1/me/profile", new
        {
            displayName = "Alice Mendes",
            jobTitle = "Engenheira",
            about = "Cuida do canal geral",
            highlightMessage = "disponível para revisão"
        });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);

        var seen = await bob.GetFromJsonAsync<CardDto>(ProfilePath(SeedData.AliceUserId.Value));
        seen!.DisplayName.Should().Be("Alice Mendes");
        seen.JobTitle.Should().Be("Engenheira");
        seen.About.Should().Be("Cuida do canal geral");
        seen.HighlightMessage.Should().Be("disponível para revisão");
        seen.Email.Should().Be("alice@vibechat.local");
        seen.AvatarUrl.Should().BeNull();

        await using var db = factory.CreateMigratorDbContext();
        var audit = await db.AuditEvents.IgnoreQueryFilters()
            .Where(x => x.ActorUserId == SeedData.AliceUserId && x.Action == AuditActions.ProfileUpdate)
            .OrderByDescending(x => x.OccurredAt)
            .FirstAsync();
        audit.MetadataJson.Should().NotContain("Engenheira");
        audit.MetadataJson.Should().NotContain("Alice Mendes");

        (await alice.PutAsJsonAsync("/api/v1/me/profile", new { displayName = "Alice" }))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Avatar_upload_is_readable_by_a_peer_and_delete_restores_initials()
    {
        using var alice = Client("alice");
        using var bob = Client("bob");
        using var upload = new MultipartFormDataContent();
        var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        upload.Add(file, "file", "a.png");
        var posted = await alice.PostAsync("/api/v1/me/profile/avatar", upload);
        posted.StatusCode.Should().Be(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());
        var card = await posted.Content.ReadFromJsonAsync<CardDto>();
        card!.AvatarUrl.Should().NotBeNullOrWhiteSpace();

        var bytes = await bob.GetByteArrayAsync(card.AvatarUrl);
        bytes.Should().Equal(Png);

        var members = await bob.GetFromJsonAsync<MemberDto[]>($"/api/v1/workspaces/{WorkspaceId}/members");
        members!.Single(x => x.UserId == SeedData.AliceUserId.Value).AvatarUrl.Should().NotBeNullOrWhiteSpace();

        using var text = new MultipartFormDataContent();
        var bad = new ByteArrayContent("hello"u8.ToArray());
        bad.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        text.Add(bad, "file", "a.txt");
        var rejected = await alice.PostAsync("/api/v1/me/profile/avatar", text);
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await rejected.Content.ReadAsStringAsync()).Should().Contain(MemberProfileRules.AvatarTypeNotAllowed);

        (await alice.DeleteAsync("/api/v1/me/profile/avatar")).EnsureSuccessStatusCode();
        var gone = await bob.GetAsync(AvatarPath(SeedData.AliceUserId.Value));
        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Idempotency_key_replays_the_same_card()
    {
        using var alice = Client("alice");
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/me/profile")
        {
            Content = JsonContent.Create(new { displayName = "Alice", jobTitle = "Staff" })
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "b167-replay");
        var first = await alice.SendAsync(request);
        first.EnsureSuccessStatusCode();
        var body = await first.Content.ReadAsStringAsync();

        using var again = new HttpRequestMessage(HttpMethod.Put, "/api/v1/me/profile")
        {
            Content = JsonContent.Create(new { displayName = "Alice", jobTitle = "Staff" })
        };
        again.Headers.TryAddWithoutValidation("Idempotency-Key", "b167-replay");
        var second = await alice.SendAsync(again);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Content.ReadAsStringAsync()).Should().Be(body);

        using var conflict = new HttpRequestMessage(HttpMethod.Put, "/api/v1/me/profile")
        {
            Content = JsonContent.Create(new { displayName = "Alice", jobTitle = "Outro" })
        };
        conflict.Headers.TryAddWithoutValidation("Idempotency-Key", "b167-replay");
        var mismatched = await alice.SendAsync(conflict);
        mismatched.StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await alice.PutAsJsonAsync("/api/v1/me/profile", new { displayName = "Alice", jobTitle = "" }))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Oversized_text_is_rejected()
    {
        using var alice = Client("alice");
        var response = await alice.PutAsJsonAsync("/api/v1/me/profile", new
        {
            displayName = "Alice",
            about = new string('x', 501)
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(MemberProfileRules.AboutTooLong);
    }

    private static string ProfilePath(Guid userId) =>
        $"/api/v1/workspaces/{WorkspaceId}/members/{userId}/profile";

    private static string AvatarPath(Guid userId) =>
        $"/api/v1/workspaces/{WorkspaceId}/members/{userId}/profile/avatar";

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private sealed record CardDto(
        Guid UserId,
        string DisplayName,
        string? Email,
        string? JobTitle,
        string? About,
        string? HighlightMessage,
        string? AvatarUrl);

    private sealed record MemberDto(Guid UserId, string DisplayName, string Email, string Role, string? AvatarUrl);
}
