using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VibeChat.SharedKernel;

namespace VibeChat.Messaging;

/// <summary>
/// Confidential channels (B-064). The server stores ciphertext and public keys only.
/// </summary>
public sealed class E2eeOptions
{
    public const string SectionName = "Messaging:E2ee";

    /// <summary>Off until an independent crypto review. Routes return 404.</summary>
    public bool Enabled { get; set; }
}

public static class E2eeErrors
{
    public const string Disabled = "E2eeDisabled";
    public const string EnvelopeRequired = "E2eeEnvelopeRequired";
    public const string VersionUnsupported = "E2eeVersionUnsupported";
    public const string EpochStale = "E2eeEpochStale";
    public const string RecipientsMismatch = "E2eeRecipientsMismatch";
    public const string DeviceRequired = "E2eeDeviceRequired";
    public const string DeviceInvalid = "E2eeDeviceInvalid";
    public const string ForwardForbidden = "E2eeForwardForbidden";
    public const string CapabilityUnavailable = "E2eeCapabilityUnavailable";
    public const string AttachmentRequired = "E2eeAttachmentRequired";
    public const string BackupInvalid = "E2eeBackupInvalid";
    public const string BackupWarning = "E2eeBackupNoEscrow";
}

public static class E2eeRules
{
    public const int EnvelopeVersion = 1;
    public const string Protocol = "hpke-base-p256";
    public const string SealedContentType = "application/vnd.vibechat.e2ee";
    public const int MaxRecipients = 64;
    public const int MaxPublicJwkLength = 1024;
    public const int MaxBackupLength = 16_384;
    public const int MinCipherFieldLength = 4;

    public static readonly string[] ReducedCapabilities =
    [
        "server-search",
        "rag",
        "dlp",
        "body-moderation",
        "legal-hold-body",
        "server-preview",
        "transcription",
        "ai-summary",
        "polls",
        "schedule",
        "forward"
    ];

    public static readonly string[] KeptCapabilities =
    [
        "membership",
        "metadata-audit",
        "realtime-ciphertext"
    ];

    public static bool IsSealedContentType(string? contentType) =>
        string.Equals(contentType?.Trim(), SealedContentType, StringComparison.OrdinalIgnoreCase);

    public static string? ValidatePublicJwk(string? publicJwk)
    {
        if (string.IsNullOrWhiteSpace(publicJwk) || publicJwk.Length > MaxPublicJwkLength)
        {
            return E2eeErrors.DeviceInvalid;
        }

        try
        {
            using var doc = JsonDocument.Parse(publicJwk);
            var root = doc.RootElement;
            if (!FieldEquals(root, "kty", "EC") || !FieldEquals(root, "crv", "P-256"))
            {
                return E2eeErrors.DeviceInvalid;
            }

            if (!HasField(root, "x") || !HasField(root, "y") || root.TryGetProperty("d", out _))
            {
                return E2eeErrors.DeviceInvalid;
            }
        }
        catch (JsonException)
        {
            return E2eeErrors.DeviceInvalid;
        }

        return null;
    }

    public static string Fingerprint(string publicJwk)
    {
        using var doc = JsonDocument.Parse(publicJwk);
        var root = doc.RootElement;
        var material = string.Join('|',
            root.GetProperty("kty").GetString(),
            root.GetProperty("crv").GetString(),
            root.GetProperty("x").GetString(),
            root.GetProperty("y").GetString());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    /// <summary>
    /// Structural check only. The server never decrypts.
    /// Recipient device ids must be exactly the active devices of current members.
    /// </summary>
    public static string? ValidateEnvelope(string? body, int generation, IReadOnlyCollection<Guid> allowedDeviceIds)
    {
        if (string.IsNullOrWhiteSpace(body) || generation < 1 || allowedDeviceIds.Count == 0)
        {
            return allowedDeviceIds.Count == 0 ? E2eeErrors.DeviceRequired : E2eeErrors.EnvelopeRequired;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return E2eeErrors.EnvelopeRequired;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.TryGetProperty("plaintext", out _)
                || root.TryGetProperty("body", out _))
            {
                return E2eeErrors.EnvelopeRequired;
            }

            if (!root.TryGetProperty("v", out var version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != EnvelopeVersion)
            {
                return E2eeErrors.VersionUnsupported;
            }

            if (!root.TryGetProperty("epoch", out var epoch) || epoch.ValueKind != JsonValueKind.Number)
            {
                return E2eeErrors.EnvelopeRequired;
            }

            if (epoch.GetInt32() != generation)
            {
                return E2eeErrors.EpochStale;
            }

            if (!root.TryGetProperty("recipients", out var recipients) || recipients.ValueKind != JsonValueKind.Array)
            {
                return E2eeErrors.EnvelopeRequired;
            }

            var kids = new HashSet<Guid>();
            foreach (var recipient in recipients.EnumerateArray())
            {
                if (recipient.ValueKind != JsonValueKind.Object
                    || !recipient.TryGetProperty("kid", out var kidNode)
                    || !Guid.TryParse(kidNode.GetString(), out var kid)
                    || !IsCipherField(recipient, "enc")
                    || !IsCipherField(recipient, "ct"))
                {
                    return E2eeErrors.EnvelopeRequired;
                }

                if (!kids.Add(kid))
                {
                    return E2eeErrors.RecipientsMismatch;
                }
            }

            if (kids.Count == 0 || kids.Count > MaxRecipients || !kids.SetEquals(allowedDeviceIds))
            {
                return E2eeErrors.RecipientsMismatch;
            }
        }

        return null;
    }

    public static string? ValidateBackup(string? blob)
    {
        if (string.IsNullOrWhiteSpace(blob) || blob.Length > MaxBackupLength)
        {
            return E2eeErrors.BackupInvalid;
        }

        try
        {
            using var doc = JsonDocument.Parse(blob);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.TryGetProperty("plaintext", out _)
                || root.TryGetProperty("d", out _)
                || !FieldEquals(root, "v", "1")
                || !IsCipherField(root, "salt")
                || !IsCipherField(root, "iv")
                || !IsCipherField(root, "ct"))
            {
                return E2eeErrors.BackupInvalid;
            }
        }
        catch (JsonException)
        {
            return E2eeErrors.BackupInvalid;
        }

        return null;
    }

    private static bool IsCipherField(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var node)
        && node.ValueKind == JsonValueKind.String
        && IsBase64Url(node.GetString());

    private static bool IsBase64Url(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < MinCipherFieldLength)
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool HasField(JsonElement root, string name) =>
        root.TryGetProperty(name, out var node)
        && node.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(node.GetString());

    private static bool FieldEquals(JsonElement root, string name, string expected) =>
        root.TryGetProperty(name, out var node)
        && node.ValueKind == JsonValueKind.String
        && string.Equals(node.GetString(), expected, StringComparison.Ordinal);
}

public sealed class E2eeDevice
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public UserId UserId { get; set; }
    public string PublicJwk { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class E2eeBackup
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public UserId UserId { get; set; }
    public string Blob { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class E2eeVerification
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public UserId UserId { get; set; }
    public Guid DeviceId { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public DateTimeOffset VerifiedAt { get; set; }
}
