using System.Text.Json.Nodes;
using VibeChat.BuildingBlocks;

namespace VibeChat.Integrations;

/// <summary>
/// Developer portal catalog (B-135). Documentation is derived from the canonical
/// integration surface. Sandbox calls are synthetic and never leave this process.
/// </summary>
public sealed class DeveloperPortalOptions
{
    public const string SectionName = "Integrations:DeveloperPortal";

    /// <summary>Off by default. Portal routes return 404 until an operator turns it on.</summary>
    public bool Enabled { get; set; }
}

public static class DeveloperPortalErrors
{
    public const string Disabled = "DeveloperPortalDisabled";
    public const string ScopeDenied = "ExplorerScopeDenied";
    public const string NotFound = "CredentialNotFound";
    public const string Revoked = "CredentialRevoked";
    public const string IdempotencyKeyRequired = "IdempotencyKeyRequired";
    public const string IdempotencyConflict = "IdempotencyConflict";
    public const string InvalidChannelScope = "InvalidChannelScope";
    public const string InvalidName = "InvalidBotName";
    public const string UnknownOperation = "UnknownDeveloperOperation";
}

public static class DeveloperPortalCatalog
{
    public const string SendOperation = "messages.send";
    public const string DirectOperation = "messages.dm";
    public const string SendChannelPath = "/api/v1/integrations/v1/channels/{channelId:guid}/messages";
    public const string SendDirectPath = "/api/v1/integrations/v1/dms";
    public const string TokenHeader = "X-VibeChat-Integration-Token";

    public static readonly IReadOnlyList<string> RuntimePaths = [SendChannelPath, SendDirectPath];

    public static JsonObject Catalog(bool liveSendEnabled) => new()
    {
        ["schemaVersion"] = 1,
        ["sandbox"] = true,
        ["liveSendEnabled"] = liveSendEnabled,
        ["secretInUrl"] = false,
        ["tokenHeader"] = TokenHeader,
        ["capabilities"] = new JsonArray(PluginManifestRules.CapabilityMessagesSend),
        ["operations"] = Operations(),
        ["rateLimit"] = RateLimit(RateLimitPolicies.DefaultSendPerMinute),
        ["changelog"] = Changelog(),
        ["events"] = EventIndex(),
        ["examples"] = Examples()
    };

    public static JsonObject OpenApiDocument() => new()
    {
        ["openapi"] = "3.0.3",
        ["info"] = new JsonObject
        {
            ["title"] = "VibeChat Integration API",
            ["version"] = "1"
        },
        ["paths"] = new JsonObject
        {
            [SendChannelPath] = OperationObject(SendOperation, "post"),
            [SendDirectPath] = OperationObject(DirectOperation, "post")
        },
        ["components"] = new JsonObject
        {
            ["securitySchemes"] = new JsonObject
            {
                ["integrationToken"] = new JsonObject
                {
                    ["type"] = "apiKey",
                    ["in"] = "header",
                    ["name"] = TokenHeader
                }
            }
        }
    };

