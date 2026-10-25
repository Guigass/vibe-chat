using System.Security.Cryptography;
using System.Text;

namespace VibeChat.Messaging;

/// <summary>
/// Offline write queue and incremental sync (B-143). Default off.
/// The cursor key only protects cursor integrity. Authorization stays on the server.
/// </summary>
public sealed class OfflineSyncOptions
{
    public const string SectionName = "Features:OfflineWrite";

    public const string Disabled = "OfflineWriteDisabled";

    public bool Enabled { get; set; }

    /// <summary>HMAC key for the opaque cursor. Empty or CHANGE_ME keeps the feature fail-closed.</summary>
    public string CursorKey { get; set; } = string.Empty;

    public static bool IsActive(OfflineSyncOptions options) =>
        options.Enabled
        && !string.IsNullOrWhiteSpace(options.CursorKey)
        && options.CursorKey.Trim().Length >= 16
        && options.CursorKey.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase) is false;
}

public static class OfflineSyncProtocol
{
    public const int Version = 1;

    public const int PageSize = 50;

    public const int CandidateCap = 200;

    public const string CursorExpired = "CursorExpired";

    public const string ProtocolUpgradeRequired = "ProtocolUpgradeRequired";

    public const string ScopeRevoked = "ScopeRevoked";

    public const string FullResyncRequired = "FullResyncRequired";

    public const string MessageVersionConflict = "MessageVersionConflict";

    public const string InvalidDevice = "InvalidDevice";

    public const string InvalidReactionIntent = "InvalidReactionIntent";

    public static long MessageVersion(DateTimeOffset? editedAt) =>
        editedAt is null ? 0 : editedAt.Value.ToUnixTimeMilliseconds();
}

public enum OfflineSyncCursorStatus
{
    Valid,
    Expired,
    Rejected,
    UpgradeRequired,
}

public readonly record struct OfflineSyncCursorState(
    OfflineSyncCursorStatus Status,
    DateTimeOffset Watermark,
    Guid LastId)
{
    public static OfflineSyncCursorState Initial { get; } = new(OfflineSyncCursorStatus.Valid, DateTimeOffset.UnixEpoch, Guid.Empty);
}

public static class OfflineSyncCursor
{
    public static string Issue(
        Guid tenantId,
        Guid userId,
        Guid deviceId,
        DateTimeOffset watermark,
        Guid lastId,
        DateTimeOffset now,
        string key,
        int version = OfflineSyncProtocol.Version)
    {
        var payload = Payload(version, tenantId, userId, deviceId, watermark, lastId, now.AddDays(7));
        return Encode(payload, Sign(payload, key));
    }

    public static OfflineSyncCursorState Read(
        string? cursor,
        Guid tenantId,
        Guid userId,
        Guid deviceId,
        DateTimeOffset now,
        string key)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return OfflineSyncCursorState.Initial;
        }

        var parts = cursor.Split('.', 2);
        if (parts.Length != 2 || !TryDecode(parts[0], out var payload) || !TryDecode(parts[1], out var signature))
        {
            return new OfflineSyncCursorState(OfflineSyncCursorStatus.Rejected, DateTimeOffset.UnixEpoch, Guid.Empty);
        }

        var expected = Sign(payload, key);
        if (expected.Length != signature.Length || !CryptographicOperations.FixedTimeEquals(expected, signature))
        {
            return new OfflineSyncCursorState(OfflineSyncCursorStatus.Rejected, DateTimeOffset.UnixEpoch, Guid.Empty);
        }

        var fields = Encoding.UTF8.GetString(payload).Split('|');
        if (fields.Length != 7
            || !int.TryParse(fields[0], out var version)
            || !Guid.TryParse(fields[1], out var cursorTenant)
            || !Guid.TryParse(fields[2], out var cursorUser)
            || !Guid.TryParse(fields[3], out var cursorDevice)
            || !long.TryParse(fields[4], out var watermarkTicks)
            || !Guid.TryParse(fields[5], out var lastId)
            || !long.TryParse(fields[6], out var expTicks))
        {
            return new OfflineSyncCursorState(OfflineSyncCursorStatus.Rejected, DateTimeOffset.UnixEpoch, Guid.Empty);
        }

        if (version != OfflineSyncProtocol.Version)
        {
            return new OfflineSyncCursorState(OfflineSyncCursorStatus.UpgradeRequired, DateTimeOffset.UnixEpoch, Guid.Empty);
        }

        if (cursorTenant != tenantId || cursorUser != userId || cursorDevice != deviceId)
        {
            return new OfflineSyncCursorState(OfflineSyncCursorStatus.Rejected, DateTimeOffset.UnixEpoch, Guid.Empty);
        }

        var watermark = DateTimeOffset.FromUnixTimeMilliseconds(watermarkTicks);
        if (DateTimeOffset.FromUnixTimeMilliseconds(expTicks) <= now)
        {
            return new OfflineSyncCursorState(OfflineSyncCursorStatus.Expired, watermark, lastId);
        }

        return new OfflineSyncCursorState(OfflineSyncCursorStatus.Valid, watermark, lastId);
    }

    private static byte[] Payload(
        int version,
        Guid tenantId,
        Guid userId,
        Guid deviceId,
        DateTimeOffset watermark,
        Guid lastId,
        DateTimeOffset expires)
    {
        var text = string.Join(
            '|',
            version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            tenantId.ToString("D"),
            userId.ToString("D"),
            deviceId.ToString("D"),
            watermark.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            lastId.ToString("D"),
            expires.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Encoding.UTF8.GetBytes(text);
    }

    private static byte[] Sign(byte[] payload, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return hmac.ComputeHash(payload);
    }

    private static string Encode(byte[] payload, byte[] signature) =>
        Base64Url(payload) + "." + Base64Url(signature);

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryDecode(string text, out byte[] bytes)
    {
        bytes = [];
        try
        {
            var padded = text.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            bytes = Convert.FromBase64String(padded);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed record OfflineSyncCandidate(
    string Kind,
    Guid ConversationId,
    Guid ChannelId,
    Guid Id,
    long Seq,
    long Version,
    string? Body,
    bool Deleted,
    DateTimeOffset Activity);

public static class OfflineSyncPage
{
    public static (IReadOnlyList<OfflineSyncCandidate> Changes, IReadOnlyList<OfflineSyncCandidate> Tombstones) Split(
        IEnumerable<OfflineSyncCandidate> rows)
    {
        var changes = new List<OfflineSyncCandidate>();
        var tombstones = new List<OfflineSyncCandidate>();
        foreach (var row in rows)
        {
            if (row.Deleted)
            {
                tombstones.Add(row with { Body = null });
            }
            else
            {
                changes.Add(row);
            }
        }

        return (changes, tombstones);
    }

    public static IReadOnlyList<OfflineSyncCandidate> Continue(
        IReadOnlyList<OfflineSyncCandidate> candidates,
        DateTimeOffset watermark,
        Guid lastId,
        int limit,
        out bool hasMore,
        out DateTimeOffset nextWatermark,
        out Guid nextId)
    {
        var ordered = candidates
            .OrderBy(row => row.Activity)
            .ThenBy(row => row.Id)
            .Where(row => row.Activity > watermark || (row.Activity == watermark && row.Id.CompareTo(lastId) > 0))
            .ToList();
        hasMore = ordered.Count > limit;
        var page = ordered.Take(limit).ToList();
        if (hasMore && page.Count > 0)
        {
            var last = page[^1];
            nextWatermark = last.Activity;
            nextId = last.Id;
        }
        else
        {
            nextWatermark = default;
            nextId = Guid.Empty;
        }

        return page;
    }
}
