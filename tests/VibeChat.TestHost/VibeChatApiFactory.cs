using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Minio;
using Minio.DataModel.Args;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using Xunit;

namespace VibeChat.TestHost;

[CollectionDefinition(Name)]
public sealed class VibeChatApiCollection : ICollectionFixture<VibeChatApiFactory>
{
    public const string Name = "VibeChatApi";
}

/// <summary>
/// Hosts the API for integration/security tests.
/// Prefers an already-running local stack (compose on localhost); otherwise starts Testcontainers.
/// </summary>
public sealed class VibeChatApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string MinioUser = "minioadmin";
    private const string MinioPassword = "minioadmin_dev_password_change_me";
    private const string MinioBucket = "vibechat";
    private const string LocalRedisEndpoint = "localhost:6379";
    private const string BootstrapUser = "vibechat";
    private const string BootstrapPassword = "vibechat_dev_password_change_me";
    private const string AppUser = "vibechat_app";
    private const string AppPassword = "vibechat_app_password_change_me";
    private const string MigratorUser = "vibechat_migrator";
    private const string MigratorPassword = "vibechat_migrator_password_change_me";
    private const string BackupUser = "vibechat_backup";
    private const string BackupPassword = "vibechat_backup_password_change_me";

    // Keep test keys off the dev keyspace (database 0) on a shared local Redis.
    private const int LocalRedisDatabase = 15;

    private PostgreSqlContainer? _postgres;
    private RedisContainer? _redis;
    private IContainer? _minio;
    private bool _ownsContainers;

    private string _bootstrapDatabase =
        $"Host=localhost;Port=5432;Database=vibechat_test;Username={BootstrapUser};Password={BootstrapPassword}";
    private string _migratorDatabase =
        $"Host=localhost;Port=5432;Database=vibechat_test;Username={MigratorUser};Password={MigratorPassword}";
    private string _runtimeDatabase =
        $"Host=localhost;Port=5432;Database=vibechat_test;Username={AppUser};Password={AppPassword}";
    private string _redisConnection = $"{LocalRedisEndpoint},defaultDatabase={LocalRedisDatabase}";
    private string _minioEndpoint = "localhost:9000";

    public string RuntimeDatabaseConnectionString => _runtimeDatabase;
    public string MigratorDatabaseConnectionString => _migratorDatabase;
    public string BootstrapDatabaseConnectionString => _bootstrapDatabase;

    /// <summary>
    /// Migrator/BYPASSRLS context for test seeding and DB assertions only.
    /// Request path under test still uses the app runtime role.
    /// </summary>
    public VibeChatDbContext CreateMigratorDbContext()
    {
        var options = new DbContextOptionsBuilder<VibeChatDbContext>()
            .UseNpgsql(_migratorDatabase)
            .Options;
        return new VibeChatDbContext(options, new TenantContext());
    }

    public async Task InitializeAsync()
    {
        if (await IsPortOpenAsync("127.0.0.1", 5432) &&
            await IsPortOpenAsync("127.0.0.1", 6379) &&
            await IsPortOpenAsync("127.0.0.1", 9000))
        {
            await ResetLocalTestDatabaseAsync();
            await ResetLocalTestRedisAsync();
            await EnsureBucketAsync(_minioEndpoint);
            return;
        }

        _ownsContainers = true;
        _postgres = new PostgreSqlBuilder("postgres:16.6")
            .WithDatabase("vibechat_test")
            .WithUsername(BootstrapUser)
            .WithPassword(BootstrapPassword)
            .Build();

        _redis = new RedisBuilder("redis:7.4-alpine").Build();

        _minio = new ContainerBuilder("minio/minio:RELEASE.2024-12-18T13-15-44Z")
            .WithEnvironment("MINIO_ROOT_USER", MinioUser)
            .WithEnvironment("MINIO_ROOT_PASSWORD", MinioPassword)
            .WithCommand("server", "/data", "--address", ":9000")
            .WithPortBinding(9000, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r =>
                r.ForPath("/minio/health/live").ForPort(9000)))
            .Build();

        await Task.WhenAll(
            _postgres.StartAsync(),
            _redis.StartAsync(),
            _minio.StartAsync());

        _bootstrapDatabase = _postgres.GetConnectionString();
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(_bootstrapDatabase)
        {
            Database = "vibechat_test"
        };
        _bootstrapDatabase = builder.ConnectionString;
        builder.Username = MigratorUser;
        builder.Password = MigratorPassword;
        _migratorDatabase = builder.ConnectionString;
        builder.Username = AppUser;
        builder.Password = AppPassword;
        _runtimeDatabase = builder.ConnectionString;

        _redisConnection = _redis.GetConnectionString();
        // Prefer loopback — Hostname can be unreachable depending on Docker networking.
        _minioEndpoint = $"127.0.0.1:{_minio.GetMappedPublicPort(9000)}";
        await EnsureBucketAsync(_minioEndpoint);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        if (!_ownsContainers)
        {
            return;
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }

        if (_redis is not null)
        {
            await _redis.DisposeAsync();
        }

        if (_minio is not null)
        {
            await _minio.DisposeAsync();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Database", _runtimeDatabase);
        builder.UseSetting("ConnectionStrings:DatabaseMigrator", _migratorDatabase);
        builder.UseSetting("ConnectionStrings:DatabaseBootstrap", _bootstrapDatabase);
        builder.UseSetting("POSTGRES_APP_USER", AppUser);
        builder.UseSetting("POSTGRES_APP_PASSWORD", AppPassword);
        builder.UseSetting("POSTGRES_MIGRATOR_USER", MigratorUser);
        builder.UseSetting("POSTGRES_MIGRATOR_PASSWORD", MigratorPassword);
        builder.UseSetting("POSTGRES_BACKUP_USER", BackupUser);
        builder.UseSetting("POSTGRES_BACKUP_PASSWORD", BackupPassword);
        builder.UseSetting("ConnectionStrings:Redis", _redisConnection);
        // Dual-host on purpose (BUG-003): internal ops via 127.0.0.1, browser URLs via
        // localhost. Rewriting a URL signed for one host to the other breaks SigV4.
        var minioPort = _minioEndpoint.Contains(':', StringComparison.Ordinal)
            ? _minioEndpoint[(_minioEndpoint.LastIndexOf(':') + 1)..]
            : "9000";
        builder.UseSetting("Minio:Endpoint", $"127.0.0.1:{minioPort}");
        builder.UseSetting("Minio:PublicEndpoint", $"http://localhost:{minioPort}");
        builder.UseSetting("Minio:AccessKey", MinioUser);
        builder.UseSetting("Minio:SecretKey", MinioPassword);
        builder.UseSetting("Minio:Bucket", MinioBucket);
        builder.UseSetting("Minio:UseSsl", "false");
        builder.UseSetting("Files:MaxSizeBytes", "10485760");
        builder.UseSetting("Seed:Enabled", "true");
        builder.UseSetting("Database:BootstrapOnStartup", "true");
        builder.UseSetting("Ai:Enabled", "true");
        builder.UseSetting("Ai:Provider", "Mock");
        // Fake secrets for B-069 mask assertions — never real credentials.
        builder.UseSetting("Ai:OpenRouter:ApiKey", "sk-test-secret-key99");
        builder.UseSetting("Email:Enabled", "false");
        builder.UseSetting("Email:Smtp:Password", "smtp-test-password42");
        // ADR-020: test keyring (32 zero bytes) + overrides on for rotate/reencrypt coverage.
        builder.UseSetting("RuntimeSettings:DatabaseOverridesEnabled", "true");
        builder.UseSetting("RuntimeSettings:Encryption:ActiveKeyVersion", "1");
        builder.UseSetting(
            "RuntimeSettings:Encryption:Keys:1",
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");
        // B-047: process kill switch on for purge processor tests; tenant policy still opt-in.
        builder.UseSetting("MessageRetention:Enabled", "true");
        builder.UseSetting("MessageRetention:DefaultRetentionDays", "90");
        builder.UseSetting("MessageRetention:BatchSize", "500");
        builder.UseSetting("Authentication:RequireHttpsMetadata", "false");
    }

    // Testcontainers give every run a clean stack; a reused local stack does not, so
    // drop the test database instead of keeping whatever the previous run wrote.
    private async Task ResetLocalTestDatabaseAsync()
    {
        await using var conn = new Npgsql.NpgsqlConnection(
            $"Host=localhost;Port=5432;Database=postgres;Username={BootstrapUser};Password={BootstrapPassword}");
        await conn.OpenAsync();

        await using (var drop = new Npgsql.NpgsqlCommand(
            "DROP DATABASE IF EXISTS vibechat_test WITH (FORCE)", conn))
        {
            await drop.ExecuteNonQueryAsync();
        }

        await using var create = new Npgsql.NpgsqlCommand($"CREATE DATABASE vibechat_test OWNER {BootstrapUser}", conn);
        await create.ExecuteNonQueryAsync();

        _bootstrapDatabase =
            $"Host=localhost;Port=5432;Database=vibechat_test;Username={BootstrapUser};Password={BootstrapPassword}";
        _migratorDatabase =
            $"Host=localhost;Port=5432;Database=vibechat_test;Username={MigratorUser};Password={MigratorPassword}";
        _runtimeDatabase =
            $"Host=localhost;Port=5432;Database=vibechat_test;Username={AppUser};Password={AppPassword}";
    }

    private static async Task ResetLocalTestRedisAsync()
    {
        using var connection = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(
            $"{LocalRedisEndpoint},allowAdmin=true");

        foreach (var endpoint in connection.GetEndPoints())
        {
            await connection.GetServer(endpoint).FlushDatabaseAsync(LocalRedisDatabase);
        }
    }

    private static async Task EnsureBucketAsync(string endpoint)
    {
        var parts = endpoint.Split(':', 2);
        var host = parts[0];
        var port = parts.Length > 1 && int.TryParse(parts[1], out var parsed) ? parsed : 9000;

        Exception? last = null;
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                var client = new MinioClient()
                    .WithEndpoint(host, port)
                    .WithCredentials(MinioUser, MinioPassword)
                    .WithSSL(false)
                    .Build();

                var exists = await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(MinioBucket));
                if (!exists)
                {
                    await client.MakeBucketAsync(new MakeBucketArgs().WithBucket(MinioBucket));
                }

                return;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(500 * attempt);
            }
        }

        throw new InvalidOperationException($"MinIO bucket setup failed at {endpoint}", last);
    }

    private static async Task<bool> IsPortOpenAsync(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
