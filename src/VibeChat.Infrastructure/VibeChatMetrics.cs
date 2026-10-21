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

public static class VibeChatMetrics
{
    public const string MeterName = "VibeChat";
    public static readonly Meter Meter = new(MeterName, "1.0.0");
    public static readonly Counter<long> MessagesSent = Meter.CreateCounter<long>("vibechat.messages.sent");
    public static readonly Counter<long> MessagesRejected = Meter.CreateCounter<long>("vibechat.messages.rejected");
    public static readonly Counter<long> TopicsRejected = Meter.CreateCounter<long>("vibechat.topics.rejected");
    public static readonly Counter<long> DigestsGenerated = Meter.CreateCounter<long>("vibechat.digests.generated");
    public static readonly Counter<long> FormsPublished = Meter.CreateCounter<long>("vibechat.forms.published");
    public static readonly Counter<long> FormsSubmitted = Meter.CreateCounter<long>("vibechat.forms.submitted");
    public static readonly Counter<long> FormsDecided = Meter.CreateCounter<long>("vibechat.forms.decided");
    public static readonly Counter<long> AutomationsPublished = Meter.CreateCounter<long>("vibechat.automations.published");
    public static readonly Counter<long> AutomationRuns = Meter.CreateCounter<long>("vibechat.automations.runs");
    public static readonly Counter<long> AutomationStopped = Meter.CreateCounter<long>("vibechat.automations.stopped");
    public static readonly Counter<long> IncidentsDeclared = Meter.CreateCounter<long>("vibechat.incidents.declared");
    public static readonly Counter<long> IncidentsResolved = Meter.CreateCounter<long>("vibechat.incidents.resolved");
    public static readonly Counter<long> IncidentAutomationFailed = Meter.CreateCounter<long>("vibechat.incidents.automation_failed");
    public static readonly Counter<long> ConnectorInbound = Meter.CreateCounter<long>("vibechat.connectors.inbound");
    public static readonly Counter<long> ConnectorOutboundFailed = Meter.CreateCounter<long>("vibechat.connectors.outbound_failed");
    public static readonly Counter<long> ConnectorRevoked = Meter.CreateCounter<long>("vibechat.connectors.revoked");
    public static readonly Counter<long> SsoLinkCreated = Meter.CreateCounter<long>("vibechat.sso.link_created");
    public static readonly Counter<long> SsoLinkDenied = Meter.CreateCounter<long>("vibechat.sso.link_denied");
    public static readonly Counter<long> ScimProvisioned = Meter.CreateCounter<long>("vibechat.scim.provisioned");
    public static readonly Counter<long> AttachmentScans = Meter.CreateCounter<long>("vibechat.files.scan");
    public static readonly Counter<long> EventSchemaRejected = Meter.CreateCounter<long>("vibechat.events.schema.rejected");
    public static readonly Counter<long> QuotaConsumed = Meter.CreateCounter<long>("vibechat.quota.consumed");
    public static readonly Counter<long> QuotaDenied = Meter.CreateCounter<long>("vibechat.quota.denied");
    public static readonly Counter<long> QuotaCheckFailed = Meter.CreateCounter<long>("vibechat.quota.check_failed");
    public static readonly Counter<long> BreakGlassOpened = Meter.CreateCounter<long>("vibechat.delegation.break_glass");
    public static readonly UpDownCounter<long> AttachmentScanQueue = Meter.CreateUpDownCounter<long>("vibechat.files.scan.queue");
    public static readonly UpDownCounter<long> RealtimeConnections = Meter.CreateUpDownCounter<long>("vibechat.realtime.connections");
    private static long _realtimeConnectionsGauge;
    public static long RealtimeConnectionsGauge => Interlocked.Read(ref _realtimeConnectionsGauge);

    public static void AdjustRealtimeConnections(long delta)
    {
        RealtimeConnections.Add(delta);
        Interlocked.Add(ref _realtimeConnectionsGauge, delta);
    }
}
