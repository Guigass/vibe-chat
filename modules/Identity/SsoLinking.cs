using System.Text.Json;

namespace VibeChat.Identity;

public sealed record SsoLinkSettings(bool Enabled)
{
    public static SsoLinkSettings Disabled { get; } = new(false);
}

/// <summary>Per-request flag so every profile resolution sees the same switch.</summary>
public static class SsoLinkAmbient
{
    private static readonly AsyncLocal<SsoLinkSettings?> Slot = new();

    public static SsoLinkSettings Current => Slot.Value ?? SsoLinkSettings.Disabled;

    public static IDisposable Push(SsoLinkSettings settings)
    {
        var prior = Slot.Value;
        Slot.Value = settings;
        return new Pop(prior);
    }

    private sealed class Pop(SsoLinkSettings? prior) : IDisposable
    {
        public void Dispose() => Slot.Value = prior;
    }
}

public static class SsoClaimNames
{
    public const string Issuer = "broker_issuer";
    public const string Subject = "broker_subject";
    public const string Alias = "broker_alias";
}

public static class SsoLinkReasons
{
    public const string ExistingLink = "existing-link";
    public const string ExistingSubject = "existing-subject";
    public const string PendingInvite = "pending-invite";
    public const string FirstLogin = "first-login";
}

public sealed class SsoLinkConflictException : Exception
{
    public SsoLinkConflictException()
        : base("Identity link conflict.")
    {
    }
}

public readonly record struct SsoBrokerClaims(string Issuer, string Subject, string? Alias);

public static class SsoBrokerClaimsReader
{
    public const int MaxIssuerLength = 512;
    public const int MaxSubjectLength = 256;

    public static bool TryRead(IEnumerable<System.Security.Claims.Claim> claims, out SsoBrokerClaims broker)
    {
        broker = default;
        string? issuer = null;
        string? subject = null;
        string? alias = null;
        foreach (var claim in claims)
        {
            if (claim.Type == SsoClaimNames.Issuer)
            {
                issuer = claim.Value;
            }
            else if (claim.Type == SsoClaimNames.Subject)
            {
                subject = claim.Value;
            }
            else if (claim.Type == SsoClaimNames.Alias)
            {
                alias = claim.Value;
            }
        }

        if (!TryNormalizeIssuer(issuer, out var normalizedIssuer)
            || !TryNormalizeSubject(subject, out var normalizedSubject))
        {
            return false;
        }

        broker = new SsoBrokerClaims(normalizedIssuer, normalizedSubject, NormalizeAlias(alias));
        return true;
    }

    public static bool TryNormalizeIssuer(string? value, out string issuer)
    {
        issuer = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim().TrimEnd('/');
        if (trimmed.Length is 0 or > MaxIssuerLength)
        {
            return false;
        }

        issuer = trimmed;
        return true;
    }

    public static bool TryNormalizeSubject(string? value, out string subject)
    {
        subject = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length is 0 or > MaxSubjectLength)
        {
            return false;
        }

        subject = trimmed;
        return true;
    }

    public static string? NormalizeAlias(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length is < 1 or > 64)
        {
            return null;
        }

        foreach (var ch in trimmed)
        {
            var ok = char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-';
            if (!ok)
            {
                return null;
            }
        }

        return trimmed;
    }
}

public readonly record struct SsoLinkFacts(
    Guid? ProfileByKeycloakSubject,
    bool ProfileIsPending,
    Guid? PendingByEmail,
    Guid? LinkedUserId);

public enum SsoLinkKind
{
    UseExisting,
    ClaimPending,
    CreateUnprivileged,
    Conflict
}

public readonly record struct SsoLinkDecision(SsoLinkKind Kind, Guid? UserId, string Reason);

