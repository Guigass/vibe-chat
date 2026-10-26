using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Minio;
using Minio.DataModel.Args;
using Renci.SshNet;
using VibeChat.Administration;

namespace VibeChat.Infrastructure;

public interface IChatBackupSftp
{
    Task TestAsync(string host, int port, string username, string password, CancellationToken cancellationToken);

    Task PutAsync(string host, int port, string username, string password, string remoteKey, byte[] body, CancellationToken cancellationToken);

    Task DeleteAsync(string host, int port, string username, string password, string remoteKey, CancellationToken cancellationToken);
}

/// <summary>Lab seam. Production leaves this null and uses SSH.NET.</summary>
public static class ChatBackupTestDoubles
{
    public static IChatBackupSftp? Sftp { get; set; }
}

public sealed class SshChatBackupSftp : IChatBackupSftp
{
    public async Task TestAsync(string host, int port, string username, string password, CancellationToken cancellationToken)
    {
        using var client = Open(host, port, username, password);
        await Task.Run(client.Connect, cancellationToken);
        client.Disconnect();
    }

    public async Task PutAsync(string host, int port, string username, string password, string remoteKey, byte[] body, CancellationToken cancellationToken)
    {
        using var client = Open(host, port, username, password);
        await Task.Run(() =>
        {
            client.Connect();
            EnsureFolder(client, remoteKey);
            using var stream = new MemoryStream(body);
            client.UploadFile(stream, remoteKey, true);
            client.Disconnect();
        }, cancellationToken);
    }

    public async Task DeleteAsync(string host, int port, string username, string password, string remoteKey, CancellationToken cancellationToken)
    {
        using var client = Open(host, port, username, password);
        await Task.Run(() =>
        {
            client.Connect();
            if (client.Exists(remoteKey))
            {
                client.DeleteFile(remoteKey);
            }

            client.Disconnect();
        }, cancellationToken);
    }

    private static SftpClient Open(string host, int port, string username, string password)
    {
        var client = new SftpClient(host, port, username, password)
        {
            OperationTimeout = ChatBackupLimits.TransferTimeout
        };
        client.ConnectionInfo.Timeout = ChatBackupLimits.TransferTimeout;
        return client;
    }

    private static void EnsureFolder(SftpClient client, string remoteKey)
    {
        var slash = remoteKey.LastIndexOf('/');
        if (slash <= 0)
        {
            return;
        }

        var folder = remoteKey[..slash];
        var current = string.Empty;
        foreach (var part in folder.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.Length == 0 ? part : current + "/" + part;
            if (!client.Exists(current))
            {
                client.CreateDirectory(current);
            }
        }
    }
}

public sealed class ChatBackupTransfer(IHttpClientFactory httpClientFactory)
{
    public const string HttpClientName = "vibechat-chat-backup";

    private readonly IChatBackupSftp _sftp = new SshChatBackupSftp();

