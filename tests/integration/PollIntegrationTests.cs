using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class PollIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid DemoChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Create_single_vote_replaces_and_multiple_accumulates()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var single = await CreatePollAsync(alice, allowMultiple: false, anonymous: false);
        var first = single.Poll!.Options[0].Id;
        var second = single.Poll.Options[1].Id;

        var vote1 = await alice.PostAsJsonAsync($"/api/v1/polls/{single.Id}/votes", new { optionIds = new[] { first } });
        vote1.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterFirst = await vote1.Content.ReadFromJsonAsync<PollDto>(JsonOptions);
        afterFirst!.Options.Single(o => o.Id == first).VoteCount.Should().Be(1);

        var vote2 = await alice.PostAsJsonAsync($"/api/v1/polls/{single.Id}/votes", new { optionIds = new[] { second } });
        vote2.StatusCode.Should().Be(HttpStatusCode.OK);
        var replaced = await vote2.Content.ReadFromJsonAsync<PollDto>(JsonOptions);
        replaced!.Options.Single(o => o.Id == first).VoteCount.Should().Be(0);
        replaced.Options.Single(o => o.Id == second).VoteCount.Should().Be(1);
        replaced.Options.Single(o => o.Id == second).Voters.Should().NotBeNull();
        replaced.Options.Single(o => o.Id == second).Voters!.Should().Contain(v => v.UserId != Guid.Empty);

        var twoOptions = await alice.PostAsJsonAsync($"/api/v1/polls/{single.Id}/votes", new { optionIds = new[] { first, second } });
        twoOptions.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var multi = await CreatePollAsync(alice, allowMultiple: true, anonymous: false);
        var m1 = multi.Poll!.Options[0].Id;
        var m2 = multi.Poll.Options[1].Id;
        var multiVote = await alice.PostAsJsonAsync($"/api/v1/polls/{multi.Id}/votes", new { optionIds = new[] { m1, m2 } });
        multiVote.StatusCode.Should().Be(HttpStatusCode.OK);
        var multiResult = await multiVote.Content.ReadFromJsonAsync<PollDto>(JsonOptions);
        multiResult!.Options.Single(o => o.Id == m1).VoteCount.Should().Be(1);
        multiResult.Options.Single(o => o.Id == m2).VoteCount.Should().Be(1);
        multiResult.TotalVotes.Should().Be(2);
    }

    [Fact]
    public async Task Anonymous_history_omits_voter_identity()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        var created = await CreatePollAsync(alice, allowMultiple: false, anonymous: true);
        var optionId = created.Poll!.Options[0].Id;
        (await alice.PostAsJsonAsync($"/api/v1/polls/{created.Id}/votes", new { optionIds = new[] { optionId } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var history = await alice.GetAsync($"/api/v1/channels/{DemoChannelId}/messages?after=0&limit=100");
        history.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await history.Content.ReadAsStringAsync());
        var poll = doc.RootElement.GetProperty("messages")
            .EnumerateArray()
            .Select(m => m.TryGetProperty("poll", out var p) ? p : default)
            .First(p => p.ValueKind == JsonValueKind.Object && p.GetProperty("id").GetGuid() == created.Id);
        var pollJson = poll.GetRawText().ToLowerInvariant();
        pollJson.Should().NotContain("userid");
        pollJson.Should().NotContain("voters");
    }

    [Fact]
    public async Task Close_rejects_votes_and_worker_closes_expired()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        var created = await CreatePollAsync(alice, allowMultiple: false, anonymous: false);
        var close = await alice.PostAsync($"/api/v1/polls/{created.Id}/close", null);
        close.StatusCode.Should().Be(HttpStatusCode.OK);

        var vote = await alice.PostAsJsonAsync(
            $"/api/v1/polls/{created.Id}/votes",
            new { optionIds = new[] { created.Poll!.Options[0].Id } });
        vote.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var timed = await CreatePollAsync(alice, allowMultiple: false, anonymous: false, closesAt: DateTimeOffset.UtcNow.AddHours(1));
        await using var db = factory.CreateMigratorDbContext();
        var poll = await db.Polls.IgnoreQueryFilters().SingleAsync(x => x.MessageId == new MessageId(timed.Id));
        poll.ClosesAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        using var scope = factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<PollCloseProcessor>();
        var closed = await processor.ProcessBatchAsync(CancellationToken.None);
        closed.Should().BeGreaterThanOrEqualTo(1);

        var after = await alice.PostAsJsonAsync(
            $"/api/v1/polls/{timed.Id}/votes",
            new { optionIds = new[] { timed.Poll!.Options[0].Id } });
        after.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Validation_rejects_bad_question_options_and_past_deadline()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var tooLong = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/polls",
            new CreatePollRequest(Guid.NewGuid(), $"idem-{Guid.NewGuid():N}", new string('q', 501), ["a", "b"]));
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var oneOption = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/polls",
            new CreatePollRequest(Guid.NewGuid(), $"idem-{Guid.NewGuid():N}", "Só uma", ["a"]));
        oneOption.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var past = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/polls",
            new CreatePollRequest(
                Guid.NewGuid(),
                $"idem-{Guid.NewGuid():N}",
                "Prazo velho",
                ["a", "b"],
                false,
                false,
                DateTimeOffset.UtcNow.AddMinutes(-5)));
        past.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<MessageDto> CreatePollAsync(
        HttpClient client,
        bool allowMultiple,
        bool anonymous,
        DateTimeOffset? closesAt = null)
    {
        var id = Guid.NewGuid();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/polls",
            new CreatePollRequest(
                id,
                $"idem-poll-{id:N}",
                $"Pergunta {id:N}"[..24],
                ["Alpha", "Beta", "Gamma"],
                allowMultiple,
                anonymous,
                closesAt));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var dto = await response.Content.ReadFromJsonAsync<MessageDto>(JsonOptions);
        dto.Should().NotBeNull();
        dto!.Poll.Should().NotBeNull();
        return dto;
    }

    private sealed record CreatePollRequest(
        Guid MessageId,
        string IdempotencyKey,
        string Question,
        string[] Options,
        bool AllowMultiple = false,
        bool Anonymous = false,
        DateTimeOffset? ClosesAt = null);

    private sealed record MessageDto(
        Guid Id,
        Guid ChannelId,
        long Sequence,
        Guid AuthorId,
        string Body,
        PollDto? Poll = null);

    private sealed record PollDto(
        Guid Id,
        Guid MessageId,
        string Question,
        bool AllowMultiple,
        bool Anonymous,
        int TotalVotes,
        PollOptionDto[] Options);

    private sealed record PollVoterDto(Guid UserId, string DisplayName);
    private sealed record PollOptionDto(
        Guid Id,
        string Text,
        int Position,
        int VoteCount,
        int Percent,
        bool VotedByMe,
        PollVoterDto[]? Voters = null);
}
