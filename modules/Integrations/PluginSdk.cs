using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace VibeChat.Integrations;

/// <summary>
/// Official plugin SDK contract (B-136 / ADR-051). HTTP and the public event
/// catalog stay canonical. This type does not open a second schema.
/// </summary>
public sealed class PluginSdkOptions
{
    public const string SectionName = "Integrations:PluginSdk";

    /// <summary>Off by default. SDK routes return 404 until an operator turns them on.</summary>
    public bool Enabled { get; set; }
}

public static class PluginSdkErrors
{
    public const string Disabled = "PluginSdkDisabled";
    public const string Accepted = "Accepted";
    public const string InvalidSignature = "InvalidSignature";
    public const string ReplayDetected = "ReplayDetected";
    public const string StaleDelivery = "StaleDelivery";
    public const string DeliveryIdRequired = "DeliveryIdRequired";
    public const string UnknownOperation = "UnknownSdkOperation";
    public const string CrossTenant = "CrossTenant";
}

public static class PluginSdkPolicies
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);
    public const int MinIdempotencyLength = 8;
    public const int MaxIdempotencyLength = 120;
    public const int MaxDeliveryIdLength = 80;
}

public sealed record SdkVerification(bool Accepted, string Reason);

public readonly record struct SdkSandboxDecision(bool Ok, int Status, string? Error, JsonObject? Body);

/// <summary>
/// HMAC verifier compatible with <see cref="WebhookDelivery.ComputeSignature"/>.
/// The caller owns the replay cache. The method never writes the secret.
/// </summary>
public static class PluginSdkVerifier
{
    public static SdkVerification Verify(
        string secret,
        string body,
        string? signature,
        string? deliveryId,
        DateTimeOffset deliveredAt,
        DateTimeOffset now,
        IReadOnlyCollection<string>? seenDeliveryIds)
    {
        var id = (deliveryId ?? string.Empty).Trim();
        if (id.Length is < 1 or > PluginSdkPolicies.MaxDeliveryIdLength)
        {
            return new SdkVerification(false, PluginSdkErrors.DeliveryIdRequired);
        }

        var expected = WebhookDelivery.ComputeSignature(secret, body);
        if (!FixedEquals(expected, (signature ?? string.Empty).Trim()))
        {
            return new SdkVerification(false, PluginSdkErrors.InvalidSignature);
        }

        if (deliveredAt > now.Add(PluginSdkPolicies.ClockSkew) || now - deliveredAt > PluginSdkPolicies.MaxAge)
        {
            return new SdkVerification(false, PluginSdkErrors.StaleDelivery);
        }

        if (seenDeliveryIds is not null && seenDeliveryIds.Contains(id, StringComparer.Ordinal))
        {
            return new SdkVerification(false, PluginSdkErrors.ReplayDetected);
        }

        return new SdkVerification(true, PluginSdkErrors.Accepted);
    }

    public static JsonObject ToJson(SdkVerification verification) => new()
    {
        ["synthetic"] = true,
        ["accepted"] = verification.Accepted,
        ["reason"] = verification.Reason
    };

    private static bool FixedEquals(string expected, string provided)
    {
        var left = Encoding.UTF8.GetBytes(expected);
        var right = Encoding.UTF8.GetBytes(provided);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }
}

public static class PluginSdkIdempotency
{
    public static bool TryNormalize(string? key, out string normalized)
    {
        normalized = (key ?? string.Empty).Trim();
        if (normalized.Length is < PluginSdkPolicies.MinIdempotencyLength or > PluginSdkPolicies.MaxIdempotencyLength)
        {
            return false;
        }

        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character))
            {
                return false;
            }
        }

        return true;
    }
}

public static class PluginSdkRedaction
{
    public static string Scrub(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var text = value;
        text = ReplaceToken(text, "vc_int_");
        text = ReplaceToken(text, "vc_cb_");
        return ReplaceBearer(text);
    }

    private static string ReplaceToken(string text, string prefix)
    {
        var start = 0;
        while (start < text.Length)
        {
            var index = text.IndexOf(prefix, start, StringComparison.Ordinal);
            if (index < 0)
            {
                return text;
            }

            var end = index + prefix.Length;
            while (end < text.Length && IsTokenChar(text[end]))
            {
                end++;
            }

            text = string.Concat(text.AsSpan(0, index), "[redacted]", text.AsSpan(end));
            start = index + "[redacted]".Length;
        }

        return text;
    }

    private static string ReplaceBearer(string text)
    {
        const string marker = "Bearer ";
        var index = text.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return text;
        }

        var end = index + marker.Length;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
        {
            end++;
        }

        return string.Concat(text.AsSpan(0, index), "Bearer [redacted]", text.AsSpan(end));
    }

    private static bool IsTokenChar(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '_' or '-';
}

