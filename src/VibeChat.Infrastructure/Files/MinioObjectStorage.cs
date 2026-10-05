using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Minio;
using StackExchange.Redis;
using VibeChat.Administration;
using VibeChat.AI;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Directory;
using VibeChat.Files;
using VibeChat.Identity;
using VibeChat.Integrations;
using VibeChat.Messaging;
using VibeChat.Notifications;
using VibeChat.Realtime;
using NpgsqlTypes;
using VibeChat.Search;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using Role = VibeChat.SharedKernel.Role;

namespace VibeChat.Infrastructure;

/// <summary>
/// MinIO client configured with <c>Minio:PublicEndpoint</c> for browser-facing
/// presigned URLs. Must sign the public host — rewriting after signing breaks SigV4.
/// </summary>
public sealed class MinioPresignClient(IMinioClient Client)
{
    public IMinioClient Client { get; } = Client;
}

public sealed class MinioObjectStorage(
    IMinioClient minioClient,
    MinioPresignClient presignClient,
    IConfiguration configuration) : IObjectStorage
{
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        var bucket = Bucket();
        try
        {
            return await minioClient.BucketExistsAsync(new Minio.DataModel.Args.BucketExistsArgs().WithBucket(bucket), cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    public async Task<PresignedUpload> CreateUploadUrlAsync(string storageKey, string contentType, TimeSpan ttl, CancellationToken cancellationToken)
    {
        _ = contentType;
        var expiry = Math.Clamp((int)ttl.TotalSeconds, 60, 3600);
        // Sign with the public host so browser PUTs match SigV4 (BUG-003).
        var url = await presignClient.Client.PresignedPutObjectAsync(new Minio.DataModel.Args.PresignedPutObjectArgs()
            .WithBucket(Bucket())
            .WithObject(storageKey)
            .WithExpiry(expiry));
        // Keep required headers empty — signed PUT URLs reject unsigned Content-Type headers.
        return new PresignedUpload(
            new Uri(url),
            DateTimeOffset.UtcNow.AddSeconds(expiry),
            new Dictionary<string, string>());
    }

    public async Task<PresignedDownload> CreateDownloadUrlAsync(string storageKey, string fileName, TimeSpan ttl, CancellationToken cancellationToken)
    {
        _ = fileName;
        var expiry = Math.Clamp((int)ttl.TotalSeconds, 60, 3600);
        var url = await presignClient.Client.PresignedGetObjectAsync(new Minio.DataModel.Args.PresignedGetObjectArgs()
            .WithBucket(Bucket())
            .WithObject(storageKey)
            .WithExpiry(expiry));
        return new PresignedDownload(new Uri(url), DateTimeOffset.UtcNow.AddSeconds(expiry));
    }

    public async Task<ObjectStat?> StatObjectAsync(string storageKey, CancellationToken cancellationToken)
    {
        try
        {
            var stat = await minioClient.StatObjectAsync(new Minio.DataModel.Args.StatObjectArgs()
                .WithBucket(Bucket())
                .WithObject(storageKey), cancellationToken);
            return new ObjectStat(stat.Size, stat.ContentType ?? "application/octet-stream", stat.ETag);
        }
        catch (Minio.Exceptions.ObjectNotFoundException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task DeleteObjectAsync(string storageKey, CancellationToken cancellationToken)
    {
        try
        {
            await minioClient.RemoveObjectAsync(new Minio.DataModel.Args.RemoveObjectArgs()
                .WithBucket(Bucket())
                .WithObject(storageKey), cancellationToken);
        }
        catch (Minio.Exceptions.ObjectNotFoundException)
        {
            // Idempotent: already gone.
        }
    }

    public async Task<Stream?> GetObjectAsync(string storageKey, CancellationToken cancellationToken)
    {
        try
        {
            var memory = new MemoryStream();
            await minioClient.GetObjectAsync(new Minio.DataModel.Args.GetObjectArgs()
                .WithBucket(Bucket())
                .WithObject(storageKey)
                .WithCallbackStream(stream => stream.CopyTo(memory)), cancellationToken);
            memory.Position = 0;
            return memory;
        }
        catch (Minio.Exceptions.ObjectNotFoundException)
        {
            return null;
        }
    }

    public async Task PutObjectAsync(string storageKey, Stream content, string contentType, CancellationToken cancellationToken)
    {
        if (content.CanSeek)
        {
            content.Position = 0;
        }

        var size = content.CanSeek ? content.Length : -1;
        var putArgs = new Minio.DataModel.Args.PutObjectArgs()
            .WithBucket(Bucket())
            .WithObject(storageKey)
            .WithStreamData(content)
            .WithObjectSize(size)
            .WithContentType(contentType);
        await minioClient.PutObjectAsync(putArgs, cancellationToken);
    }

    private string Bucket() => configuration["Minio:Bucket"] ?? "vibechat";
}

internal static class MinioEndpoint
{
    public static IMinioClient CreateClient(IConfiguration cfg, string endpoint)
    {
        var (host, port, useSsl) = Parse(endpoint, cfg);
        return new MinioClient()
            .WithEndpoint(host, port)
            .WithCredentials(
                cfg["Minio:AccessKey"] ?? "minioadmin",
                cfg["Minio:SecretKey"] ?? "minioadmin_dev_password_change_me")
            .WithSSL(useSsl)
            .Build();
    }

    public static string ResolveInternalEndpoint(IConfiguration cfg) =>
        cfg["Minio:Endpoint"] ?? "localhost:9000";

    public static string ResolvePublicEndpoint(IConfiguration cfg)
    {
        var publicEndpoint = cfg["Minio:PublicEndpoint"];
        return string.IsNullOrWhiteSpace(publicEndpoint)
            ? ResolveInternalEndpoint(cfg)
            : publicEndpoint;
    }

    private static (string Host, int Port, bool UseSsl) Parse(string endpoint, IConfiguration cfg)
    {
        var useSsl = bool.TryParse(cfg["Minio:UseSsl"], out var configuredSsl) && configuredSsl;
        var trimmed = endpoint.Trim();
        if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            useSsl = true;
            trimmed = trimmed["https://".Length..];
        }
        else if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            useSsl = false;
            trimmed = trimmed["http://".Length..];
        }

        var parts = trimmed.Split(':', 2);
        var host = parts[0];
        var port = parts.Length > 1 && int.TryParse(parts[1], out var parsedPort)
            ? parsedPort
            : useSsl ? 443 : 9000;
        return (host, port, useSsl);
    }
}