    public static JsonObject ContractKit(bool liveSendEnabled)
    {
        var events = new JsonArray();
        foreach (var version in PublicEventCatalog.Versions)
        {
            events.Add(new JsonObject
            {
                ["eventType"] = version.EventType,
                ["schemaVersion"] = version.SchemaVersion,
                ["wireName"] = version.WireName,
                ["status"] = version.Status.ToString().ToLowerInvariant(),
                ["deprecatedUntil"] = version.DeprecatedUntil?.ToString("yyyy-MM-dd"),
                ["changelog"] = version.Changelog,
                ["schema"] = version.Schema.DeepClone(),
                ["example"] = version.Example.DeepClone()
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["sandbox"] = true,
            ["openapi"] = OpenApiDocument(),
            ["events"] = events,
            ["capabilities"] = new JsonArray(PluginManifestRules.CapabilityMessagesSend),
            ["examples"] = Examples(),
            ["changelog"] = Changelog(),
            ["rateLimit"] = RateLimit(RateLimitPolicies.DefaultSendPerMinute),
            ["liveSendEnabled"] = liveSendEnabled
        };
    }

    public static DeveloperAccessDecision Decide(
        bool credentialEnabled,
        bool revoked,
        bool allowDms,
        IReadOnlyCollection<Guid> channelIds,
        string? operation,
        Guid? channelId)
    {
        if (!credentialEnabled || revoked)
        {
            return DeveloperAccessDecision.Deny(401, DeveloperPortalErrors.Revoked);
        }

        if (string.Equals(operation, SendOperation, StringComparison.Ordinal))
        {
            if (channelId is null || channelId == Guid.Empty || !channelIds.Contains(channelId.Value))
            {
                return DeveloperAccessDecision.Deny(403, DeveloperPortalErrors.ScopeDenied);
            }

            return DeveloperAccessDecision.Allow(SendOperation);
        }

        if (string.Equals(operation, DirectOperation, StringComparison.Ordinal))
        {
            return allowDms
                ? DeveloperAccessDecision.Allow(DirectOperation)
                : DeveloperAccessDecision.Deny(403, DeveloperPortalErrors.ScopeDenied);
        }

        return DeveloperAccessDecision.Deny(400, DeveloperPortalErrors.UnknownOperation);
    }

    public static JsonObject SyntheticCall(Guid credentialId, string operation) => new()
    {
        ["synthetic"] = true,
        ["sandbox"] = true,
        ["delivered"] = false,
        ["operation"] = operation,
        ["credentialId"] = credentialId,
        ["status"] = "accepted"
    };

    public static JsonObject SyntheticWebhook()
    {
        var version = PublicEventCatalog.Versions.First(item =>
            item.EventType == PublicEventCatalog.MessageCreated && item.SchemaVersion == 1);
        return new JsonObject
        {
            ["synthetic"] = true,
            ["sandbox"] = true,
            ["delivered"] = false,
            ["eventType"] = version.EventType,
            ["schemaVersion"] = version.SchemaVersion,
            ["payload"] = version.Example.DeepClone()
        };
    }

    public static bool ContainsSecret(string json) =>
        json.Contains("vc_int_", StringComparison.Ordinal);

    private static JsonArray Operations() =>
    [
        new JsonObject
        {
            ["id"] = SendOperation,
            ["method"] = "POST",
            ["path"] = SendChannelPath,
            ["scope"] = "channel"
        },
        new JsonObject
        {
            ["id"] = DirectOperation,
            ["method"] = "POST",
            ["path"] = SendDirectPath,
            ["scope"] = "allowDms"
        }
    ];

    private static JsonObject OperationObject(string operationId, string method) => new()
    {
        [method] = new JsonObject
        {
            ["operationId"] = operationId,
            ["x-sandbox"] = true,
            ["security"] = new JsonArray(new JsonObject { ["integrationToken"] = new JsonArray() })
        }
    };

    private static JsonObject RateLimit(int sendPerMinute) => new()
    {
        ["sendPerMinute"] = sendPerMinute,
        ["window"] = "1m",
        ["scope"] = "credential"
    };

    private static JsonArray Changelog() =>
    [
        new JsonObject
        {
            ["id"] = "integration-http-v1",
            ["status"] = "current",
            ["retiresOn"] = null
        },
        new JsonObject
        {
            ["id"] = "messaging.message.created.v1",
            ["status"] = "deprecated",
            ["retiresOn"] = PublicEventCatalog.MessageCreatedV1RetiresOn.ToString("yyyy-MM-dd")
        }
    ];

    private static JsonArray EventIndex()
    {
        var events = new JsonArray();
        foreach (var version in PublicEventCatalog.Versions)
        {
            events.Add(new JsonObject
            {
                ["eventType"] = version.EventType,
                ["schemaVersion"] = version.SchemaVersion,
                ["status"] = version.Status.ToString().ToLowerInvariant()
            });
        }

        return events;
    }

    private static JsonArray Examples() =>
    [
        new JsonObject
        {
            ["operation"] = SendOperation,
            ["synthetic"] = true,
            ["body"] = new JsonObject
            {
                ["body"] = "hello from the sandbox",
                ["idempotencyKey"] = "example-key"
            }
        },
        new JsonObject
        {
            ["operation"] = DirectOperation,
            ["synthetic"] = true,
            ["body"] = new JsonObject
            {
                ["peerUserId"] = "00000000-0000-0000-0000-000000000000",
                ["body"] = "hello from the sandbox",
                ["idempotencyKey"] = "example-key"
            }
        }
    ];
}

public sealed record DeveloperAccessDecision(bool Allowed, int StatusCode, string? Error, string? Operation)
{
    public static DeveloperAccessDecision Allow(string operation) => new(true, 200, null, operation);

    public static DeveloperAccessDecision Deny(int statusCode, string error) => new(false, statusCode, error, null);
}

public sealed record DeveloperCredentialView(
    Guid Id,
    string Name,
    bool Enabled,
    bool AllowDms,
    Guid[] ChannelIds,
    bool TokenConfigured,
    string? Last4,
    DateTimeOffset? LastUsedAt,
    bool Revoked,
    DateTimeOffset CreatedAt);
