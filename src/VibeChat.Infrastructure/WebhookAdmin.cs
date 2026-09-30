using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.Conversations;
using VibeChat.Integrations;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Infrastructure;

public sealed record WebhookWriteResult(bool Ok, int StatusCode, string? Error, string? Message, object? Body);

public sealed class WebhookAdminService(
    VibeChatDbContext db,
    RuntimeSecretProtector protector,
    IOptions<RuntimeSettingsOptions> runtimeOptions,
    IOutboundWebhookDispatcher dispatcher,
    IAuditWriter audit,
    IClock clock)
{
    private readonly RuntimeSettingsOptions _runtime = runtimeOptions.Value;

    public async Task<WebhookWriteResult> CreateAsync(
        Workspace workspace,
        UserId actorUserId,
        string? name,
        string? url,
        bool enabled,
        IReadOnlyList<string>? subscribedEvents,
        IReadOnlyList<Guid>? channelFilter,
        string? secret,
        CancellationToken ct)
    {
        var gate = RequireEncryption();
        if (gate is not null)
        {
            return gate;
        }

        var count = await db.OutboundWebhookEndpoints.CountAsync(x => x.TenantId == workspace.TenantId, ct);
        if (count >= WebhookPolicies.MaxEndpointsPerTenant)
        {
            return Fail(409, "WebhookEndpointLimit",
                $"At most {WebhookPolicies.MaxEndpointsPerTenant} webhook endpoints per tenant.");
        }

        var parsed = await ParseAsync(workspace.TenantId, name, url, subscribedEvents, channelFilter, defaultEvents: true, ct);
        if (parsed.Error is not null)
        {
            return parsed.Error;
        }

        if (!string.IsNullOrWhiteSpace(secret))
        {
            var secretError = ValidateSecret(secret);
            if (secretError is not null)
            {
                return secretError;
            }
        }

        var now = clock.UtcNow;
        var row = new OutboundWebhookEndpoint
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            Name = parsed.Name,
            Enabled = false,
            Url = parsed.Url,
            Secret = null,
            SubscribedEvents = parsed.Events,
            ChannelFilter = parsed.Channels,
            UpdatedAt = now
        };

        if (!string.IsNullOrWhiteSpace(secret))
        {
            try
            {
                row.SigningSecret.CopyFrom(Protect(secret.Trim(), workspace.TenantId, now));
            }
            catch (CryptographicException)
            {
                return Fail(503, "RuntimeSecretEncryptionUnavailable", "Encryption keyring is not configured.");
            }
        }

        var secretConfigured = row.SigningSecret.IsPresent;
        if (enabled)
        {
            if (!SecretMasking.IsConfigured(row.Url) || !secretConfigured)
            {
                return Fail(400, "WebhookIncomplete", "Enable requires a valid URL and signing secret.");
            }

            row.Enabled = true;
        }

        db.OutboundWebhookEndpoints.Add(row);
        AddAudit(workspace, actorUserId, "webhooks.created", row.Id, new { name = row.Name, enabled = row.Enabled });
        await db.SaveChangesAsync(ct);
        return new WebhookWriteResult(true, 201, null, null, await PublicViewAsync(workspace.TenantId, row.Id, ct));
    }

    public async Task<WebhookWriteResult> UpdateAsync(
        Workspace workspace,
        UserId actorUserId,
        Guid endpointId,
        string? name,
        string? url,
        bool? enabled,
        IReadOnlyList<string>? subscribedEvents,
        IReadOnlyList<Guid>? channelFilter,
        CancellationToken ct)
    {
        var row = await FindAsync(workspace.TenantId, endpointId, ct);
        if (row is null)
        {
            return Fail(404, "WebhookEndpointNotFound", "Webhook endpoint was not found.");
        }

        var parsed = await ParseAsync(
            workspace.TenantId,
            name ?? row.Name,
            url ?? row.Url,
            subscribedEvents ?? row.SubscribedEvents,
            channelFilter ?? row.ChannelFilter,
            defaultEvents: false,
            ct);
        if (parsed.Error is not null)
        {
            return parsed.Error;
        }

        row.Name = parsed.Name;
        row.Url = parsed.Url;
        row.SubscribedEvents = parsed.Events;
        row.ChannelFilter = parsed.Channels;

        if (enabled is bool nextEnabled && nextEnabled != row.Enabled)
        {
            if (nextEnabled)
            {
                var secretConfigured = row.SigningSecret.IsPresent || SecretMasking.IsConfigured(row.Secret);
                if (!SecretMasking.IsConfigured(row.Url) || !secretConfigured)
                {
                    return Fail(400, "WebhookIncomplete", "Enable requires a valid URL and signing secret.");
                }
            }

            row.Enabled = nextEnabled;
        }

        row.UpdatedAt = clock.UtcNow;
        AddAudit(workspace, actorUserId, "webhooks.updated", row.Id, new { name = row.Name, enabled = row.Enabled });
        await db.SaveChangesAsync(ct);
        return new WebhookWriteResult(true, 200, null, null, await PublicViewAsync(workspace.TenantId, row.Id, ct));
    }

    public async Task<WebhookWriteResult> DeleteAsync(
        Workspace workspace,
        UserId actorUserId,
        Guid endpointId,
        CancellationToken ct)
    {
        var row = await FindAsync(workspace.TenantId, endpointId, ct);
        if (row is null)
        {
            return Fail(404, "WebhookEndpointNotFound", "Webhook endpoint was not found.");
        }

        db.OutboundWebhookEndpoints.Remove(row);
        AddAudit(workspace, actorUserId, "webhooks.deleted", row.Id, new { name = row.Name });
        await db.SaveChangesAsync(ct);
        return new WebhookWriteResult(true, 204, null, null, null);
    }

    public async Task<WebhookWriteResult> RotateAsync(
        Workspace workspace,
        UserId actorUserId,
        Guid endpointId,
        string secret,
        CancellationToken ct)
    {
        var gate = RequireEncryption();
        if (gate is not null)
        {
            return gate;
        }

        var secretError = ValidateSecret(secret);
        if (secretError is not null)
        {
            return secretError;
        }

        var row = await FindAsync(workspace.TenantId, endpointId, ct);
        if (row is null)
        {
            return Fail(404, "WebhookEndpointNotFound", "Webhook endpoint was not found.");
        }

        var now = clock.UtcNow;
        EncryptedSecretEnvelope envelope;
        try
        {
            envelope = Protect(secret.Trim(), workspace.TenantId, now);
        }
        catch (CryptographicException)
        {
            return Fail(503, "RuntimeSecretEncryptionUnavailable", "Encryption keyring is not configured.");
        }

        row.SigningSecret.CopyFrom(envelope);
        row.Secret = null;
        row.UpdatedAt = now;
        audit.Add(new AuditEvent
        {
            TenantId = workspace.TenantId,
            ActorUserId = actorUserId,
            Action = AuditActions.SettingsCredentialRotate,
            EntityType = "OutboundWebhookEndpoint",
            EntityId = row.Id.ToString("D"),
            MetadataJson = JsonSerializer.Serialize(new
            {
                workspaceId = workspace.Id.Value,
                kind = RuntimeSecretKinds.WebhookSigningSecret,
                keyVersion = envelope.KeyVersion
            })
        });
        await db.SaveChangesAsync(ct);
        return new WebhookWriteResult(true, 200, null, null, new
        {
            configured = true,
            mask = SecretMasking.MaskFromSuffix(envelope.MaskSuffix),
            keyVersion = envelope.KeyVersion,
            rotatedAt = envelope.RotatedAt
        });
    }

    public async Task<WebhookWriteResult> PingAsync(
        Workspace workspace,
        Guid endpointId,
        CancellationToken ct)
    {
        var exists = await db.OutboundWebhookEndpoints.AsNoTracking()
            .AnyAsync(x => x.Id == endpointId && x.TenantId == workspace.TenantId, ct);
        if (!exists)
        {
            return Fail(404, "WebhookEndpointNotFound", "Webhook endpoint was not found.");
        }

        var ping = await dispatcher.PingAsync(workspace.TenantId, endpointId, ct);
        if (ping.Error == "WebhookEndpointNotFound")
        {
            return Fail(404, ping.Error, "Webhook endpoint was not found.");
        }

        if (ping.Error == "WebhookIncomplete")
        {
            return Fail(400, ping.Error, "Ping requires a valid URL and signing secret.");
        }

        return new WebhookWriteResult(true, 200, null, null, new
        {
            ok = ping.Ok,
            statusCode = ping.HttpStatus,
            lastError = ping.LastError
        });
    }

    private async Task<object> PublicViewAsync(TenantId tenantId, Guid endpointId, CancellationToken ct)
    {
        var resolver = new WebhookEndpointResolver(
            db,
            protector,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WebhookEndpointResolver>.Instance);
        var row = (await resolver.ListAsync(tenantId, ct)).First(x => x.Id == endpointId);
        return new
        {
            id = row.Id,
            name = row.Name,
            enabled = row.Enabled,
            url = SecretMasking.IsConfigured(row.Url) ? row.Url.Trim() : string.Empty,
            subscribedEvents = row.SubscribedEvents,
            channelFilter = row.ChannelFilter,
            secretConfigured = row.SecretConfigured,
            secretMask = row.SecretMask,
            secretSource = row.SecretSource,
            secretKeyVersion = row.SecretKeyVersion,
            secretRotatedAt = row.SecretRotatedAt,
            lastDeliveryAt = row.LastDeliveryAt,
            lastStatusCode = row.LastStatusCode,
            lastError = row.LastError,
            updatedAt = row.UpdatedAt
        };
    }

    private async Task<OutboundWebhookEndpoint?> FindAsync(TenantId tenantId, Guid endpointId, CancellationToken ct) =>
        await db.OutboundWebhookEndpoints.FirstOrDefaultAsync(x => x.Id == endpointId && x.TenantId == tenantId, ct);

    private async Task<(WebhookWriteResult? Error, string Name, string Url, string[] Events, Guid[] Channels)> ParseAsync(
        TenantId tenantId,
        string? name,
        string? url,
        IReadOnlyList<string>? subscribedEvents,
        IReadOnlyList<Guid>? channelFilter,
        bool defaultEvents,
        CancellationToken ct)
    {
        var trimmedName = (name ?? string.Empty).Trim();
        if (trimmedName.Length is 0 or > WebhookPolicies.MaxNameLength)
        {
            return (Fail(400, "InvalidWebhookName", $"Name must be 1–{WebhookPolicies.MaxNameLength} characters."), "", "", [], []);
        }

        var trimmedUrl = (url ?? string.Empty).Trim();
        if (trimmedUrl.Length > 2048)
        {
            return (Fail(400, "InvalidWebhookUrl", "URL exceeds 2048 characters."), "", "", [], []);
        }

        if (trimmedUrl.Length > 0 && !WebhookDelivery.IsValidHttpsUrl(trimmedUrl))
        {
            return (Fail(400, "InvalidWebhookUrl", "URL must be https (or http://localhost for lab)."), "", "", [], []);
        }

        string[] events;
        if (subscribedEvents is null || subscribedEvents.Count == 0)
        {
            if (!defaultEvents)
            {
                return (Fail(400, "InvalidWebhookEvents", "At least one event is required."), "", "", [], []);
            }

            events = WebhookEventTypes.DefaultSubscribed;
        }
        else
        {
            events = subscribedEvents
                .Select(x => (x ?? string.Empty).Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (events.Length == 0 || events.Any(x => !WebhookEventTypes.IsSubscribable(x)))
            {
                return (Fail(400, "InvalidWebhookEvents",
                    "Events must be a subset of MessageCreated, MessageEdited, MessageDeleted, ReactionChanged."), "", "", [], []);
            }
        }

        var channels = (channelFilter ?? [])
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToArray();
        if (channels.Length > WebhookPolicies.MaxChannelFilter)
        {
            return (Fail(400, "InvalidChannelFilter",
                $"Channel filter accepts at most {WebhookPolicies.MaxChannelFilter} ids."), "", "", [], []);
        }

        foreach (var channelId in channels)
        {
            var id = new ChannelId(channelId);
            var exists = await db.Channels.AsNoTracking().AnyAsync(x => x.Id == id && x.TenantId == tenantId, ct);
            if (!exists)
            {
                return (Fail(400, "InvalidChannelFilter", "Channel filter contains an id outside this tenant."), "", "", [], []);
            }
        }

        return (null, trimmedName, trimmedUrl, events, channels);
    }

    private WebhookWriteResult? RequireEncryption()
    {
        if (!_runtime.DatabaseOverridesEnabled)
        {
            return Fail(503, "RuntimeSettingsDisabled", "RuntimeSettings:DatabaseOverridesEnabled is false.");
        }

        if (!protector.IsEncryptionAvailable)
        {
            return Fail(503, "RuntimeSecretEncryptionUnavailable", "Encryption keyring is not configured.");
        }

        return null;
    }

    private static WebhookWriteResult? ValidateSecret(string secret)
    {
        var trimmed = secret.Trim();
        if (trimmed.Length < WebhookPolicies.MinSecretLength)
        {
            return Fail(400, "InvalidSecret", "Secret must be at least 8 characters.");
        }

        if (trimmed.Length > 512)
        {
            return Fail(400, "InvalidSecret", "Secret exceeds 512 characters.");
        }

        return null;
    }

    private EncryptedSecretEnvelope Protect(string secret, TenantId tenantId, DateTimeOffset now) =>
        protector.Protect(
            secret,
            RuntimeSecretKinds.WebhookSigningSecret,
            tenantId,
            workspaceId: null,
            tenantId.Value.ToString("D"),
            now);

    private void AddAudit(Workspace workspace, UserId actorUserId, string change, Guid endpointId, object metadata)
    {
        audit.Add(new AuditEvent
        {
            TenantId = workspace.TenantId,
            ActorUserId = actorUserId,
            Action = AuditActions.SettingsChange,
            EntityType = "OutboundWebhookEndpoint",
            EntityId = endpointId.ToString("D"),
            MetadataJson = JsonSerializer.Serialize(new
            {
                workspaceId = workspace.Id.Value,
                change,
                metadata
            })
        });
    }

    private static WebhookWriteResult Fail(int status, string error, string message) =>
        new(false, status, error, message, null);
}
