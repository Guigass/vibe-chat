using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace VibeChat.Infrastructure;

public enum MigrationPhase
{
    Expand,
    Migrate,
    Contract
}

/// <summary>
/// B-144 / ADR-062. Off by default so Standard stays a single node.
/// </summary>
public sealed class HaOptions
{
    public const string SectionName = "Ha";

    public bool Enabled { get; set; }

    public int MaxConnectionsPerNode { get; set; } = 2000;

    public int SlowConsumerBuffer { get; set; } = 32;

    public int DrainDeadlineSeconds { get; set; } = 10;

    public int RpoSeconds { get; set; } = 300;

    public int RtoSeconds { get; set; } = 1800;

    public int SchemaRevision { get; set; } = 1;

    public int CompatibilityWindow { get; set; } = 1;

    public string Phase { get; set; } = "expand";

    public bool MeetsProfile =>
        RpoSeconds > 0 && RpoSeconds <= 300 && RtoSeconds > 0 && RtoSeconds <= 1800;
}

public static class SchemaCompatibility
{
    public static bool TryParsePhase(string? value, out MigrationPhase phase)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "expand":
                phase = MigrationPhase.Expand;
                return true;
            case "migrate":
                phase = MigrationPhase.Migrate;
                return true;
            case "contract":
                phase = MigrationPhase.Contract;
                return true;
            default:
                phase = MigrationPhase.Expand;
                return false;
        }
    }

    public static bool RollbackAllowed(MigrationPhase phase) =>
        phase is MigrationPhase.Expand or MigrationPhase.Migrate;

    public static bool CanRunTogether(int left, int right, MigrationPhase phase, int window)
    {
        if (left == right)
        {
            return true;
        }

        if (phase == MigrationPhase.Contract || window < 1)
        {
            return false;
        }

        return Math.Abs(left - right) <= window;
    }
}

public static class HaSignalR
{
    public static bool WebSocketsOnly(bool haEnabled) => haEnabled;

    public static bool StickySessionsRequired(bool haEnabled) =>
        haEnabled && !WebSocketsOnly(haEnabled);
}

public sealed class HaJobGate(IOptions<HaOptions> options)
{
    private int _leader = options.Value.Enabled ? 0 : 1;
    private long _lastTakeoverMs = -1;

    public bool IsLeader => Volatile.Read(ref _leader) == 1;

    public long? LastTakeoverMs
    {
        get
        {
            var value = Volatile.Read(ref _lastTakeoverMs);
            return value < 0 ? null : value;
        }
    }

    public async Task<bool> WaitTurnAsync(CancellationToken cancellationToken)
    {
        if (IsLeader)
        {
            return true;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
        return false;
    }

    internal void SetLeader(bool leader) => Volatile.Write(ref _leader, leader ? 1 : 0);

    internal void RecordTakeover(long elapsedMs) =>
        Volatile.Write(ref _lastTakeoverMs, Math.Max(0, elapsedMs));
}

public sealed class HaAdmission(IOptions<HaOptions> options)
{
    private readonly bool _enabled = options.Value.Enabled;
    private readonly int _max = Math.Max(1, options.Value.MaxConnectionsPerNode);
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private int _draining;
    private int _inFlight;

    public bool Enabled => _enabled;

    public bool TracksRequests => _enabled;

    public bool Draining => Volatile.Read(ref _draining) == 1;

    public int InFlight => Volatile.Read(ref _inFlight);

    public string NodeId { get; } = Guid.NewGuid().ToString("n");

    public bool TryAccept(bool hasPrincipal, string connectionId)
    {
        if (!_enabled)
        {
            return true;
        }

        if (!hasPrincipal || Draining || string.IsNullOrEmpty(connectionId))
        {
            return false;
        }

        lock (_gate)
        {
            if (_ids.Count >= _max)
            {
                return false;
            }

            return _ids.Add(connectionId);
        }
    }

    public void Release(string connectionId)
    {
        if (!_enabled || string.IsNullOrEmpty(connectionId))
        {
            return;
        }

        lock (_gate)
        {
            _ids.Remove(connectionId);
        }
    }

    public IReadOnlyCollection<string> ConnectionIds
    {
        get
        {
            lock (_gate)
            {
                return _ids.ToArray();
            }
        }
    }

    public void BeginDrain() => Volatile.Write(ref _draining, 1);

    public void Enter() => Interlocked.Increment(ref _inFlight);

    public void Exit() => Interlocked.Decrement(ref _inFlight);
}

public sealed class HaBackpressure(IOptions<HaOptions> options)
{
    private readonly bool _enabled = options.Value.Enabled;
    private readonly int _limit = Math.Max(1, options.Value.SlowConsumerBuffer);
    private int _depth;

    public int Depth => Volatile.Read(ref _depth);

