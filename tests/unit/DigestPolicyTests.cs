using FluentAssertions;
using VibeChat.Notifications;

namespace VibeChat.UnitTests;

public sealed class DigestPolicyTests
{
    [Fact]
    public void Production_digest_flag_defaults_off()
    {
        var json = File.ReadAllText(FindRepoFile(Path.Combine("apps", "api", "appsettings.json")));
        json.Should().Contain("\"Digests\"");
        json.Should().Contain("\"Enabled\": false");
    }

    [Fact]
    public void Cap_keeps_the_deterministic_list_bounded()
    {
        var capped = DigestPolicies.Cap(Enumerable.Range(0, 40));
        capped.Should().HaveCount(DigestPolicies.MaxPerKind);
        capped[0].Should().Be(0);
    }

    [Fact]
    public void Daily_slot_uses_the_user_time_zone()
    {
        var schedule = Schedule(DigestCadence.Daily, new TimeOnly(9, 0), weekday: 1, "America/Sao_Paulo");
        var before = new DateTimeOffset(2026, 10, 15, 11, 30, 0, TimeSpan.Zero);
        var after = new DateTimeOffset(2026, 10, 15, 12, 30, 0, TimeSpan.Zero);
        DigestPolicies.IsDue(schedule, before).Should().BeFalse();
        DigestPolicies.IsDue(schedule, after).Should().BeTrue();
        DigestPolicies.PeriodKey(DigestCadence.Daily, new DateTime(2026, 10, 15), null)
            .Should().Be("daily:2026-10-15");
    }

    [Fact]
    public void Weekly_period_is_stable_and_waits_for_the_weekday()
    {
        var schedule = Schedule(DigestCadence.Weekly, new TimeOnly(8, 0), weekday: (short)DayOfWeek.Thursday, "UTC");
        var wednesday = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        var thursday = new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
        DigestPolicies.IsDue(schedule, wednesday).Should().BeFalse();
        DigestPolicies.IsDue(schedule, thursday).Should().BeTrue();
        var local = thursday.UtcDateTime;
        var key = DigestPolicies.PeriodKey(DigestCadence.Weekly, local, null);
        DigestPolicies.PeriodKey(DigestCadence.Weekly, local.AddHours(5), null).Should().Be(key);
    }

    [Fact]
    public void Dnd_defers_the_next_check_without_a_new_period()
    {
        var schedule = Schedule(DigestCadence.Daily, new TimeOnly(0, 0), 1, "UTC");
        var now = new DateTimeOffset(2026, 10, 15, 15, 0, 0, TimeSpan.Zero);
        DigestPolicies.NextCheckUtc(schedule, now, deferredForDnd: true).Should().Be(now.AddMinutes(15));
        var within = PushDispatchPolicies.IsWithinDnd(true, new TimeOnly(0, 0), new TimeOnly(23, 59), 0, "UTC", now);
        within.Should().BeTrue();
        DigestPolicies.ShouldDeferExternal(DigestDelivery.Email, itemCount: 2, withinDnd: true).Should().BeTrue();
        DigestPolicies.ShouldSendExternal(DigestDelivery.Email, 2, withinDnd: true, transportEnabled: true).Should().BeFalse();
    }

    [Fact]
    public void External_notice_is_counts_only()
    {
        const string secret = "segredo-do-corpo-da-mensagem";
        var notice = DigestPolicies.ExternalNotice("pt-BR", 2, 1, 0, 1, 3);
        notice.Should().Be("2 não lidas, 1 menções, 0 anúncios, 1 decisões, 3 tarefas");
        notice.Should().NotContain(secret);
        DigestPolicies.AiPrompt(2, 1, 0, 1, 3).Should().NotContain(secret);
        DigestPolicies.ExternalNotice("en-US", 2, 1, 0, 1, 3).Should().StartWith("2 unread");
    }

    [Fact]
    public void Repeated_period_key_matches_and_short_idempotency_is_rejected()
    {
        DigestPolicies.TryNormalizeIdempotency("idem-catchup-1", out var key).Should().BeTrue();
        var first = DigestPolicies.PeriodKey(DigestCadence.OnDemand, DateTime.UtcNow, key);
        var second = DigestPolicies.PeriodKey(DigestCadence.OnDemand, DateTime.UtcNow.AddMinutes(5), key);
        first.Should().Be(second);
        DigestPolicies.TryNormalizeIdempotency("short", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Ai_failure_keeps_the_deterministic_list()
    {
        var items = new[]
        {
            new DigestSource(DigestItemKind.Unread, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 4, DateTimeOffset.UtcNow),
            new DigestSource(DigestItemKind.Decision, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, DateTimeOffset.UtcNow),
        };
        var ai = await DigestAiGate.RunAsync(_ => throw new InvalidOperationException("provider down"), enabled: true, CancellationToken.None);
        var draft = DigestPolicies.Draft(items, ai, externalDeferred: false);
        draft.Items.Should().BeEquivalentTo(items);
        draft.Status.Should().Be(DigestRunStatus.Partial);
        draft.AiStatus.Should().Be(DigestAiStatus.Failed);
        draft.Summary.Should().BeNull();

        var off = DigestPolicies.Draft(items, DigestAiOutcome.Off(), externalDeferred: false);
        off.Status.Should().Be(DigestRunStatus.Ready);
        off.AiStatus.Should().Be(DigestAiStatus.Off);
        off.Items.Should().HaveCount(2);
    }

    private static DigestSchedule Schedule(string cadence, TimeOnly localTime, short weekday, string timeZone) => new()
    {
        Enabled = true,
        Cadence = cadence,
        LocalTime = localTime,
        Weekday = weekday,
        TimeZone = timeZone,
    };

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
