using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VibeChat.SharedKernel;

namespace VibeChat.Integrations;

/// <summary>
/// Bidirectional connector installation (B-127). Secrets stay in envelopes.
/// The inbound tenant is the installation row, never a field of the payload.
/// </summary>
public sealed class ConnectorInstallation
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string OpaqueKey { get; set; } = string.Empty;
    public string Status { get; set; } = ConnectorPolicies.StatusActive;
    public string DestinationKind { get; set; } = string.Empty;
    public Guid DestinationId { get; set; }
    public string[] Scopes { get; set; } = [];
    public string[] AllowlistHosts { get; set; } = [];
    public string? BaseUrl { get; set; }
    public string? ClientId { get; set; }
    public string? OauthStateHash { get; set; }
    public EncryptedSecretEnvelope AccessToken { get; set; } = new();
    public EncryptedSecretEnvelope RefreshToken { get; set; } = new();
    public EncryptedSecretEnvelope OauthVerifier { get; set; } = new();
    public int SecretVersion { get; set; } = 1;
    public string Circuit { get; set; } = ConnectorPolicies.CircuitClosed;
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? OpenUntil { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastErrorCode { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public UserId CreatedBy { get; set; }
    public UserId? IdentityUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class ConnectorEffect
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid InstallationId { get; set; }
    public string ExternalEventId { get; set; } = string.Empty;
    public string DestinationKind { get; set; } = string.Empty;
    public Guid DestinationId { get; set; }
    public Guid EffectRef { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ConnectorDelivery
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid InstallationId { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public int Attempt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ConnectorCheckpoint
{
    public Guid InstallationId { get; set; }
    public TenantId TenantId { get; set; }
    public string Cursor { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ConnectorOptions
{
    public const string SectionName = "Connectors";

    public bool Enabled { get; set; }
}

public sealed record ConnectorCatalogEntry(string Id, string[] Scopes, bool Oauth, string[] AllowlistHosts);

public sealed record ConnectorInstallationState(
    Guid Id,
    string Provider,
    string Status,
    string DestinationKind,
    Guid DestinationId,
    string[] Scopes,
    string? SecretMask,
    string Circuit,
    string? LastErrorCode,
    int SecretVersion,
    string InboundPath);

public sealed record ConnectorOauthStart(string AuthorizeUrl, string[] Scopes);

public sealed record ConnectorHttpRequest(
    string Method,
    Uri Url,
    string? Body,
    string? ContentType,
    IReadOnlyDictionary<string, string> Headers);

public sealed record ConnectorHttpResult(int StatusCode, string? Body, string? Location, string? ErrorCode);

public interface IConnectorTransport
{
    Task<ConnectorHttpResult> SendAsync(ConnectorHttpRequest request, CancellationToken cancellationToken);
}

public interface IConnectorAddressLookup
{
    IReadOnlyList<IPAddress> Lookup(string host);
}

public readonly record struct EgressDecision(bool Allowed, string? ErrorCode);

public readonly record struct CircuitSnapshot(
    string Circuit,
    int Failures,
    DateTimeOffset? OpenUntil,
    DateTimeOffset? NextAttemptAt);

public static class ConnectorPolicies
{
    public const string ContractVersion = "connector.manifest.v1";
    public const string ProviderGitHub = "github";
    public const string ProviderGitLab = "gitlab";
    public const string ProviderCalendar = "calendar";
    public const string StatusActive = "active";
    public const string StatusRevoked = "revoked";
    public const string DestinationChannel = "channel";
    public const string DestinationTask = "task";
    public const string DestinationIncident = "incident";
    public const string CircuitClosed = "closed";
    public const string CircuitOpen = "open";
    public const string DirectionInbound = "inbound";
    public const string DirectionOutbound = "outbound";
    public const string DeliveryDelivered = "delivered";
    public const string DeliveryFailed = "failed";
    public const string DeliverySkipped = "skipped";
    public const string SignatureHeader = "X-VibeChat-Connector-Signature";
    public const string GitHubSignatureHeader = "X-Hub-Signature-256";
    public const string GitLabTokenHeader = "X-Gitlab-Token";
    public const string AccessKind = "connector-access";
    public const string RefreshKind = "connector-refresh";
    public const string VerifierKind = "connector-verifier";
    public const string JobRole = "connector";
    public const string IdentitySubjectPrefix = "connector:";
    public const int MaxInstallationsPerWorkspace = 20;
    public const int MaxFailuresBeforeOpen = 3;
    public const int MaxRedirectHops = 2;
    public const int MaxBodyChars = 16_384;
    public const int MaxTitleLength = 80;
    public const int MaxExternalIdLength = 80;
    public const int MaxIdempotencyKeyLength = 80;
    public const int MaxTokenLength = 200;
    public const int MinTokenLength = 16;
    public static readonly TimeSpan CircuitOpenFor = TimeSpan.FromSeconds(60);

    public static readonly ConnectorCatalogEntry[] Catalog =
    [
        new(ProviderGitHub, ["issues:read", "issues:write"], true, ["api.github.com", "github.com"]),
        new(ProviderGitLab, ["read_api", "write_repository"], true, ["gitlab.com"]),
        new(ProviderCalendar, ["calendar:read", "calendar:write"], false, []),
    ];

    public static string Normalize(string? value) => (value ?? string.Empty).Trim();

    public static ConnectorCatalogEntry? Find(string provider) =>
        Catalog.FirstOrDefault(entry => string.Equals(entry.Id, provider, StringComparison.Ordinal));

    public static bool IsValidIdempotencyKey(string key) =>
        key.Length is >= 8 and <= MaxIdempotencyKeyLength
        && key.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');

    public static bool IsValidToken(string token) =>
        token.Length is >= MinTokenLength and <= MaxTokenLength
        && token.All(ch => !char.IsWhiteSpace(ch))
        && !token.Contains("://", StringComparison.Ordinal);

    public static bool IsValidClientId(string? clientId) =>
        !string.IsNullOrWhiteSpace(clientId)
        && clientId.Trim().Length <= 80
        && clientId.Trim().All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-');

    public static bool ScopesMatch(IReadOnlyCollection<string> requested, IReadOnlyCollection<string> minimum)
    {
        var left = requested.Select(scope => scope.Trim()).Where(scope => scope.Length > 0).ToHashSet(StringComparer.Ordinal);
        var right = minimum.ToHashSet(StringComparer.Ordinal);
        return left.SetEquals(right);
    }

    public static bool IsDestination(string kind) =>
        kind is DestinationChannel or DestinationTask or DestinationIncident;

    public static string InboundPath(string opaqueKey) => $"/api/v1/connectors/inbound/{opaqueKey}";

    public static string NewOpaqueKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public static string ComputeSignature(string secret, string body)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool SignatureMatches(string secret, string body, string? header)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(header))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(ComputeSignature(secret, body));
        var actual = Encoding.UTF8.GetBytes(header.Trim().ToLowerInvariant());
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public static bool TokenMatches(string secret, string? header)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(header))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(secret);
        var actual = Encoding.UTF8.GetBytes(header.Trim());
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public static bool InboundAuthenticated(string provider, string secret, string body, string? own, string? github, string? gitlab)
    {
        if (string.Equals(provider, ProviderGitLab, StringComparison.Ordinal) && TokenMatches(secret, gitlab))
        {
            return true;
        }

        return SignatureMatches(secret, body, own) || SignatureMatches(secret, body, github);
    }

    public static string SanitizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(Math.Min(value.Length, MaxTitleLength));
        foreach (var ch in value.Trim())
        {
            if (buffer.Length >= MaxTitleLength)
            {
                break;
            }

            if (char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_' or '.' or ':')
            {
                buffer.Append(ch);
            }
        }

        return buffer.ToString().Trim();
    }

    public static string Summary(string provider, string externalId, string title)
    {
        var safeTitle = SanitizeTitle(title);
        return safeTitle.Length == 0 ? $"{provider} {externalId}" : $"{provider} {externalId}: {safeTitle}";
    }

    public static bool TryReadEvent(string provider, string body, out string externalId, out string title, out string error)
    {
        externalId = string.Empty;
        title = string.Empty;
        error = "InvalidConnectorEvent";
        if (body.Length is 0 or > MaxBodyChars)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (string.Equals(provider, ProviderGitHub, StringComparison.Ordinal) && root.TryGetProperty("issue", out var issue))
            {
                externalId = ReadId(issue, "id");
                title = ReadString(issue, "title");
            }
            else if (string.Equals(provider, ProviderGitLab, StringComparison.Ordinal)
                && root.TryGetProperty("object_attributes", out var attributes))
            {
                externalId = ReadId(attributes, "id");
                title = ReadString(attributes, "title");
            }
            else if (string.Equals(provider, ProviderCalendar, StringComparison.Ordinal))
            {
                externalId = ReadString(root, "uid");
                title = ReadString(root, "summary");
            }

            if (externalId.Length == 0)
            {
                externalId = ReadId(root, "id");
                if (title.Length == 0)
                {
                    title = ReadString(root, "title");
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        if (!IsValidExternalId(externalId))
        {
            return false;
        }

        title = SanitizeTitle(title);
        error = string.Empty;
        return true;
    }

    public static bool TryReadToken(string? body, string provider, out string accessToken, out string? refreshToken, out string error)
    {
        accessToken = string.Empty;
        refreshToken = null;
        error = "OauthFailed";
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            accessToken = ReadString(root, "access_token");
            var refresh = ReadString(root, "refresh_token");
            refreshToken = refresh.Length == 0 ? null : refresh;
            if (!IsValidToken(accessToken))
            {
                return false;
            }

            if (root.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.String)
            {
                var granted = scope.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var minimum = Find(provider)?.Scopes ?? [];
                if (!ScopesMatch(granted, minimum))
                {
                    error = "ScopeMismatch";
                    accessToken = string.Empty;
                    refreshToken = null;
                    return false;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static Uri? ProbeUri(ConnectorInstallation installation)
    {
        var entry = Find(installation.Provider);
        if (entry is null)
        {
            return null;
        }

        if (string.Equals(installation.Provider, ProviderCalendar, StringComparison.Ordinal))
        {
            return Uri.TryCreate(installation.BaseUrl, UriKind.Absolute, out var calendar) ? calendar : null;
        }

        var probe = string.Equals(installation.Provider, ProviderGitHub, StringComparison.Ordinal)
            ? "https://api.github.com/"
            : "https://gitlab.com/api/v4/version";
        return Uri.TryCreate(probe, UriKind.Absolute, out var uri) ? uri : null;
    }

    public static Uri? TokenUri(string provider) => provider switch
    {
        ProviderGitHub => new Uri("https://github.com/login/oauth/access_token"),
        ProviderGitLab => new Uri("https://gitlab.com/oauth/token"),
        _ => null,
    };

    public static string? AuthorizeUrl(string provider, string clientId, string state, string challenge)
    {
        var entry = Find(provider);
        if (entry is null || !entry.Oauth)
        {
            return null;
        }

        var scopes = string.Join("%20", entry.Scopes);
        var id = Uri.EscapeDataString(clientId);
        var encodedState = Uri.EscapeDataString(state);
        var encodedChallenge = Uri.EscapeDataString(challenge);
        return provider switch
        {
            ProviderGitHub =>
                $"https://github.com/login/oauth/authorize?client_id={id}&scope={scopes}&state={encodedState}&code_challenge={encodedChallenge}&code_challenge_method=S256",
            ProviderGitLab =>
                $"https://gitlab.com/oauth/authorize?client_id={id}&response_type=code&scope={scopes}&state={encodedState}&code_challenge={encodedChallenge}&code_challenge_method=S256",
            _ => null,
        };
    }

    public static string NewState() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public static string HashState(string state) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state))).ToLowerInvariant();

    public static (string Verifier, string Challenge) NewPkce()
    {
        var verifier = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return (verifier, challenge);
    }

    public static bool MayCall(ConnectorInstallation installation, DateTimeOffset now, out string error)
    {
        if (!string.Equals(installation.Status, StatusActive, StringComparison.Ordinal))
        {
            error = "ConnectorRevoked";
            return false;
        }

        if (string.Equals(installation.Circuit, CircuitOpen, StringComparison.Ordinal)
            && installation.OpenUntil is DateTimeOffset until
            && until > now)
        {
            error = "CircuitOpen";
            return false;
        }

        if (installation.NextAttemptAt is DateTimeOffset next && next > now)
        {
            error = "Backoff";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static CircuitSnapshot RecordFailure(int failures, DateTimeOffset now)
    {
        var next = failures + 1;
        var open = next >= MaxFailuresBeforeOpen;
        var delaySeconds = Math.Min(30, Math.Pow(2, next));
        return new CircuitSnapshot(
            open ? CircuitOpen : CircuitClosed,
            next,
            open ? now.Add(CircuitOpenFor) : null,
            now.Add(TimeSpan.FromSeconds(delaySeconds)));
    }

    public static CircuitSnapshot RecordSuccess() => new(CircuitClosed, 0, null, null);

    public static EgressDecision Authorize(
        Uri url,
        IReadOnlyList<IPAddress> firstLookup,
        IReadOnlyList<IPAddress> secondLookup,
        IReadOnlyCollection<string> allowlist)
    {
        if (!string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(url.UserInfo)
            || IsBlockedHost(url.Host))
        {
            return new EgressDecision(false, "EgressBlocked");
        }

        var host = url.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (!allowlist.Contains(host, StringComparer.OrdinalIgnoreCase))
        {
            return new EgressDecision(false, "EgressBlocked");
        }

        if (firstLookup.Count == 0 || secondLookup.Count == 0)
        {
            return new EgressDecision(false, "EgressBlocked");
        }

        var firstBlocked = firstLookup.Any(IsBlockedAddress);
        var secondBlocked = secondLookup.Any(IsBlockedAddress);
        if (secondBlocked && !firstBlocked)
        {
            return new EgressDecision(false, "DnsRebinding");
        }

        if (firstBlocked || secondBlocked)
        {
            return new EgressDecision(false, "EgressBlocked");
        }

        return new EgressDecision(true, null);
    }

    public static bool IsBlockedHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return true;
        }

        var name = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (name is "localhost" or "metadata" or "metadata.google.internal" or "kubernetes.default")
        {
            return true;
        }

        return name.EndsWith(".local", StringComparison.Ordinal)
            || name.EndsWith(".internal", StringComparison.Ordinal)
            || name.EndsWith(".localhost", StringComparison.Ordinal);
    }

    public static bool IsBlockedAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
            {
                return true;
            }

            var bytes = address.GetAddressBytes();
            return (bytes[0] & 0xFE) == 0xFC;
        }

        var v4 = address.GetAddressBytes();
        if (v4[0] is 0 or 10 or 127 || v4[0] >= 224)
        {
            return true;
        }

        if (v4[0] == 100 && v4[1] is >= 64 and <= 127)
        {
            return true;
        }

        if (v4[0] == 169 && v4[1] == 254)
        {
            return true;
        }

        if (v4[0] == 172 && v4[1] is >= 16 and <= 31)
        {
            return true;
        }

        if (v4[0] == 192 && v4[1] == 168)
        {
            return true;
        }

        if (v4[0] == 192 && v4[1] == 0 && v4[2] == 2)
        {
            return true;
        }

        if (v4[0] == 198 && v4[1] == 51 && v4[2] == 100)
        {
            return true;
        }

        return v4[0] == 203 && v4[1] == 0 && v4[2] == 113;
    }

    public static IReadOnlyList<IPAddress> AddressesFor(Uri url, IConnectorAddressLookup lookup)
    {
        if (IPAddress.TryParse(url.IdnHost, out var literal))
        {
            return [literal];
        }

        return lookup.Lookup(url.IdnHost);
    }

    private static bool IsValidExternalId(string value) =>
        value.Length is >= 1 and <= MaxExternalIdLength
        && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');

    private static string ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string ReadId(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.TryGetInt64(out var number)
                ? number.ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            _ => string.Empty,
        };
    }
}