    public async Task TestAsync(ChatBackupDestination destination, string secret, ChatBackupOptions options, CancellationToken cancellationToken)
    {
        switch (destination.Kind)
        {
            case ChatBackupKinds.Sftp:
                await Sftp().TestAsync(destination.Host, destination.Port, destination.Username, secret, cancellationToken);
                return;
            case ChatBackupKinds.Ftp:
            case ChatBackupKinds.Smb:
                await ProbeTcpAsync(destination.Host, destination.Port, cancellationToken);
                return;
            case ChatBackupKinds.S3:
                await WithS3Async(destination, secret, async client =>
                {
                    var exists = await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(destination.Bucket), cancellationToken);
                    if (!exists)
                    {
                        throw new InvalidOperationException(ChatBackupErrors.ConnectionFailed);
                    }
                }, cancellationToken);
                return;
            case ChatBackupKinds.WebDav:
                await SendAsync(WebDavUri(destination, null), HttpMethod.Options, destination.Username, secret, null, cancellationToken);
                return;
            case ChatBackupKinds.Drive:
                await SendAsync(DriveUri(options, null), HttpMethod.Get, null, secret, "Bearer", cancellationToken);
                return;
            default:
                throw new InvalidOperationException(ChatBackupErrors.Invalid);
        }
    }

    public async Task PutAsync(
        ChatBackupDestination destination,
        string secret,
        ChatBackupOptions options,
        string remoteKey,
        byte[] body,
        CancellationToken cancellationToken)
    {
        switch (destination.Kind)
        {
            case ChatBackupKinds.Sftp:
                await Sftp().PutAsync(destination.Host, destination.Port, destination.Username, secret, remoteKey, body, cancellationToken);
                return;
            case ChatBackupKinds.Ftp:
                await PutFtpAsync(destination, secret, remoteKey, body, cancellationToken);
                return;
            case ChatBackupKinds.S3:
                await WithS3Async(destination, secret, async client =>
                {
                    using var stream = new MemoryStream(body);
                    var args = new PutObjectArgs()
                        .WithBucket(destination.Bucket)
                        .WithObject(remoteKey)
                        .WithStreamData(stream)
                        .WithObjectSize(body.Length)
                        .WithContentType("application/zip");
                    await client.PutObjectAsync(args, cancellationToken);
                }, cancellationToken);
                return;
            case ChatBackupKinds.WebDav:
                await SendAsync(WebDavUri(destination, remoteKey), HttpMethod.Put, destination.Username, secret, null, cancellationToken, body);
                return;
            case ChatBackupKinds.Drive:
                await SendAsync(DriveUri(options, remoteKey), HttpMethod.Put, null, secret, "Bearer", cancellationToken, body);
                return;
            default:
                throw new InvalidOperationException(ChatBackupErrors.DestinationFailed);
        }
    }

    public async Task DeleteAsync(
        ChatBackupDestination destination,
        string secret,
        ChatBackupOptions options,
        string remoteKey,
        CancellationToken cancellationToken)
    {
        switch (destination.Kind)
        {
            case ChatBackupKinds.Sftp:
                await Sftp().DeleteAsync(destination.Host, destination.Port, destination.Username, secret, remoteKey, cancellationToken);
                return;
            case ChatBackupKinds.S3:
                await WithS3Async(destination, secret, client =>
                    client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(destination.Bucket).WithObject(remoteKey), cancellationToken), cancellationToken);
                return;
            case ChatBackupKinds.WebDav:
                await SendAsync(WebDavUri(destination, remoteKey), HttpMethod.Delete, destination.Username, secret, null, cancellationToken);
                return;
            case ChatBackupKinds.Drive:
                await SendAsync(DriveUri(options, remoteKey), HttpMethod.Delete, null, secret, "Bearer", cancellationToken);
                return;
            default:
                break;
        }
    }

    private IChatBackupSftp Sftp() => ChatBackupTestDoubles.Sftp ?? _sftp;

    private static async Task ProbeTcpAsync(string host, int port, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ChatBackupLimits.TransferTimeout);
        try
        {
            await client.ConnectAsync(host, port, timeout.Token);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            throw new InvalidOperationException(ChatBackupErrors.ConnectionFailed);
        }
    }

    private static async Task WithS3Async(ChatBackupDestination destination, string secret, Func<IMinioClient, Task> action, CancellationToken cancellationToken)
    {
        using var client = new MinioClient()
            .WithEndpoint(destination.Host, destination.Port)
            .WithCredentials(destination.Username, secret)
            .WithSSL(destination.UseSsl)
            .Build();
        await action(client);
        _ = cancellationToken;
    }

    private async Task PutFtpAsync(ChatBackupDestination destination, string secret, string remoteKey, byte[] body, CancellationToken cancellationToken)
    {
        using var tcp = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ChatBackupLimits.TransferTimeout);
        await tcp.ConnectAsync(destination.Host, destination.Port, timeout.Token);
        await using var stream = tcp.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
        await ExpectAsync(reader, "220");
        await writer.WriteLineAsync($"USER {destination.Username}");
        await ExpectAsync(reader, "331");
        await writer.WriteLineAsync($"PASS {secret}");
        await ExpectAsync(reader, "230");
        await writer.WriteLineAsync("TYPE I");
        await ExpectAsync(reader, "200");
        await writer.WriteLineAsync("PASV");
        var passive = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;
        if (!passive.StartsWith("227", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(ChatBackupErrors.DestinationFailed);
        }

        var endpoint = ParsePassive(passive);
        using var data = new TcpClient();
        await data.ConnectAsync(endpoint.Address, endpoint.Port, timeout.Token);
        await writer.WriteLineAsync($"STOR {remoteKey}");
        await ExpectAsync(reader, "150");
        await data.GetStream().WriteAsync(body, cancellationToken);
        data.Close();
        await ExpectAsync(reader, "226");
        await writer.WriteLineAsync("QUIT");
    }

    private async Task SendAsync(
        Uri uri,
        HttpMethod method,
        string? username,
        string secret,
        string? scheme,
        CancellationToken cancellationToken,
        byte[]? body = null)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(method, uri);
        if (scheme == "Bearer")
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        }
        else if (!string.IsNullOrEmpty(username))
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + secret));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        }

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ChatBackupErrors.DestinationFailed);
        }
    }

    private static Uri WebDavUri(ChatBackupDestination destination, string? remoteKey)
    {
        var scheme = destination.UseSsl ? "https" : "http";
        var path = ChatBackupRules.NormalizePath(destination.RemotePath);
        var suffix = remoteKey is null ? string.Empty : "/" + remoteKey;
        return new Uri($"{scheme}://{destination.Host}:{destination.Port}/{path}{suffix}");
    }

    private static Uri DriveUri(ChatBackupOptions options, string? remoteKey)
    {
        var root = options.DriveEndpoint.TrimEnd('/');
        if (root.Length == 0 || !Uri.TryCreate(root, UriKind.Absolute, out var uri) || uri.Scheme is not "http" and not "https")
        {
            throw new InvalidOperationException(ChatBackupErrors.DriveDisabled);
        }

        if (uri.Host.Contains("googleapis", StringComparison.OrdinalIgnoreCase) || uri.Host.Contains("google.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(ChatBackupErrors.DriveDisabled);
        }

        return remoteKey is null ? uri : new Uri(root + "/" + remoteKey);
    }

    private static async Task ExpectAsync(StreamReader reader, string code)
    {
        var line = await reader.ReadLineAsync() ?? string.Empty;
        if (!line.StartsWith(code, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(ChatBackupErrors.DestinationFailed);
        }
    }

    private static IPEndPoint ParsePassive(string line)
    {
        var open = line.IndexOf('(');
        var close = line.IndexOf(')', open + 1);
        var parts = line[(open + 1)..close].Split(',');
        if (parts.Length != 6)
        {
            throw new InvalidOperationException(ChatBackupErrors.DestinationFailed);
        }

        var host = string.Join('.', parts[..4]);
        var port = (int.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture) << 8)
            + int.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture);
        return new IPEndPoint(IPAddress.Parse(host), port);
    }
}