    public bool TryEnter(string eventName, out Action release, out bool resync)
    {
        release = static () => { };
        resync = false;
        if (!_enabled)
        {
            return true;
        }

        var depth = Interlocked.Increment(ref _depth);
        if (depth <= _limit)
        {
            release = Release;
            return true;
        }

        Interlocked.Decrement(ref _depth);
        resync = !IsEphemeral(eventName);
        VibeChatMetrics.HaBackpressureDrops.Add(1);
        return false;
    }

    private void Release() => Interlocked.Decrement(ref _depth);

    public static bool IsEphemeral(string eventName) =>
        eventName is "Typing" or "PresenceChanged";
}

public sealed record HaRuntimeDocument(
    int SchemaRevision,
    int CompatibilityWindow,
    string Phase,
    bool Draining,
    bool StickySessions,
    bool WebSocketsOnly,
    int RpoSeconds,
    int RtoSeconds,
    string AvailabilityObjective,
    bool MeetsProfile,
    long? LastTakeoverMs);

public static class HaRuntime
{
    public static HaRuntimeDocument Describe(HaOptions options, HaAdmission admission, HaJobGate jobs)
    {
        SchemaCompatibility.TryParsePhase(options.Phase, out var phase);
        return new HaRuntimeDocument(
            options.SchemaRevision,
            options.CompatibilityWindow,
            phase.ToString().ToLowerInvariant(),
            admission.Draining,
            HaSignalR.StickySessionsRequired(options.Enabled),
            HaSignalR.WebSocketsOnly(options.Enabled),
            options.RpoSeconds,
            options.RtoSeconds,
            "99.95%",
            options.MeetsProfile,
            jobs.LastTakeoverMs);
    }
}

/// <summary>
/// Session advisory lock. A second holder gets null until the first connection closes.
/// </summary>
public sealed class HaLeaseSession : IAsyncDisposable
{
    public const int LockClass = 144_001;
    public const int LockId = 1;

    private readonly NpgsqlConnection _connection;
    private int _released;

    private HaLeaseSession(NpgsqlConnection connection) => _connection = connection;

    public static async Task<HaLeaseSession?> TryAcquireAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@class, @id)";
        command.Parameters.AddWithValue("class", LockClass);
        command.Parameters.AddWithValue("id", LockId);
        var acquired = await command.ExecuteScalarAsync(cancellationToken) is true;
        if (!acquired)
        {
            await connection.DisposeAsync();
            return null;
        }

        return new HaLeaseSession(connection);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "SELECT pg_advisory_unlock(@class, @id)";
            command.Parameters.AddWithValue("class", LockClass);
            command.Parameters.AddWithValue("id", LockId);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            await _connection.DisposeAsync();
        }
    }
}

public sealed class HaLeaseHost(
    IOptions<HaOptions> options,
    IConfiguration configuration,
    HaJobGate jobs,
    ILogger<HaLeaseHost> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Standard does not take the lease.
            }

            return;
        }

        var sawMiss = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                sawMiss = await HoldAsync(sawMiss, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                jobs.SetLeader(false);
                sawMiss = true;
                logger.LogWarning(ex, "HA job lease failed; this node is a follower");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }

        jobs.SetLeader(false);
    }

    private async Task<bool> HoldAsync(bool sawMiss, CancellationToken cancellationToken)
    {
        var connectionString = DatabaseBootstrap.ResolveRuntimeConnectionString(configuration);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            jobs.SetLeader(false);
            logger.LogWarning("HA job lease has no runtime connection string; this node stays a follower");
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            return true;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            var started = Stopwatch.GetTimestamp();
            var acquired = await TryLockAsync(connection, cancellationToken);
            if (!acquired)
            {
                jobs.SetLeader(false);
                sawMiss = true;
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                continue;
            }

            if (sawMiss)
            {
                jobs.RecordTakeover((long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }

            jobs.SetLeader(true);
            logger.LogInformation("HA job lease acquired");
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                    await using var ping = connection.CreateCommand();
                    ping.CommandText = "SELECT 1";
                    await ping.ExecuteScalarAsync(cancellationToken);
                }
            }
            finally
            {
                jobs.SetLeader(false);
                await UnlockAsync(connection);
            }

            return sawMiss;
        }

        return sawMiss;
    }

    private static async Task<bool> TryLockAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@class, @id)";
        command.Parameters.AddWithValue("class", HaLeaseSession.LockClass);
        command.Parameters.AddWithValue("id", HaLeaseSession.LockId);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task UnlockAsync(NpgsqlConnection connection)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_advisory_unlock(@class, @id)";
            command.Parameters.AddWithValue("class", HaLeaseSession.LockClass);
            command.Parameters.AddWithValue("id", HaLeaseSession.LockId);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception)
        {
            // The connection is closing. Session locks die with it.
        }
    }
}

public static class HaCoordination
{
    public static IServiceCollection AddHaCoordination(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<HaOptions>().Bind(configuration.GetSection(HaOptions.SectionName));
        services.AddSingleton<HaJobGate>();
        services.AddSingleton<HaAdmission>();
        services.AddSingleton<HaBackpressure>();
        services.AddHostedService<HaLeaseHost>();
        return services;
    }
}
