using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VibeChat.SharedKernel;

namespace VibeChat.Identity;

/// <summary>
/// Packaged desktop shell (B-141 / D-20). Same web app and API. No privileged route.
/// </summary>
public sealed class DesktopOptions
{
    public const string SectionName = "Desktop";

    /// <summary>Off by default. Desktop routes return 404 until an operator turns them on.</summary>
    public bool Enabled { get; set; }

    /// <summary>Hosts accepted in <c>vibechat://</c> links. Empty rejects every link.</summary>
    public string[] AllowedHosts { get; set; } = [];

    /// <summary>HTTPS feed. Empty keeps auto-update off. The API never fetches this URL.</summary>
    public string UpdateFeedUrl { get; set; } = "";

    /// <summary>SPKI (base64) or PEM public key for update manifests. The private key stays with the operator.</summary>
    public string UpdatePublicKeyPem { get; set; } = "";

    /// <summary>External credential reference. A PEM or secret in this field is rejected.</summary>
    public string SigningCredentialRef { get; set; } = "";
}

public sealed class DesktopDevice
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public UserId UserId { get; set; }
    public string Platform { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public bool LaunchAtLogin { get; set; }
    public bool NotificationsEnabled { get; set; }
    public bool TrayEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public static class DesktopErrors
{
    public const string Disabled = "DesktopDisabled";
    public const string TokenRejected = "DesktopTokenRejected";
    public const string DeepLinkRejected = "DesktopDeepLinkRejected";
    public const string UpdateOff = "DesktopUpdateOff";
    public const string UpdateRejected = "DesktopUpdateRejected";
    public const string PlatformInvalid = "DesktopPlatformInvalid";
    public const string VersionInvalid = "DesktopVersionInvalid";
    public const string PreferenceInvalid = "DesktopPreferenceInvalid";
    public const string NotFound = "DesktopDeviceNotFound";
    public const string Forbidden = "DesktopDeviceForbidden";
    public const string Revoked = "DesktopDeviceRevoked";
    public const string Limit = "DesktopDeviceLimit";
    public const string IdempotencyKeyRequired = "IdempotencyKeyRequired";
    public const string IdempotencyConflict = "DesktopIdempotencyConflict";
    public const string BodyInvalid = "DesktopBodyInvalid";
}

public sealed record DesktopShellView(
    string Wrapper,
    string WrapperVersion,
    bool UiFork,
    string ApiBase,
    string Scheme,
    int SchemeVersion,
    string[] Targets,
    string[] Capabilities,
    bool LaunchAtLoginDefault,
    string Updates,
    bool SignedBuilds,
    string Signing,
    bool BrowserFallback,
    IReadOnlyDictionary<string, string> PackageHashes);

public sealed record DesktopDeepLinkView(string Host, string Kind, Guid Id);

public sealed record DesktopUpdateView(string ProductVersion, string FeedHost, string[] Targets);

public sealed record DesktopDeviceView(
    Guid Id,
    string Platform,
    string AppVersion,
    bool LaunchAtLogin,
    bool NotificationsEnabled,
    bool TrayEnabled,
    bool Revoked,
    bool WipeSecureStorage,
    DateTimeOffset CreatedAt);

public static class DesktopPackaging
{
    public const string RecipeId = "vibechat-desktop-recipe/1";
    public const string Wrapper = "tauri";
    public const string WrapperVersion = "2";

    public static readonly string[] Targets = ["windows-x64", "macos-arm64", "linux-x64"];

    public static readonly string[] Capabilities = ["deep-links", "notifications", "tray", "launch-at-login"];

    public static string Recipe(string target) => string.Join('\n',
    [
        RecipeId,
        "wrapper=tauri",
        "wrapperVersion=2",
        "uiFork=false",
        "apiBase=/api/v1",
        "scheme=vibechat",
        "schemeVersion=1",
        "target=" + target,
        "capabilities=deep-links,notifications,tray,launch-at-login",
        "launchAtLoginDefault=false",
        "updates=off",
        "signedBuilds=false",
        "browserFallback=true"
    ]);

    public static IReadOnlyDictionary<string, string> Hashes() =>
        Targets.ToDictionary(target => target, target => Hash(Recipe(target)), StringComparer.Ordinal);

    public static string Hash(string recipe)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(recipe));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public static partial class DesktopRules
{
    public const int MaxDevicesPerUser = 10;
    public const string Scheme = "vibechat";

    private static readonly string[] Kinds = ["channel", "thread", "workspace"];
    private static readonly HashSet<string> SecretNames = new(StringComparer.Ordinal)
    {
        "accesstoken", "refreshtoken", "idtoken", "token", "authorization", "bearer",
        "password", "secret", "privatekey", "privatepem"
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static DesktopShellView Describe(DesktopOptions options)
    {
        var signing = SigningState(options.SigningCredentialRef);
        return new DesktopShellView(
            DesktopPackaging.Wrapper,
            DesktopPackaging.WrapperVersion,
            false,
            "/api/v1",
            Scheme,
            1,
            DesktopPackaging.Targets,
            DesktopPackaging.Capabilities,
            false,
            FeedConfigured(options.UpdateFeedUrl) ? "feed" : "off",
            signing == "ready",
            signing,
            true,
            DesktopPackaging.Hashes());
    }

    public static string SigningState(string? credentialRef)
    {
        if (string.IsNullOrWhiteSpace(credentialRef))
        {
            return "off";
        }

        return IsSafeCredentialRef(credentialRef) ? "ready" : "rejected";
    }

    public static bool IsSafeCredentialRef(string credentialRef)
    {
        if (credentialRef.Length is < 3 or > 81)
        {
            return false;
        }

        if (!CredentialPattern().IsMatch(credentialRef))
        {
            return false;
        }

        var lower = credentialRef.ToLowerInvariant();
        return !lower.Contains("private", StringComparison.Ordinal)
            && !lower.Contains("secret", StringComparison.Ordinal)
            && !lower.Contains("token", StringComparison.Ordinal)
            && !lower.Contains("password", StringComparison.Ordinal)
            && !lower.Contains("begin", StringComparison.Ordinal)
            && !lower.Contains("pem", StringComparison.Ordinal);
    }

    public static bool FeedConfigured(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && !string.IsNullOrEmpty(uri.IdnHost);

    public static bool ContainsSecret(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return Walk(document.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryParseDeepLink(
        string? url,
        IReadOnlyCollection<string>? allowedHosts,
        out DesktopDeepLinkView? view,
        out string? error)
    {
        view = null;
        error = DesktopErrors.DeepLinkRejected;
        if (string.IsNullOrWhiteSpace(url) || url.Length > 512)
        {
            return false;
        }

        if (url.Contains('@', StringComparison.Ordinal)
            || url.Contains('?', StringComparison.Ordinal)
            || url.Contains('#', StringComparison.Ordinal)
            || url.Contains('\\', StringComparison.Ordinal)
            || url.Contains('%', StringComparison.Ordinal)
            || url.Contains("..", StringComparison.Ordinal)
            || url.Contains("token", StringComparison.OrdinalIgnoreCase)
            || url.Contains("bearer", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.IsDefaultPort)
        {
            return false;
        }

        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3
            || !string.Equals(parts[0], "v1", StringComparison.Ordinal)
            || !Kinds.Contains(parts[1], StringComparer.Ordinal)
            || !Guid.TryParseExact(parts[2], "D", out var id))
        {
            return false;
        }

        var host = uri.IdnHost.Trim().TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0 || !HostAllowed(host, allowedHosts))
        {
            return false;
        }

        view = new DesktopDeepLinkView(host, parts[1], id);
        error = null;
        return true;
    }

    public static bool TryVerifyUpdate(
        string? json,
        DesktopOptions options,
        out DesktopUpdateView? view,
        out string? error)
    {
        view = null;
        error = DesktopErrors.UpdateRejected;
        if (!FeedConfigured(options.UpdateFeedUrl))
        {
            error = DesktopErrors.UpdateOff;
            return false;
        }

        if (ContainsSecret(json) || string.IsNullOrWhiteSpace(json))
        {
            error = ContainsSecret(json) ? DesktopErrors.TokenRejected : DesktopErrors.UpdateRejected;
            return false;
        }

        DesktopUpdateBody? body;
        try
        {
            body = JsonSerializer.Deserialize<DesktopUpdateBody>(json, Json);
        }
        catch (JsonException)
        {
            return false;
        }

        if (body is null
            || !string.Equals(body.Channel, "stable", StringComparison.Ordinal)
            || !IsVersion(body.ProductVersion)
            || string.IsNullOrWhiteSpace(body.FeedHost)
            || body.Targets is null
            || body.Targets.Length == 0
            || body.Sha256 is null
            || !ShaPattern().IsMatch(body.Sha256)
            || string.IsNullOrWhiteSpace(body.Signature))
        {
            return false;
        }

        var feedHost = new Uri(options.UpdateFeedUrl, UriKind.Absolute).IdnHost.Trim().TrimEnd('.').ToLowerInvariant();
        if (!string.Equals(body.FeedHost.Trim().TrimEnd('.').ToLowerInvariant(), feedHost, StringComparison.Ordinal))
        {
            return false;
        }

        var targets = body.Targets.Select(item => item?.Trim() ?? "").ToArray();
        if (targets.Any(item => !DesktopPackaging.Targets.Contains(item, StringComparer.Ordinal)))
        {
            return false;
        }

        var canonical = CanonicalUpdate(body.ProductVersion!, feedHost, targets, body.Sha256);
        if (!VerifySignature(options.UpdatePublicKeyPem, canonical, body.Signature))
        {
            return false;
        }

        view = new DesktopUpdateView(body.ProductVersion!, feedHost, targets);
        error = null;
        return true;
    }

    public static string CanonicalUpdate(string productVersion, string feedHost, IEnumerable<string> targets, string sha256) =>
        string.Join('\n',
        [
            "vibechat-update/1",
            "channel=stable",
            "productVersion=" + productVersion,
            "feedHost=" + feedHost,
            "targets=" + string.Join(',', targets.OrderBy(item => item, StringComparer.Ordinal)),
            "sha256=" + sha256.ToLowerInvariant()
        ]);

    public static bool IsPlatform(string? platform) =>
        platform is not null && DesktopPackaging.Targets.Contains(platform, StringComparer.Ordinal);

    public static bool IsVersion(string? version) =>
        version is not null && version.Length <= 32 && VersionPattern().IsMatch(version);

    public static bool TryPreferences(bool? launchAtLogin, bool? notifications, bool? tray) =>
        launchAtLogin is not null && notifications is not null && tray is not null;

    public static DesktopDeviceView ToView(DesktopDevice row) => new(
        row.Id,
        row.Platform,
        row.AppVersion,
        row.LaunchAtLogin,
        row.NotificationsEnabled,
        row.TrayEnabled,
        row.RevokedAt is not null,
        row.RevokedAt is not null,
        row.CreatedAt);

    private static bool HostAllowed(string host, IReadOnlyCollection<string>? allowed)
    {
        if (allowed is null)
        {
            return false;
        }

        foreach (var item in allowed)
        {
            var normalized = (item ?? "").Trim().TrimEnd('.').ToLowerInvariant();
            if (normalized.Length > 0 && string.Equals(normalized, host, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool VerifySignature(string? material, string payload, string signature)
    {
        if (!TryLoadPublic(material, out var rsa) || rsa is null)
        {
            return false;
        }

        try
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(signature);
            }
            catch (FormatException)
            {
                return false;
            }

            return rsa.VerifyData(Encoding.UTF8.GetBytes(payload), bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        }
        finally
        {
            rsa.Dispose();
        }
    }

    private static bool TryLoadPublic(string? material, out RSA? rsa)
    {
        rsa = null;
        if (string.IsNullOrWhiteSpace(material))
        {
            return false;
        }

        try
        {
            rsa = RSA.Create();
            var trimmed = material.Trim();
            if (trimmed.Contains("BEGIN", StringComparison.Ordinal))
            {
                rsa.ImportFromPem(trimmed);
            }
            else
            {
                rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(trimmed), out _);
            }

            return true;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            rsa?.Dispose();
            rsa = null;
            return false;
        }
    }

    private static bool Walk(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (SecretNames.Contains(NormalizeName(property.Name)) || Walk(property.Value))
                    {
                        return true;
                    }
                }

                return false;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (Walk(item))
                    {
                        return true;
                    }
                }

                return false;
            case JsonValueKind.String:
                var text = element.GetString() ?? "";
                return text.Contains("BEGIN PRIVATE", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("BEGIN RSA", StringComparison.OrdinalIgnoreCase)
                    || text.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
            default:
                return false;
        }
    }

    private static string NormalizeName(string name) =>
        name.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();

    [GeneratedRegex("^[a-z0-9][a-z0-9._:/-]{2,80}$", RegexOptions.CultureInvariant)]
    private static partial Regex CredentialPattern();

    [GeneratedRegex("^[0-9]+\\.[0-9]+\\.[0-9]+([-+][0-9A-Za-z.-]{1,20})?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ShaPattern();

    private sealed class DesktopUpdateBody
    {
        public string? Channel { get; set; }
        public string? ProductVersion { get; set; }
        public string? FeedHost { get; set; }
        public string?[]? Targets { get; set; }
        public string? Sha256 { get; set; }
        public string? Signature { get; set; }
    }
}
