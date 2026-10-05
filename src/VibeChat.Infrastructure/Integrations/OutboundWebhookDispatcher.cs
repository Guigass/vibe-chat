using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Minio;
using StackExchange.Redis;
using VibeChat.Administration;
using VibeChat.AI;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Directory;
using VibeChat.Files;
using VibeChat.Identity;
using VibeChat.Integrations;
using VibeChat.Messaging;
using VibeChat.Notifications;
using VibeChat.Realtime;
using NpgsqlTypes;
using VibeChat.Search;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using Role = VibeChat.SharedKernel.Role;

namespace VibeChat.Infrastructure;

/// <summary>HTTP POST outbound webhooks with HMAC-SHA256 (B-048 / B-108 / ADR-026). Failures are logged, never thrown.</summary>
public sealed class OutboundWebhookDispatcher(
    VibeChatDbContext dbContext,
    WebhookEndpointResolver webhookResolver,
    IHttpClientFactory httpClientFactory,
    IClock clock,
    ILogger<OutboundWebhookDispatcher> logger) : IOutboundWebhookDispatcher
{
    public const string HttpClientName = "OutboundWebhooks";

    public async Task TryDispatchAsync(
        TenantId tenantId,
        string eventName,
        Guid deliveryId,
        string payloadJson,
        Guid channelId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<EffectiveWebhookEndpoint> endpoints;
        try
        {
            endpoints = await webhookResolver.ListAsync(tenantId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Outbound webhook lookup failed for tenant {TenantId}", tenantId.Value);
            return;
        }

        foreach (var endpoint in endpoints)
        {
            if (!endpoint.Enabled
                || !WebhookDelivery.MatchesSubscription(
                    endpoint.SubscribedEvents,
                    endpoint.ChannelFilter,
                    eventName,
                    channelId))
            {
                continue;
            }

            await DeliverOneAsync(
                tenantId,
                endpoint,
                eventName,
                deliveryId,
                payloadJson,
                persistImmediately: false,
                cancellationToken);
        }
    }

    public async Task<WebhookPingResult> PingAsync(
        TenantId tenantId,
        Guid endpointId,
        CancellationToken cancellationToken)
    {
        var endpoints = await webhookResolver.ListAsync(tenantId, cancellationToken);
        var endpoint = endpoints.FirstOrDefault(x => x.Id == endpointId);
        if (endpoint is null)
        {
            return new WebhookPingResult(false, 404, "WebhookEndpointNotFound", null, null);
        }

        if (!SecretMasking.IsConfigured(endpoint.Url)
            || !SecretMasking.IsConfigured(endpoint.SigningSecret)
            || !WebhookDelivery.IsValidHttpsUrl(endpoint.Url))
        {
            return new WebhookPingResult(false, 400, "WebhookIncomplete", null, null);
        }

        var sentAt = clock.UtcNow;
        var payload = JsonSerializer.Serialize(new
        {
            type = WebhookEventTypes.WebhookTest,
            tenantId = tenantId.Value,
            endpointId,
            sentAt
        });
        var (httpStatus, error) = await DeliverOneAsync(
            tenantId,
            endpoint,
            WebhookEventTypes.WebhookTest,
            Guid.NewGuid(),
            payload,
            persistImmediately: true,
            cancellationToken);
        var ok = httpStatus is >= 200 and < 300;
        return new WebhookPingResult(ok, ok ? 200 : 502, ok ? null : "WebhookDeliveryFailed", httpStatus, error);
    }

    private async Task<(int? HttpStatus, string? Error)> DeliverOneAsync(
        TenantId tenantId,
        EffectiveWebhookEndpoint endpoint,
        string eventName,
        Guid deliveryId,
        string payloadJson,
        bool persistImmediately,
        CancellationToken cancellationToken)
    {
        int? httpStatus = null;
        string? error = null;
        try
        {
            if (!SecretMasking.IsConfigured(endpoint.Url)
                || !SecretMasking.IsConfigured(endpoint.SigningSecret)
                || !WebhookDelivery.IsValidHttpsUrl(endpoint.Url))
            {
                error = "WebhookIncomplete";
            }
            else
            {
                var client = httpClientFactory.CreateClient(HttpClientName);
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url.Trim())
                {
                    Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
                };
                request.Headers.TryAddWithoutValidation(WebhookDelivery.EventHeader, eventName);
                request.Headers.TryAddWithoutValidation(WebhookDelivery.DeliveryIdHeader, deliveryId.ToString("D"));
                request.Headers.TryAddWithoutValidation(
                    WebhookDelivery.SignatureHeader,
                    WebhookDelivery.ComputeSignature(endpoint.SigningSecret!.Trim(), payloadJson));

                using var response = await client.SendAsync(request, cancellationToken);
                httpStatus = (int)response.StatusCode;
                if (!response.IsSuccessStatusCode)
                {
                    error = $"HTTP {httpStatus}";
                    logger.LogWarning(
                        "Outbound webhook delivery {DeliveryId} for tenant {TenantId} returned {StatusCode}",
                        deliveryId,
                        tenantId.Value,
                        httpStatus);
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name;
            logger.LogWarning(
                ex,
                "Outbound webhook delivery {DeliveryId} for tenant {TenantId} failed",
                deliveryId,
                tenantId.Value);
        }

        try
        {
            var row = await dbContext.OutboundWebhookEndpoints
                .FirstOrDefaultAsync(x => x.Id == endpoint.Id && x.TenantId == tenantId, cancellationToken);
            if (row is not null)
            {
                row.LastDeliveryAt = clock.UtcNow;
                row.LastStatusCode = httpStatus;
                row.LastError = error is null
                    ? null
                    : error.Length > WebhookPolicies.MaxLastErrorLength
                        ? error[..WebhookPolicies.MaxLastErrorLength]
                        : error;
                if (persistImmediately)
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to record webhook delivery status for endpoint {EndpointId}", endpoint.Id);
        }

        return (httpStatus, error);
    }
}
