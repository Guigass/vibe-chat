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
    public static readonly UpDownCounter<long> RealtimeConnections = Meter.CreateUpDownCounter<long>("vibechat.realtime.connections");
    private static long _realtimeConnectionsGauge;
    public static long RealtimeConnectionsGauge => Interlocked.Read(ref _realtimeConnectionsGauge);

    public static void AdjustRealtimeConnections(long delta)
    {
        RealtimeConnections.Add(delta);
        Interlocked.Add(ref _realtimeConnectionsGauge, delta);
    }
}
