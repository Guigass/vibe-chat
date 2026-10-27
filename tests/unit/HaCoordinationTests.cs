using FluentAssertions;
using Microsoft.Extensions.Options;
using VibeChat.Infrastructure;

namespace VibeChat.UnitTests;

public sealed class HaCoordinationTests
{
    [Fact]
    public void Defaults_meet_the_ha_profile_and_keep_the_flag_off()
    {
        var options = new HaOptions();

        options.Enabled.Should().BeFalse();
        options.RpoSeconds.Should().BeLessThanOrEqualTo(300);
        options.RtoSeconds.Should().BeLessThanOrEqualTo(1800);
        options.MeetsProfile.Should().BeTrue();
        HaSignalR.WebSocketsOnly(false).Should().BeFalse();
        HaSignalR.WebSocketsOnly(true).Should().BeTrue();
        HaSignalR.StickySessionsRequired(true).Should().BeFalse();
        HaSignalR.StickySessionsRequired(false).Should().BeFalse();
    }

    [Theory]
    [InlineData(MigrationPhase.Expand, true)]
    [InlineData(MigrationPhase.Migrate, true)]
    [InlineData(MigrationPhase.Contract, false)]
    public void Rollback_is_allowed_until_the_contract_phase(MigrationPhase phase, bool allowed)
    {
        SchemaCompatibility.RollbackAllowed(phase).Should().Be(allowed);
    }

    [Fact]
    public void Adjacent_versions_share_expand_and_contract_rejects_a_mix()
    {
        SchemaCompatibility.CanRunTogether(1, 2, MigrationPhase.Expand, 1).Should().BeTrue();
        SchemaCompatibility.CanRunTogether(1, 2, MigrationPhase.Migrate, 1).Should().BeTrue();
        SchemaCompatibility.CanRunTogether(1, 3, MigrationPhase.Expand, 1).Should().BeFalse();
        SchemaCompatibility.CanRunTogether(1, 2, MigrationPhase.Contract, 1).Should().BeFalse();
        SchemaCompatibility.CanRunTogether(4, 4, MigrationPhase.Contract, 1).Should().BeTrue();
        SchemaCompatibility.TryParsePhase("contract", out var phase).Should().BeTrue();
        phase.Should().Be(MigrationPhase.Contract);
    }

    [Fact]
    public void Disabled_gate_is_leader_and_enabled_gate_waits()
    {
        var off = new HaJobGate(Options.Create(new HaOptions()));
        var on = new HaJobGate(Options.Create(new HaOptions { Enabled = true }));

        off.IsLeader.Should().BeTrue();
        on.IsLeader.Should().BeFalse();
    }

    [Fact]
    public void Admission_rejects_drain_anonymous_and_overflow_only_when_enabled()
    {
        var off = new HaAdmission(Options.Create(new HaOptions()));
        off.TryAccept(false, "c1").Should().BeTrue();

        var on = new HaAdmission(Options.Create(new HaOptions
        {
            Enabled = true,
            MaxConnectionsPerNode = 1
        }));
        on.TryAccept(false, "anon").Should().BeFalse();
        on.TryAccept(true, "c1").Should().BeTrue();
        on.TryAccept(true, "c2").Should().BeFalse();
        on.BeginDrain();
        on.Release("c1");
        on.TryAccept(true, "c3").Should().BeFalse();
        on.Draining.Should().BeTrue();
    }

    [Fact]
    public void Slow_consumer_does_not_grow_the_buffer_without_bound()
    {
        var pressure = new HaBackpressure(Options.Create(new HaOptions
        {
            Enabled = true,
            SlowConsumerBuffer = 2
        }));

        pressure.TryEnter("MessageCreated", out var first, out var firstResync).Should().BeTrue();
        firstResync.Should().BeFalse();
        pressure.TryEnter("MessageCreated", out var second, out _).Should().BeTrue();
        pressure.TryEnter("MessageCreated", out _, out var durableResync).Should().BeFalse();
        durableResync.Should().BeTrue();
        pressure.TryEnter("Typing", out _, out var typingResync).Should().BeFalse();
        typingResync.Should().BeFalse();
        pressure.Depth.Should().Be(2);

        first();
        second();
        pressure.Depth.Should().Be(0);
        pressure.TryEnter("MessageCreated", out var third, out _).Should().BeTrue();
        third();
        pressure.Depth.Should().Be(0);
    }

    [Fact]
    public void Disabled_backpressure_does_not_drop()
    {
        var pressure = new HaBackpressure(Options.Create(new HaOptions()));
        for (var i = 0; i < 100; i++)
        {
            pressure.TryEnter("MessageCreated", out _, out var resync).Should().BeTrue();
            resync.Should().BeFalse();
        }

        pressure.Depth.Should().Be(0);
    }
}
