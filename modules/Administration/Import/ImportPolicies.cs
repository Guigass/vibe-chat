using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using VibeChat.Files;

namespace VibeChat.Administration;

public static class ImportPolicies
{
    private static readonly Regex SecretPattern = new(
        "(sk-[A-Za-z0-9]|xox[baprs]-|Bearer\\s+[A-Za-z0-9\\-._~+/]+=*|password\\s*=)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Member", "Moderator", "Auditor", "Admin"
    };

    private static readonly HashSet<string> AllowedContentTypes = new(AttachmentPolicies.DefaultAllowedContentTypes, StringComparer.OrdinalIgnoreCase);

    static ImportPolicies()
    {
        foreach (var type in AttachmentPolicies.DefaultAllowedAudioContentTypes)
        {
            AllowedContentTypes.Add(type);
        }

        foreach (var type in AttachmentPolicies.DefaultAllowedVideoContentTypes)
        {
            AllowedContentTypes.Add(type);
        }
    }

    public static string? NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return "Member";
        }

        var trimmed = role.Trim();
        return AllowedRoles.Contains(trimmed)
            ? AllowedRoles.First(x => x.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    public static ImportParseResult Inspect(CanonicalImport import)
    {
        var report = new ImportReport();
        if (import.TenantClaimIgnored)
        {
            report.Warnings.Add("TenantIgnored");
        }

        if (import.Principals.Count > ImportLimits.MaxPrincipals
            || import.Channels.Count > ImportLimits.MaxChannels
            || import.Messages.Count > ImportLimits.MaxMessages
            || import.Attachments.Count > ImportLimits.MaxAttachments)
        {
            return new ImportParseResult(false, ImportErrors.Quota, null);
        }

        if (!Unique(import.Principals.Select(x => x.ExternalId))
            || !Unique(import.Spaces.Select(x => x.ExternalId))
            || !Unique(import.Channels.Select(x => x.ExternalId))
            || !Unique(import.Threads.Select(x => x.ExternalId))
            || !Unique(import.Messages.Select(x => x.ExternalId))
            || !Unique(import.Attachments.Select(x => x.ExternalId)))
        {
            return new ImportParseResult(false, ImportErrors.DuplicateExternalId, null);
        }

        var mapped = import.Principals.Where(x => x.MappedUserId is not null).Select(x => x.MappedUserId!.Value).ToArray();
        if (mapped.Length != mapped.Distinct().Count())
        {
            return new ImportParseResult(false, ImportErrors.MappingAmbiguous, null);
        }

        foreach (var principal in import.Principals)
        {
            var role = NormalizeRole(principal.Role);
            if (role is null)
            {
                return new ImportParseResult(false, ImportErrors.RoleForbidden, null);
            }

            principal.Role = role;
            principal.Historical = principal.MappedUserId is null;
            principal.DisplayName = Trim(principal.DisplayName, 80, principal.ExternalId);
        }

        var channels = import.Channels.ToDictionary(x => x.ExternalId, StringComparer.Ordinal);
        foreach (var channel in import.Channels)
        {
            channel.Name = channel.Name.Trim();
            if (channel.Name.Length is 0 or > 120)
            {
                channel.Ignored = true;
                channel.IgnoreReason = "InvalidChannelName";
                report.Ignored++;
                report.Warnings.Add("InvalidChannelName");
                continue;
            }

            if (channel.Kind is not ("public" or "private"))
            {
                channel.Ignored = true;
                channel.IgnoreReason = "UnsupportedChannelKind";
                report.Ignored++;
                report.Warnings.Add("UnsupportedChannelKind");
            }
        }

        var messages = import.Messages.ToDictionary(x => x.ExternalId, StringComparer.Ordinal);
        foreach (var message in import.Messages)
        {
            if (!channels.TryGetValue(message.ChannelExternalId, out var channel) || channel.Ignored)
            {
                message.Ignored = true;
                message.IgnoreReason = "UnknownChannel";
                report.Ignored++;
                continue;
            }

            if (message.Body.Length > ImportLimits.MaxBodyLength)
            {
                message.Ignored = true;
                message.IgnoreReason = "MessageBodyTooLong";
                report.Ignored++;
                report.Failed++;
                report.Warnings.Add("MessageBodyTooLong");
                continue;
            }

            if (SecretPattern.IsMatch(message.Body))
            {
                message.BodyRedactedInReport = true;
                report.Redacted++;
            }
        }

        foreach (var thread in import.Threads)
        {
            if (!messages.TryGetValue(thread.RootMessageExternalId, out var root) || root.Ignored)
            {
                report.Warnings.Add("OrphanThread");
                report.Ignored++;
            }
        }

        foreach (var message in import.Messages.Where(x => x.ThreadExternalId is not null))
        {
            if (import.Threads.All(x => x.ExternalId != message.ThreadExternalId))
            {
                message.Ignored = true;
                message.IgnoreReason = "OrphanThread";
                report.Ignored++;
            }
        }

        foreach (var attachment in import.Attachments)
        {
            ClassifyAttachment(attachment);
            if (attachment.Quarantined)
            {
                report.Quarantined++;
                if (attachment.QuarantineReason is not null)
                {
                    report.Warnings.Add(attachment.QuarantineReason);
                }

                continue;
            }

            if (string.IsNullOrEmpty(attachment.PayloadBase64))
            {
                attachment.QuarantineReason = "MissingAttachmentBytes";
                report.Ignored++;
                report.Warnings.Add("MissingAttachmentBytes");
            }
        }

        report.Principals = import.Principals.Count;
        report.Spaces = import.Spaces.Count;
        report.Channels = import.Channels.Count(x => !x.Ignored);
        report.Threads = import.Threads.Count;
        report.Messages = import.Messages.Count(x => !x.Ignored);
        report.Attachments = import.Attachments.Count(x => !x.Quarantined && !string.IsNullOrEmpty(x.PayloadBase64));
        report.Warnings = report.Warnings.Distinct(StringComparer.Ordinal).Take(40).ToList();
        return new ImportParseResult(true, null, import) { Report = report };
    }

    public static void ClassifyAttachment(CanonicalAttachment attachment)
    {
        var name = Path.GetFileName(attachment.FileName.Trim());
        if (string.IsNullOrWhiteSpace(name) || name.Contains("..", StringComparison.Ordinal) || name.Length > AttachmentPolicies.MaxFileNameLength)
        {
            Quarantine(attachment, "UnsafeFileName");
            return;
        }

        attachment.FileName = name;
        byte[]? payload = null;
        if (!string.IsNullOrEmpty(attachment.PayloadBase64))
        {
            try
            {
                payload = Convert.FromBase64String(attachment.PayloadBase64);
            }
            catch (FormatException)
            {
                Quarantine(attachment, "InvalidPayload");
                return;
            }
        }

        var length = payload?.LongLength ?? attachment.ByteLength;
        if (attachment.ByteLength <= 0)
        {
            attachment.ByteLength = length;
        }

        if (length > ImportLimits.MaxAttachmentBytes || attachment.ByteLength > ImportLimits.MaxAttachmentBytes)
        {
            Quarantine(attachment, "AttachmentTooLarge");
            return;
        }

        if (payload is not null && attachment.ByteLength > payload.LongLength * 100 && attachment.ByteLength > 1_000_000)
        {
            Quarantine(attachment, "ZipBomb");
            return;
        }

        if (payload is not null && attachment.Sha256 is not null)
        {
            var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
            if (!hash.Equals(attachment.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                Quarantine(attachment, "ChecksumMismatch");
                return;
            }
        }

        var type = attachment.ContentType.Split(';')[0].Trim();
        if (!AllowedContentTypes.Contains(type))
        {
            Quarantine(attachment, "UnsupportedMime");
            return;
        }

        attachment.ContentType = type;
    }

    public static bool LooksLikeZipBomb(ReadOnlySpan<byte> header, int compressedLength)
    {
        if (header.Length < 26 || header[0] != 0x50 || header[1] != 0x4B)
        {
            return false;
        }

        var uncompressed = BitConverter.ToUInt32(header.Slice(22, 4));
        return uncompressed > 50_000_000 || (compressedLength > 0 && uncompressed > (uint)compressedLength * 100 && uncompressed > 1_000_000);
    }

    public static void ApplyNameConflicts(
        CanonicalImport import,
        IReadOnlyDictionary<string, string> existingNameToKind,
        ImportReport report)
    {
        foreach (var channel in import.Channels.Where(x => !x.Ignored))
        {
            if (!existingNameToKind.TryGetValue(channel.Name, out var kind))
            {
                continue;
            }

            if (string.Equals(kind, channel.Kind, StringComparison.OrdinalIgnoreCase))
            {
                report.Warnings.Add("ChannelReused");
                continue;
            }

            report.Blocking = true;
            report.Conflicts.Add(channel.Name);
            report.Warnings.Add("ChannelTypeConflict");
        }

        report.Warnings = report.Warnings.Distinct(StringComparer.Ordinal).Take(40).ToList();
        report.Conflicts = report.Conflicts.Distinct(StringComparer.Ordinal).Take(40).ToList();
    }

    public static void StripPayloads(CanonicalImport import)
    {
        foreach (var attachment in import.Attachments)
        {
            attachment.PayloadBase64 = null;
        }
    }

    private static void Quarantine(CanonicalAttachment attachment, string reason)
    {
        attachment.Quarantined = true;
        attachment.QuarantineReason = reason;
        attachment.PayloadBase64 = null;
    }

    private static bool Unique(IEnumerable<string> ids)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!seen.Add(id))
            {
                return false;
            }
        }

        return true;
    }

    private static string Trim(string? value, int max, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return text.Length <= max ? text : text[..max];
    }
}

public sealed record ImportParseResult(bool Ok, string? Error, CanonicalImport? Document)
{
    public ImportReport Report { get; init; } = new();
}
