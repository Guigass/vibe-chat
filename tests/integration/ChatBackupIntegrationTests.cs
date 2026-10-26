using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Administration;
using VibeChat.Files;
using VibeChat.Infrastructure;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-172 — chat backup packages, schedules and destinations.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChatBackupIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Flag_off_hides_chat_backup()
    {
        using var client = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Features:ChatBackup:Enabled", "false")).CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "demo");

        var response = await client.GetAsync(ListPath());
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain(ChatBackupErrors.Disabled);
    }

    [Fact]
    public async Task Secrets_stay_masked_and_rotate_without_redeploy()
    {
        using var client = Client("demo");
        var created = await SendAsync(client, DestinationsPath(), new
        {
            name = "masked",
            kind = "s3",
            host = "storage.internal",
            port = 9000,
            remotePath = "backups",
            username = "backup",
            bucket = "vibechat",
            useSsl = false,
            secret = "super-secret-value"
        }, "mask-" + Guid.NewGuid().ToString("N"));
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await created.Content.ReadAsStringAsync();
        body.Should().NotContain("super-secret-value").And.Contain("••••alue");

        var id = JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
        var rotated = await SendAsync(client, $"{DestinationsPath()}/{id}/rotate", new { secret = "rotated-secret-99" }, "rot-" + Guid.NewGuid().ToString("N"));
        var rotatedBody = await rotated.Content.ReadAsStringAsync();
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        rotatedBody.Should().NotContain("rotated-secret-99").And.Contain("••••t-99");
    }

    [Fact]
    public async Task Export_import_round_trip_keeps_message_and_attachment_checksums()
    {
        using var client = Client("demo");
        var messageId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var bytes = "hello-backup"u8.ToArray();
        var checksum = ChatBackupRules.Sha256Hex(bytes);
        var payload = new Dictionary<string, byte[]>
        {
            [ChatBackupPackage.MessagesEntry] = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new[]
            {
                new ChatBackupMessageEntry(messageId, SeedData.DemoChannelId.Value, 1, SeedData.DemoUserId.Value, "hello-backup", false, null, null, DateTimeOffset.UtcNow, null, null, null)
            }, ChatBackupPackage.Json),
            [ChatBackupPackage.AttachmentsEntry] = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new[]
            {
                new ChatBackupAttachmentEntry(attachmentId, messageId, SeedData.DemoChannelId.Value, "note.txt", "text/plain", bytes.Length, checksum, DateTimeOffset.UtcNow)
            }, ChatBackupPackage.Json),
            [ChatBackupPackage.ObjectName(attachmentId)] = bytes
        };
        var zip = ChatBackupPackage.Compose(Guid.NewGuid(), WorkspaceId, DateTimeOffset.UtcNow, true, true, payload);

        var validate = await PostZipAsync(client, $"{Root()}/import/validate", zip, null);
        validate.StatusCode.Should().Be(HttpStatusCode.OK);
        var plan = await validate.Content.ReadFromJsonAsync<JsonElement>();
        plan.GetProperty("insert").GetInt32().Should().Be(1);

        var tampered = new MemoryStream();
        tampered.Write(zip);
        tampered.Position = 0;
        using (var archive = new System.IO.Compression.ZipArchive(tampered, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry(ChatBackupPackage.MessagesEntry)!;
            entry.Delete();
            var replaced = archive.CreateEntry(ChatBackupPackage.MessagesEntry);
            using var stream = replaced.Open();
            stream.Write("{}"u8);
        }

        var rejected = await PostZipAsync(client, $"{Root()}/import/validate", tampered.ToArray(), null);
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var unconfirmed = await PostZipAsync(client, $"{Root()}/import/execute", zip, "imp-" + Guid.NewGuid().ToString("N"));
        (await unconfirmed.Content.ReadAsStringAsync()).Should().Contain(ChatBackupErrors.ConfirmationRequired);

        var applied = await PostZipAsync(client, $"{Root()}/import/execute?confirm=true", zip, "imp-" + Guid.NewGuid().ToString("N"));
        applied.StatusCode.Should().Be(HttpStatusCode.OK);
        (await applied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("inserted").GetInt32().Should().Be(1);

        var again = await PostZipAsync(client, $"{Root()}/import/execute?confirm=true", zip, "imp-" + Guid.NewGuid().ToString("N"));
        (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("inserted").GetInt32().Should().Be(0);

        await using (var db = factory.CreateMigratorDbContext())
        {
            var row = await db.Attachments.IgnoreQueryFilters().SingleAsync(x => x.Id == attachmentId);
            row.ChecksumSha256.Should().Be(checksum);
            row.TenantId.Should().Be(SeedData.DemoTenantId);
        }

        var metadata = await client.GetAsync($"{Root()}/export?includeAttachments=false");
        metadata.StatusCode.Should().Be(HttpStatusCode.OK);
        var withoutFiles = ChatBackupPackage.Read(await metadata.Content.ReadAsByteArrayAsync());
        withoutFiles.Ok.Should().BeTrue();
        withoutFiles.IncludeAttachments.Should().BeFalse();
        withoutFiles.Messages.Should().Contain(x => x.Id == messageId && x.Body == "hello-backup");
        withoutFiles.Objects.Should().BeEmpty();

        var full = await client.GetAsync($"{Root()}/export?includeAttachments=true");
        var withFiles = ChatBackupPackage.Read(await full.Content.ReadAsByteArrayAsync());
        withFiles.Ok.Should().BeTrue();
        withFiles.IncludeAttachments.Should().BeTrue();
        withFiles.Objects[ChatBackupPackage.ObjectName(attachmentId)].Should().Equal(bytes);
    }

    [Fact]
    public async Task Scheduled_job_writes_s3_and_sftp_with_checksum()
    {
        var recorder = new RecordingSftp();
        ChatBackupTestDoubles.Sftp = recorder;
        try
        {
            using var client = Client("demo");
            var config = factory.Services.GetRequiredService<IConfiguration>();
            var endpoint = config["Minio:Endpoint"] ?? "127.0.0.1:9000";
            var parts = endpoint.Split(':');
            var port = int.Parse(parts[^1], System.Globalization.CultureInfo.InvariantCulture);
            var s3 = await CreateDestinationAsync(client, new
            {
                name = "lab-s3",
                kind = "s3",
                host = parts[0],
                port,
                remotePath = "chat-backup",
                username = config["Minio:AccessKey"],
                bucket = config["Minio:Bucket"],
                useSsl = false,
                secret = config["Minio:SecretKey"]
            });
            var sftp = await CreateDestinationAsync(client, new
            {
                name = "lab-sftp",
                kind = "sftp",
                host = "backup.example",
                port = 22,
                remotePath = "chat-backup",
                username = "backup",
                secret = "sftp-secret"
            });
            var s3Job = await CreateJobAsync(client, s3, "s3-nightly");
            var sftpJob = await CreateJobAsync(client, sftp, "sftp-nightly");
            await ForceDueAsync(s3Job, sftpJob);

            var processor = factory.Services.GetRequiredService<ChatBackupDispatchProcessor>();
            (await processor.ProcessBatchAsync(CancellationToken.None)).Should().BeGreaterThanOrEqualTo(2);

            await using var db = factory.CreateMigratorDbContext();
            var runs = await db.ChatBackupRuns.IgnoreQueryFilters()
                .Where(x => x.JobId == s3Job || x.JobId == sftpJob)
                .ToListAsync();
            runs.Should().OnlyContain(x => x.Status == ChatBackupRunStatuses.Success && x.ChecksumSha256.Length == 64);
            var s3Run = runs.Single(x => x.JobId == s3Job);
            using var scope = factory.Services.CreateScope();
            var stored = await scope.ServiceProvider.GetRequiredService<IObjectStorage>().GetObjectAsync(s3Run.RemoteKey, CancellationToken.None);
            stored.Should().NotBeNull();
            using var memory = new MemoryStream();
            await stored!.CopyToAsync(memory);
            ChatBackupRules.Sha256Hex(memory.ToArray()).Should().Be(s3Run.ChecksumSha256);
            recorder.Bodies.Should().ContainSingle();
            ChatBackupRules.Sha256Hex(recorder.Bodies[0]).Should().Be(runs.Single(x => x.JobId == sftpJob).ChecksumSha256);
        }
        finally
        {
            ChatBackupTestDoubles.Sftp = null;
        }
    }

    [Fact]
    public async Task Destination_failure_is_a_failed_run_and_the_api_stays_up()
    {
        using var client = Client("demo");
        var destination = await CreateDestinationAsync(client, new
        {
            name = "down",
            kind = "smb",
            host = "127.0.0.1",
            port = 1,
            remotePath = "share",
            username = "backup",
            secret = "smb-secret"
        });
        var probed = await client.PostAsync($"{DestinationsPath()}/{destination}/test", new StringContent("{}"));
        probed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await probed.Content.ReadAsStringAsync()).Should().Contain(ChatBackupErrors.ConnectionFailed);

        var job = await CreateJobAsync(client, destination, "smb-fail");
        var ran = await SendAsync(client, $"{Root()}/jobs/{job}/run", new { }, "run-" + Guid.NewGuid().ToString("N"));
        ran.StatusCode.Should().Be(HttpStatusCode.OK);
        var run = await ran.Content.ReadFromJsonAsync<JsonElement>();
        run.GetProperty("status").GetString().Should().Be(ChatBackupRunStatuses.Failed);
        run.GetProperty("error").GetString().Should().NotContain("smb-secret");

        var health = await client.GetAsync("/health");
        health.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Webdav_and_drive_connection_tests_use_the_local_substitute()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var accept = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                try
                {
                    var context = await listener.GetContextAsync();
                    context.Response.StatusCode = 204;
                    context.Response.Close();
                }
                catch (Exception)
                {
                    break;
                }
            }
        });

        try
        {
            using var client = Client("demo");
            var webdav = await CreateDestinationAsync(client, new
            {
                name = "dav",
                kind = "webdav",
                host = "127.0.0.1",
                port,
                remotePath = "dav",
                username = "backup",
                useSsl = false,
                secret = "dav-secret"
            });
            var tested = await client.PostAsync($"{DestinationsPath()}/{webdav}/test", new StringContent("{}"));
            tested.StatusCode.Should().Be(HttpStatusCode.OK);
            (await tested.Content.ReadAsStringAsync()).Should().NotContain("dav-secret");

            var blocked = await SendAsync(client, DestinationsPath(), new
            {
                name = "drive-off",
                kind = "drive",
                host = "drive.local",
                port = 443,
                remotePath = "vibechat",
                secret = "drive-token"
            }, "drv-" + Guid.NewGuid().ToString("N"));
            (await blocked.Content.ReadAsStringAsync()).Should().Contain(ChatBackupErrors.DriveDisabled);

            using var enabled = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Features:ChatBackup:DriveEnabled", "true");
                builder.UseSetting("Features:ChatBackup:DriveEndpoint", $"http://127.0.0.1:{port}/drive");
            }).CreateClient();
            enabled.DefaultRequestHeaders.Add("X-Dev-User", "demo");
            var drive = await SendAsync(enabled, DestinationsPath(), new
            {
                name = "drive-lab",
                kind = "drive",
                host = "drive.local",
                port = 443,
                remotePath = "vibechat",
                secret = "drive-token"
            }, "drv-" + Guid.NewGuid().ToString("N"));
            drive.StatusCode.Should().Be(HttpStatusCode.OK, await drive.Content.ReadAsStringAsync());
            var driveId = (await drive.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var driveTest = await enabled.PostAsync($"{DestinationsPath()}/{driveId}/test", new StringContent("{}"));
            driveTest.StatusCode.Should().Be(HttpStatusCode.OK);
            (await driveTest.Content.ReadAsStringAsync()).Should().NotContain("drive-token");
        }
        finally
        {
            listener.Close();
            try
            {
                await accept;
            }
            catch (Exception)
            {
                // The listener closed the accept loop.
            }
        }
    }

    private async Task<Guid> CreateDestinationAsync(HttpClient client, object body)
    {
        var response = await SendAsync(client, DestinationsPath(), body, "dst-" + Guid.NewGuid().ToString("N"));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateJobAsync(HttpClient client, Guid destinationId, string name)
    {
        var response = await SendAsync(client, $"{Root()}/jobs", new
        {
            name,
            destinationId,
            includeAttachments = false,
            intervalMinutes = 5,
            retentionCount = 7,
            retentionDays = 30
        }, "job-" + Guid.NewGuid().ToString("N"));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task ForceDueAsync(params Guid[] jobIds)
    {
        await using var db = factory.CreateMigratorDbContext();
        var rows = await db.ChatBackupJobs.IgnoreQueryFilters().Where(x => jobIds.Contains(x.Id)).ToListAsync();
        foreach (var row in rows)
        {
            row.NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        }

        await db.SaveChangesAsync();
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string url, object body, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostZipAsync(HttpClient client, string url, byte[] zip, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new ByteArrayContent(zip)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return await client.SendAsync(request);
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string Root() => $"/api/v1/workspaces/{WorkspaceId}/chat-backup";

    private static string ListPath() => Root();

    private static string DestinationsPath() => $"{Root()}/destinations";

    private sealed class RecordingSftp : IChatBackupSftp
    {
        public List<byte[]> Bodies { get; } = [];

        public Task TestAsync(string host, int port, string username, string password, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task PutAsync(string host, int port, string username, string password, string remoteKey, byte[] body, CancellationToken cancellationToken)
        {
            Bodies.Add(body);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string host, int port, string username, string password, string remoteKey, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
