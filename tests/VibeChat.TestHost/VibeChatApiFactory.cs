using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Minio;
using Minio.DataModel.Args;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace VibeChat.TestHost;

/// <summary>
/// Hosts the API for integration/security tests.
/// Prefers an already-running local stack (compose on localhost); otherwise starts Testcontainers.
/// </summary>
public sealed class VibeChatApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string MinioUser = "minioadmin";
    private const string MinioPassword = "minioadmin_dev_password_change_me";
    private const string MinioBucket = "vibechat";

    private PostgreSqlContainer? _postgres;
    private RedisContainer? _redis;
    private IContainer? _minio;
    private bool _ownsContainers;

    private string _database =
        "Host=localhost;Port=5432;Database=vibechat_test;Username=vibechat;Password=vibechat_dev_password_change_me";
    private string _redisConnection = "localhost:6379";
    private string _minioEndpoint = "localhost:9000";

    public async Task InitializeAsync()
    {
        if (await IsPortOpenAsync("127.0.0.1", 5432) &&
            await IsPortOpenAsync("127.0.0.1", 6379) &&
            await IsPortOpenAsync("127.0.0.1", 9000))
        {
            await EnsureLocalTestDatabaseAsync();
            await EnsureBucketAsync(_minioEndpoint);
            return;
        }

        _ownsContainers = true;
        _postgres = new PostgreSqlBuilder("postgres:16.6")
            .WithDatabase("vibechat_test")
            .WithUsername("vibechat")
            .WithPassword("vibechat_dev_password_change_me")
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

        _database = _postgres.GetConnectionString();
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
        builder.UseSetting("ConnectionStrings:Database", _database);
        builder.UseSetting("ConnectionStrings:Redis", _redisConnection);
        builder.UseSetting("Minio:Endpoint", _minioEndpoint);
        builder.UseSetting("Minio:AccessKey", MinioUser);
        builder.UseSetting("Minio:SecretKey", MinioPassword);
        builder.UseSetting("Minio:Bucket", MinioBucket);
        builder.UseSetting("Minio:UseSsl", "false");
        builder.UseSetting("Seed:Enabled", "true");
        builder.UseSetting("Ai:Enabled", "true");
        builder.UseSetting("Ai:Provider", "Mock");
        builder.UseSetting("Authentication:RequireHttpsMetadata", "false");
    }

    private static async Task EnsureLocalTestDatabaseAsync()
    {
        await using var conn = new Npgsql.NpgsqlConnection(
            "Host=localhost;Port=5432;Database=postgres;Username=vibechat;Password=vibechat_dev_password_change_me");
        await conn.OpenAsync();
        await using var check = new Npgsql.NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = 'vibechat_test'", conn);
        var exists = await check.ExecuteScalarAsync();
        if (exists is null)
        {
            await using var create = new Npgsql.NpgsqlCommand("CREATE DATABASE vibechat_test OWNER vibechat", conn);
            await create.ExecuteNonQueryAsync();
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

                // Confirm again — health check uses BucketExists.
                if (await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(MinioBucket)))
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                last = ex;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt));
        }

        throw new InvalidOperationException($"Unable to ensure MinIO bucket '{MinioBucket}' at {endpoint}", last);
    }

    private static async Task<bool> IsPortOpenAsync(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await client.ConnectAsync(host, port, cts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }
}

[CollectionDefinition(Name)]
public sealed class VibeChatApiCollection : ICollectionFixture<VibeChatApiFactory>
{
    public const string Name = "vibechat-api";
}
