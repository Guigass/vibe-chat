using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VibeChat.Administration;

public sealed record ChatBackupMessageEntry(
    Guid Id,
    Guid ConversationId,
    long Sequence,
    Guid AuthorId,
    string Body,
    bool BodyRedacted,
    Guid? ReplyToMessageId,
    Guid? ThreadId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    DateTimeOffset? DeletedAt,
    Guid? DeletedBy);

public sealed record ChatBackupAttachmentEntry(
    Guid Id,
    Guid MessageId,
    Guid ChannelId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string ChecksumSha256,
    DateTimeOffset CreatedAt);

public sealed record ChatBackupPackageRead(
    bool Ok,
    string? Error,
    Guid PackageId,
    bool IncludeAttachments,
    bool ContentAuditEnabled,
    IReadOnlyList<ChatBackupMessageEntry> Messages,
    IReadOnlyList<ChatBackupAttachmentEntry> Attachments,
    IReadOnlyDictionary<string, byte[]> Objects);

public static class ChatBackupPackage
{
    public const string MessagesEntry = "payload/messages.json";
    public const string AttachmentsEntry = "payload/attachments.json";

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static byte[] Compose(
        Guid packageId,
        Guid workspaceId,
        DateTimeOffset createdAt,
        bool includeAttachments,
        bool contentAuditEnabled,
        IReadOnlyDictionary<string, byte[]> payload)
    {
        if (packageId == Guid.Empty || !payload.ContainsKey(MessagesEntry))
        {
            throw new InvalidOperationException(ChatBackupErrors.PackageInvalid);
        }

        var entries = new JsonArray();
        foreach (var pair in payload.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (!SafeEntry(pair.Key) || pair.Value.Length > ChatBackupLimits.MaxPackageBytes)
            {
                throw new InvalidOperationException(ChatBackupErrors.UnsafePath);
            }

            entries.Add(new JsonObject
            {
                ["name"] = pair.Key,
                ["sha256"] = ChatBackupRules.Sha256Hex(pair.Value),
                ["bytes"] = pair.Value.Length
            });
        }

        var manifest = new JsonObject
        {
            ["format"] = ChatBackupLimits.Format,
            ["packageId"] = packageId,
            ["workspaceId"] = workspaceId,
            ["createdAt"] = createdAt,
            ["includeAttachments"] = includeAttachments,
            ["contentAuditEnabled"] = contentAuditEnabled,
            ["entries"] = entries
        };
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, Json);
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "manifest.json", manifestBytes);
            foreach (var pair in payload.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                Write(zip, pair.Key, pair.Value);
            }
        }

        var bytes = memory.ToArray();
        if (bytes.Length > ChatBackupLimits.MaxPackageBytes)
        {
            throw new InvalidOperationException(ChatBackupErrors.TooLarge);
        }

        return bytes;
    }

    public static ChatBackupPackageRead Read(byte[] zipBytes)
    {
        if (zipBytes.Length is 0 or > ChatBackupLimits.MaxPackageBytes)
        {
            return Fail(ChatBackupErrors.TooLarge);
        }

        try
        {
            using var memory = new MemoryStream(zipBytes, writable: false);
            using var zip = new ZipArchive(memory, ZipArchiveMode.Read);
            var names = zip.Entries.Select(x => x.FullName.Replace('\\', '/')).ToArray();
            if (names.Any(name => !SafeEntry(name)) || names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            {
                return Fail(ChatBackupErrors.UnsafePath);
            }

            var manifestEntry = zip.GetEntry("manifest.json");
            if (manifestEntry is null)
            {
                return Fail(ChatBackupErrors.PackageInvalid);
            }

            var manifest = JsonNode.Parse(ReadEntry(manifestEntry))?.AsObject();
            if (manifest is null || manifest["format"]?.GetValue<string>() != ChatBackupLimits.Format)
            {
                return Fail(ChatBackupErrors.PackageInvalid);
            }

            var listed = new Dictionary<string, string>(StringComparer.Ordinal);
            long total = manifestEntry.Length;
            foreach (var node in manifest["entries"]?.AsArray() ?? [])
            {
                var name = node?["name"]?.GetValue<string>() ?? string.Empty;
                var hash = node?["sha256"]?.GetValue<string>() ?? string.Empty;
                if (!SafeEntry(name) || name == "manifest.json" || !listed.TryAdd(name, hash))
                {
                    return Fail(ChatBackupErrors.PackageInvalid);
                }
            }

            if (!listed.ContainsKey(MessagesEntry))
            {
                return Fail(ChatBackupErrors.PackageInvalid);
            }

            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (name == "manifest.json")
                {
                    continue;
                }

                if (!listed.TryGetValue(name, out var expected))
                {
                    return Fail(ChatBackupErrors.PackageInvalid);
                }

                total += entry.Length;
                if (total > ChatBackupLimits.MaxPackageBytes || entry.Length > ChatBackupLimits.MaxPackageBytes)
                {
                    return Fail(ChatBackupErrors.TooLarge);
                }

                var bytes = ReadEntry(entry);
                if (!string.Equals(ChatBackupRules.Sha256Hex(bytes), expected, StringComparison.OrdinalIgnoreCase))
                {
                    return Fail(ChatBackupErrors.ChecksumMismatch);
                }

                files[name] = bytes;
            }

            if (files.Count != listed.Count)
            {
                return Fail(ChatBackupErrors.PackageInvalid);
            }

            var messages = JsonSerializer.Deserialize<List<ChatBackupMessageEntry>>(files[MessagesEntry], Json) ?? [];
            var attachments = files.TryGetValue(AttachmentsEntry, out var attachmentBytes)
                ? JsonSerializer.Deserialize<List<ChatBackupAttachmentEntry>>(attachmentBytes, Json) ?? []
                : [];
            if (messages.Count > ChatBackupLimits.MaxMessages)
            {
                return Fail(ChatBackupErrors.TooLarge);
            }

            var objects = files
                .Where(x => x.Key.StartsWith("objects/", StringComparison.Ordinal))
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            return new ChatBackupPackageRead(
                true,
                null,
                manifest["packageId"]?.GetValue<Guid>() ?? Guid.Empty,
                manifest["includeAttachments"]?.GetValue<bool>() ?? false,
                manifest["contentAuditEnabled"]?.GetValue<bool>() ?? true,
                messages,
                attachments,
                objects);
        }
        catch (Exception)
        {
            return Fail(ChatBackupErrors.PackageInvalid);
        }
    }

    public static bool SafeEntry(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains("..", StringComparison.Ordinal) || name.Contains('\\') || name.StartsWith('/'))
        {
            return false;
        }

        if (name is "manifest.json" or MessagesEntry or AttachmentsEntry or "payload/workspace.json" or "payload/channels.json" or "payload/threads.json" or "payload/message-versions.json")
        {
            return true;
        }

        if (!name.StartsWith("objects/", StringComparison.Ordinal) || name.Count(ch => ch == '/') != 1)
        {
            return false;
        }

        return Guid.TryParseExact(name["objects/".Length..], "N", out _);
    }

    public static string ObjectName(Guid id) => "objects/" + id.ToString("N");

    private static ChatBackupPackageRead Fail(string error) =>
        new(false, error, Guid.Empty, false, false, [], [], new Dictionary<string, byte[]>());

    private static void Write(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