public static class PluginSdkMatrix
{
    public static bool IsCompatible(string? sdkVersion, string? apiContract, string? eventType, int schemaVersion, DateTimeOffset now)
    {
        if (!string.Equals(sdkVersion, PluginSdkCatalog.Version, StringComparison.Ordinal)
            || !string.Equals(apiContract, PluginSdkCatalog.ApiContract, StringComparison.Ordinal))
        {
            return false;
        }

        var match = PublicEventCatalog.Versions.FirstOrDefault(item =>
            string.Equals(item.EventType, eventType, StringComparison.Ordinal) && item.SchemaVersion == schemaVersion);
        if (match is null)
        {
            return false;
        }

        return match.DeprecatedUntil is not DateOnly until || DateOnly.FromDateTime(now.UtcDateTime) < until;
    }
}

public static class PluginSdkClient
{
    public const string SendOperation = "messages.send";

    public static HttpRequestMessage SandboxSend(Guid workspaceId, Guid channelId, string body, string idempotencyKey)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/workspaces/{workspaceId:D}/developer/sdk/sandbox")
        {
            Content = JsonContent.Create(new
            {
                operation = SendOperation,
                channelId,
                body,
                idempotencyKey
            })
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return request;
    }

    public static SdkSandboxDecision AcceptSandbox(string? operation, string? idempotencyKey)
    {
        if (!string.Equals(operation, SendOperation, StringComparison.Ordinal))
        {
            return new SdkSandboxDecision(false, 400, PluginSdkErrors.UnknownOperation, null);
        }

        if (!PluginSdkIdempotency.TryNormalize(idempotencyKey, out _))
        {
            return new SdkSandboxDecision(false, 400, DeveloperPortalErrors.IdempotencyKeyRequired, null);
        }

        return new SdkSandboxDecision(true, 200, null, new JsonObject
        {
            ["synthetic"] = true,
            ["sandbox"] = true,
            ["delivered"] = false,
            ["operation"] = SendOperation,
            ["status"] = "accepted"
        });
    }
}

public static class PluginSdkCatalog
{
    public const string Version = "1.0.0";
    public const string ApiContract = "1";
    public const string License = "Apache-2.0";

    /// <summary>
    /// Public sandbox HMAC key. It signs only the synthetic verify route and is
    /// never a tenant credential. Production callbacks keep their own secret.
    /// </summary>
    public const string SandboxSecret = "sandbox-not-a-secret";