/// <summary>
/// Account linking for a brokered login. Email never selects a non-pending user.
/// </summary>
public static class SsoLinkPolicy
{
    public static SsoLinkDecision Decide(SsoLinkFacts facts)
    {
        if (facts.LinkedUserId is Guid linked)
        {
            if (facts.ProfileByKeycloakSubject is Guid current && current != linked)
            {
                return new SsoLinkDecision(SsoLinkKind.Conflict, null, SsoLinkReasons.ExistingLink);
            }

            if (facts.PendingByEmail == linked)
            {
                return new SsoLinkDecision(SsoLinkKind.ClaimPending, linked, SsoLinkReasons.PendingInvite);
            }

            return new SsoLinkDecision(SsoLinkKind.UseExisting, linked, SsoLinkReasons.ExistingLink);
        }

        if (facts.ProfileByKeycloakSubject is Guid subjectUser && !facts.ProfileIsPending)
        {
            return new SsoLinkDecision(SsoLinkKind.UseExisting, subjectUser, SsoLinkReasons.ExistingSubject);
        }

        if (facts.PendingByEmail is Guid pending
            && (facts.ProfileByKeycloakSubject is null || facts.ProfileByKeycloakSubject == pending))
        {
            return new SsoLinkDecision(SsoLinkKind.ClaimPending, pending, SsoLinkReasons.PendingInvite);
        }

        if (facts.ProfileByKeycloakSubject is Guid pendingSubject && facts.ProfileIsPending)
        {
            return new SsoLinkDecision(SsoLinkKind.ClaimPending, pendingSubject, SsoLinkReasons.PendingInvite);
        }

        return new SsoLinkDecision(SsoLinkKind.CreateUnprivileged, null, SsoLinkReasons.FirstLogin);
    }
}

public static class KeycloakIssuerPolicy
{
    public static bool Matches(string? tokenIssuer, string? authority)
    {
        if (string.IsNullOrWhiteSpace(tokenIssuer) || string.IsNullOrWhiteSpace(authority))
        {
            return false;
        }

        return Normalize(tokenIssuer) == Normalize(authority);
    }

    public static string Normalize(string value) => value.Trim().TrimEnd('/');
}

public sealed record SsoProvider(string Alias, string DisplayName, IReadOnlyList<string> Domains);

public static class SsoProviderParser
{
    public static IReadOnlyList<SsoProvider> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var providers = new List<SsoProvider>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!IsEnabled(item) || IsHidden(item))
            {
                continue;
            }

            var alias = SsoBrokerClaimsReader.NormalizeAlias(ReadString(item, "alias"));
            if (alias is null || !char.IsAsciiLetterOrDigit(alias[0]))
            {
                continue;
            }

            var displayName = SanitizeDisplayName(ReadString(item, "displayName"));
            var domains = ReadDomains(item);
            providers.Add(new SsoProvider(alias, displayName, domains));
        }

        return providers;
    }

    private static bool IsEnabled(JsonElement item) => ReadFlag(item, "enabled", defaultValue: false);

    private static bool IsHidden(JsonElement item) => ReadFlag(item, "hideOnLogin", defaultValue: false);

    private static bool ReadFlag(JsonElement item, string name, bool defaultValue)
    {
        if (!item.TryGetProperty(name, out var value))
        {
            return defaultValue;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) ? parsed : defaultValue,
            _ => defaultValue
        };
    }

    private static string? ReadString(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    public static string SanitizeDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        var buffer = new char[Math.Min(trimmed.Length, 80)];
        var length = 0;
        foreach (var ch in trimmed)
        {
            if (length == buffer.Length)
            {
                break;
            }

            if (!char.IsControl(ch))
            {
                buffer[length++] = ch;
            }
        }

        return new string(buffer, 0, length).Trim();
    }

    private static IReadOnlyList<string> ReadDomains(JsonElement item)
    {
        if (!item.TryGetProperty("config", out var config) || config.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var raw = ReadString(config, "domains");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var domains = new List<string>();
        foreach (var part in raw.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (domains.Count == 8)
            {
                break;
            }

            var domain = part.Trim().TrimEnd('.').ToLowerInvariant();
            if (IsDomain(domain))
            {
                domains.Add(domain);
            }
        }

        return domains;
    }

    private static bool IsDomain(string value)
    {
        if (value.Length is < 1 or > 253
            || !value.Contains('.', StringComparison.Ordinal)
            || value.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var ch in value)
        {
            var ok = char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-';
            if (!ok)
            {
                return false;
            }
        }

        return !value.StartsWith('-') && !value.EndsWith('-');
    }
}

public sealed class SsoDirectoryCircuit
{
    private int _failures;
    private DateTimeOffset _openUntil;

    public bool IsOpen(DateTimeOffset now) => now < _openUntil;

    public void RecordSuccess()
    {
        _failures = 0;
        _openUntil = default;
    }

    public void RecordFailure(DateTimeOffset now)
    {
        _failures++;
        if (_failures >= 3)
        {
            _openUntil = now.AddSeconds(60);
        }
    }
}
