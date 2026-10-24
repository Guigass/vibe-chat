using System.Text;
using System.Text.Json;
using VibeChat.SharedKernel;

namespace VibeChat.Identity;

/// <summary>
/// Hybrid mobile shell (B-063 / D-20). Same web app and API. No privileged route.
/// </summary>
public sealed class MobileOptions
{
    public const string SectionName = "Mobile";

    /// <summary>Off by default. Mobile routes return 404 until an operator turns them on.</summary>
    public bool Enabled { get; set; }

    /// <summary>Hosts accepted in <c>vibechat://</c> links and share targets. Empty rejects every link.</summary>
    public string[] AllowedHosts { get; set; } = [];

    /// <summary>External store-account reference. Empty keeps distribution on sideload. A secret in this field is rejected.</summary>
    public string StoreCredentialRef { get; set; } = "";
}

public sealed class MobileDevice
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public UserId UserId { get; set; }
    public string Platform { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public bool BiometricLock { get; set; }
    public bool NotificationsEnabled { get; set; }
    public bool CameraEnabled { get; set; }
    public bool FilePickerEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public static class MobileErrors
{
    public const string Disabled = "MobileDisabled";
    public const string TokenRejected = "MobileTokenRejected";
    public const string DeepLinkRejected = "MobileDeepLinkRejected";
    public const string ShareRejected = "MobileShareRejected";
    public const string PushRejected = "MobilePushRejected";
    public const string PlatformInvalid = "MobilePlatformInvalid";
    public const string VersionInvalid = "MobileVersionInvalid";
    public const string PreferenceInvalid = "MobilePreferenceInvalid";
    public const string NotFound = "MobileDeviceNotFound";
    public const string Forbidden = "MobileDeviceForbidden";
    public const string Revoked = "MobileDeviceRevoked";
    public const string Limit = "MobileDeviceLimit";
    public const string IdempotencyKeyRequired = "IdempotencyKeyRequired";
    public const string IdempotencyConflict = "MobileIdempotencyConflict";
    public const string BodyInvalid = "MobileBodyInvalid";
}

public sealed record MobileShellView(
    string Wrapper,
    string WrapperVersion,
    bool UiFork,
    string ApiBase,
    string Scheme,
    int SchemeVersion,
    string[] Targets,
    string[] Capabilities,
    bool BiometricLockDefault,
    string Distribution,
    string Store,
    string PushContent,
    bool BrowserFallback,
    bool SideloadReady,
    IReadOnlyDictionary<string, string> PackageHashes);

public sealed record MobileDeepLinkView(string Host, string Kind, Guid Id);

public sealed record MobileShareView(string Host, string Kind, Guid Id, bool SendsMessage);

public sealed record MobilePushView(string Kind, Guid ConversationId, bool ContainsContent);

public sealed record MobileDeviceView(
    Guid Id,
    string Platform,
    string AppVersion,
    bool BiometricLock,
    bool NotificationsEnabled,
    bool CameraEnabled,
    bool FilePickerEnabled,
    bool Revoked,
    bool WipeSecureStorage,
    DateTimeOffset CreatedAt);

public static class MobilePackaging
{
    public const string RecipeId = "vibechat-mobile-recipe/1";
    public const string Wrapper = "capacitor";
    public const string WrapperVersion = "7";

    public static readonly string[] Targets = ["android-arm64", "ios-arm64"];

    public static readonly string[] Capabilities =
        ["deep-links", "share-target", "camera", "file-picker", "web-push", "biometric-lock"];

    public static string Recipe(string target) => string.Join('\n',
    [
        RecipeId,
        "wrapper=capacitor",
        "wrapperVersion=7",
        "uiFork=false",
        "apiBase=/api/v1",
        "scheme=vibechat",
        "schemeVersion=1",
        "target=" + target,
        "capabilities=deep-links,share-target,camera,file-picker,web-push,biometric-lock",
        "biometricLockDefault=false",
        "distribution=sideload",
        "pushContent=ids-only",
        "browserFallback=true"
    ]);

    public static IReadOnlyDictionary<string, string> Hashes() =>
        Targets.ToDictionary(target => target, target => DesktopPackaging.Hash(Recipe(target)), StringComparer.Ordinal);
}

public static class MobileRules
{
    public const int MaxDevicesPerUser = 10;

    private static readonly string[] PushKinds = ["message", "mention"];
    private static readonly HashSet<string> ExtraSecretNames = new(StringComparer.Ordinal)
    {
        "fcmtoken", "devicetoken", "apnstoken", "pushtoken", "biometrictoken", "biometricsecret",
        "content", "body", "text", "preview", "title", "tenantid", "tenant"
    };

    public static MobileShellView Describe(MobileOptions options)
    {
        var store = DesktopRules.SigningState(options.StoreCredentialRef);
        return new MobileShellView(
            MobilePackaging.Wrapper,
            MobilePackaging.WrapperVersion,
            false,
            "/api/v1",
            DesktopRules.Scheme,
            1,
            MobilePackaging.Targets,
            MobilePackaging.Capabilities,
            false,
            store == "ready" ? "store" : "sideload",
            store,
            "ids-only",
            true,
            true,
            MobilePackaging.Hashes());
    }

    public static bool IsPlatform(string? platform) =>
        platform is not null && MobilePackaging.Targets.Contains(platform, StringComparer.Ordinal);

    public static bool ContainsSecret(string? json)
    {
        if (DesktopRules.ContainsSecret(json))
        {
            return true;
        }

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
        string? json,
        IReadOnlyCollection<string>? allowedHosts,
        out MobileDeepLinkView? view,
        out string? error)
    {
        view = null;
        error = MobileErrors.DeepLinkRejected;
        if (ContainsSecret(json))
        {
            error = MobileErrors.TokenRejected;
            return false;
        }

        if (!TryObject(json, out var root))
        {
            return false;
        }

        if (!ExactKeys(root, ["url"]) || root.GetProperty("url").ValueKind != JsonValueKind.String)
        {
            return false;
        }

        if (!DesktopRules.TryParseDeepLink(root.GetProperty("url").GetString(), allowedHosts, out var link, out _))
        {
            return false;
        }

        view = new MobileDeepLinkView(link!.Host, link.Kind, link.Id);
        error = null;
        return true;
    }

    public static bool TryAcceptShare(
        string? json,
        IReadOnlyCollection<string>? allowedHosts,
        out MobileShareView? view,
        out string? error)
    {
        view = null;
        error = MobileErrors.ShareRejected;
        if (ContainsSecret(json))
        {
            error = MobileErrors.TokenRejected;
            return false;
        }

        if (!TryObject(json, out var root)
            || !ExactKeys(root, ["host", "kind", "id", "confirm"])
            || root.GetProperty("confirm").ValueKind != JsonValueKind.True
            || root.GetProperty("host").ValueKind != JsonValueKind.String
            || root.GetProperty("kind").ValueKind != JsonValueKind.String
            || root.GetProperty("id").ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var host = root.GetProperty("host").GetString() ?? "";
        var kind = root.GetProperty("kind").GetString() ?? "";
        var id = root.GetProperty("id").GetString() ?? "";
        if (host.IndexOfAny(['/', '@', '?', '#', ' ', '\\']) >= 0)
        {
            return false;
        }

        var url = "vibechat://" + host + "/v1/" + kind + "/" + id;
        if (!DesktopRules.TryParseDeepLink(url, allowedHosts, out var link, out _))
        {
            return false;
        }

        view = new MobileShareView(link!.Host, link.Kind, link.Id, false);
        error = null;
        return true;
    }

    public static bool TryAcceptPush(string? json, out MobilePushView? view, out string? error)
    {
        view = null;
        error = MobileErrors.PushRejected;
        if (ContainsSecret(json))
        {
            error = MobileErrors.TokenRejected;
            return false;
        }

        if (!TryObject(json, out var root)
            || !ExactKeys(root, ["kind", "conversationId"])
            || root.GetProperty("kind").ValueKind != JsonValueKind.String
            || root.GetProperty("conversationId").ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var kind = root.GetProperty("kind").GetString();
        if (kind is null || !PushKinds.Contains(kind, StringComparer.Ordinal))
        {
            return false;
        }

        if (!Guid.TryParseExact(root.GetProperty("conversationId").GetString(), "D", out var id))
        {
            return false;
        }

        view = new MobilePushView(kind, id, false);
        error = null;
        return true;
    }

    public static bool TryPreferences(bool? biometricLock, bool? notifications, bool? camera, bool? filePicker) =>
        biometricLock is not null && notifications is not null && camera is not null && filePicker is not null;

    public static bool IsRegisterBody(string? json) =>
        TryObject(json, out var root)
        && ExactKeys(root, ["platform", "appVersion"])
        && root.GetProperty("platform").ValueKind == JsonValueKind.String
        && root.GetProperty("appVersion").ValueKind == JsonValueKind.String;

    public static bool IsPreferenceBody(string? json) =>
        TryObject(json, out var root) && ExactKeys(root, ["biometricLock", "notifications", "camera", "filePicker"]);

    public static MobileDeviceView ToView(MobileDevice row) => new(
        row.Id,
        row.Platform,
        row.AppVersion,
        row.BiometricLock,
        row.NotificationsEnabled,
        row.CameraEnabled,
        row.FilePickerEnabled,
        row.RevokedAt is not null,
        row.RevokedAt is not null,
        row.CreatedAt);

    private static bool TryObject(string? json, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            root = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ExactKeys(JsonElement root, string[] names)
    {
        var seen = 0;
        foreach (var property in root.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.Ordinal))
            {
                return false;
            }

            seen++;
        }

        return seen == names.Length;
    }

    private static bool Walk(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (ExtraSecretNames.Contains(NormalizeName(property.Name)) || Walk(property.Value))
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
            default:
                return false;
        }
    }

    private static string NormalizeName(string name) =>
        name.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
}