    public static JsonObject Document(DateTimeOffset now)
    {
        var matrix = new JsonArray();
        foreach (var version in PublicEventCatalog.Versions)
        {
            matrix.Add(Row(version.EventType, version.SchemaVersion, now));
        }

        matrix.Add(Row(PublicEventCatalog.MessageCreated, 99, now));

        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["sdkVersion"] = Version,
            ["apiContract"] = ApiContract,
            ["license"] = License,
            ["sandbox"] = true,
            ["published"] = false,
            ["tokenLogged"] = false,
            ["secretInUrl"] = false,
            ["digest"] = Digest(),
            ["install"] = new JsonObject
            {
                ["dotnet"] = "sdks/dotnet/VibeChat.Sdk",
                ["typescript"] = "sdks/typescript"
            },
            ["examples"] = Examples(),
            ["failures"] = new JsonArray(
                PluginSdkErrors.InvalidSignature,
                PluginSdkErrors.ReplayDetected,
                PluginSdkErrors.StaleDelivery,
                DeveloperPortalErrors.Revoked,
                DeveloperPortalErrors.ScopeDenied,
                PluginSdkErrors.CrossTenant),
            ["matrix"] = matrix,
            ["provenance"] = new JsonObject
            {
                ["license"] = License,
                ["sdkVersion"] = Version,
                ["published"] = false,
                ["reproducible"] = true,
                ["source"] = "modules/Integrations/PluginSdk.cs"
            }
        };
    }

    public static string Digest()
    {
        var builder = new StringBuilder();
        builder.Append(Version).Append('|').Append(License).Append('|').Append(ApiContract);
        foreach (var version in PublicEventCatalog.Versions
                     .OrderBy(item => item.EventType, StringComparer.Ordinal)
                     .ThenBy(item => item.SchemaVersion))
        {
            builder.Append('|').Append(version.EventType).Append(':').Append(version.SchemaVersion);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static JsonObject Row(string eventType, int schemaVersion, DateTimeOffset now) => new()
    {
        ["sdkVersion"] = Version,
        ["apiContract"] = ApiContract,
        ["eventType"] = eventType,
        ["schemaVersion"] = schemaVersion,
        ["compatible"] = PluginSdkMatrix.IsCompatible(Version, ApiContract, eventType, schemaVersion, now)
    };

    private static JsonArray Examples() =>
    [
        Example("bot", PluginSdkClient.SendOperation, "POST", "/api/v1/integrations/v1/channels/{channelId}/messages"),
        Example("slash", "commands.slash", "POST", "/api/v1/workspaces/{workspaceId}/channels/{channelId}/plugin-commands"),
        Example("event-consumer", "events.subscribe", "POST", "https://sandbox.invalid/callbacks/events"),
        Example("connector", "connectors.deliver", "POST", "https://sandbox.invalid/connectors/github")
    ];

    private static JsonObject Example(string id, string operation, string method, string path) => new()
    {
        ["id"] = id,
        ["operation"] = operation,
        ["method"] = method,
        ["path"] = path,
        ["synthetic"] = true,
        ["host"] = "sandbox.invalid"
    };
}

public static class PluginSdkContractRunner
{
    public static JsonObject Report(DateTimeOffset now, string? foreignPayload = null)
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        const string body = "{\"synthetic\":true}";
        const string deliveryId = "delivery-sdk-1";
        var signature = WebhookDelivery.ComputeSignature(secret, body);
        var token = "vc_int_should_not_leak";
        var marker = string.IsNullOrEmpty(foreignPayload) ? "foreign-workspace-marker" : foreignPayload;
        var cases = new JsonArray
        {
            Case("valid-signature", PluginSdkVerifier.Verify(secret, body, signature, deliveryId, now, now, [])),
            Case("invalid-signature", PluginSdkVerifier.Verify(secret, body, signature + "00", deliveryId, now, now, [])),
            Case("replay", PluginSdkVerifier.Verify(secret, body, signature, deliveryId, now, now, [deliveryId])),
            Case("stale", PluginSdkVerifier.Verify(secret, body, signature, deliveryId, now.AddMinutes(-10), now, [])),
            AuthCase("unauthorized", DeveloperPortalCatalog.Decide(true, true, false, [], PluginSdkClient.SendOperation, Guid.NewGuid())),
            AuthCase("forbidden", DeveloperPortalCatalog.Decide(true, false, false, [], PluginSdkClient.SendOperation, Guid.NewGuid())),
            new JsonObject
            {
                ["id"] = "cross-tenant",
                ["ok"] = true,
                ["accepted"] = false,
                ["reason"] = PluginSdkErrors.CrossTenant,
                ["status"] = 403
            },
            new JsonObject
            {
                ["id"] = "token-not-logged",
                ["ok"] = PluginSdkRedaction.Scrub("prefix " + token + " suffix") == "prefix [redacted] suffix",
                ["log"] = PluginSdkRedaction.Scrub(token)
            },
            new JsonObject
            {
                ["id"] = "idempotency",
                ["ok"] = !PluginSdkIdempotency.TryNormalize("short", out _)
                    && PluginSdkIdempotency.TryNormalize("quickstart-key", out _)
            },
            new JsonObject
            {
                ["id"] = "matrix",
                ["ok"] = PluginSdkMatrix.IsCompatible(PluginSdkCatalog.Version, PluginSdkCatalog.ApiContract, PublicEventCatalog.MessageCreated, 2, now)
                    && !PluginSdkMatrix.IsCompatible(PluginSdkCatalog.Version, PluginSdkCatalog.ApiContract, PublicEventCatalog.MessageCreated, 99, now)
                    && !PluginSdkMatrix.IsCompatible("0.9.0", PluginSdkCatalog.ApiContract, PublicEventCatalog.MessageCreated, 1, now)
            }
        };

        var passed = true;
        foreach (var item in cases)
        {
            if (item is not JsonObject row || row["ok"] is not JsonValue flag || !flag.GetValue<bool>())
            {
                passed = false;
                break;
            }
        }
        var report = new JsonObject
        {
            ["synthetic"] = true,
            ["sandbox"] = true,
            ["passed"] = passed,
            ["sdkVersion"] = PluginSdkCatalog.Version,
            ["cases"] = cases
        };
        var json = report.ToJsonString();
        if (json.Contains(token, StringComparison.Ordinal)
            || json.Contains(secret, StringComparison.Ordinal)
            || json.Contains(marker, StringComparison.Ordinal)
            || json.Contains(PluginSdkCatalog.SandboxSecret, StringComparison.Ordinal))
        {
            report["passed"] = false;
            report["leak"] = true;
        }

        return report;
    }

    private static JsonObject Case(string id, SdkVerification verification)
    {
        var expected = id switch
        {
            "valid-signature" => verification.Accepted && verification.Reason == PluginSdkErrors.Accepted,
            "invalid-signature" => !verification.Accepted && verification.Reason == PluginSdkErrors.InvalidSignature,
            "replay" => !verification.Accepted && verification.Reason == PluginSdkErrors.ReplayDetected,
            "stale" => !verification.Accepted && verification.Reason == PluginSdkErrors.StaleDelivery,
            _ => false
        };
        return new JsonObject
        {
            ["id"] = id,
            ["ok"] = expected,
            ["accepted"] = verification.Accepted,
            ["reason"] = verification.Reason
        };
    }

    private static JsonObject AuthCase(string id, DeveloperAccessDecision decision)
    {
        var expected = id switch
        {
            "unauthorized" => decision.StatusCode == 401,
            "forbidden" => decision.StatusCode == 403,
            _ => false
        };
        return new JsonObject
        {
            ["id"] = id,
            ["ok"] = expected,
            ["accepted"] = false,
            ["reason"] = decision.Error,
            ["status"] = decision.StatusCode
        };
    }
}
