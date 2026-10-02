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

public sealed class ChatHub(
    ITypingService typing,
    IPresenceService presence,
    IChannelMembershipReader channels,
    IWorkspaceMembershipReader workspaces,
    IPermissionChecker permissions,
    IRateLimiter rateLimiter,
    RateLimitSettingsResolver rateLimits,
    ITenantContext tenantContext,
    VibeChatDbContext dbContext) : Hub
{
    public static string ChannelGroup(TenantId tenantId, ChannelId channelId) =>
        $"t:{tenantId.Value}:c:{channelId.Value}";

    public static string UserGroup(TenantId tenantId, UserId userId) =>
        $"t:{tenantId.Value}:u:{userId.Value}";

    public static string TenantGroup(TenantId tenantId) => $"t:{tenantId.Value}";

    public override async Task OnConnectedAsync()
    {
        VibeChatMetrics.AdjustRealtimeConnections(1);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        VibeChatMetrics.AdjustRealtimeConnections(-1);
        if (Context.Items.TryGetValue("tenantId", out var tenantObj)
            && tenantObj is Guid tenantGuid
            && !CurrentUserId().Equals(UserId.Empty))
        {
            var tenant = new TenantId(tenantGuid);
            var userId = CurrentUserId();
            await presence.SetOfflineAsync(tenant, userId, Context.ConnectionId, Context.ConnectionAborted);
            await Clients.Group(TenantGroup(tenant)).SendAsync(
                "PresenceChanged",
                new { tenantId = tenant.Value, userId = userId.Value, status = PresenceStatus.Offline.ToString().ToLowerInvariant() },
                Context.ConnectionAborted);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public Task JoinChannel(Guid tenantId, Guid channelId) =>
        WithRlsAsync(new TenantId(tenantId), CurrentUserId(), async (tenant, userId) =>
        {
            var channel = new ChannelId(channelId);
            await EnsureHubRateLimitAsync(tenant, userId);
            if (!await channels.CanAccessAsync(tenant, channel, userId, Context.ConnectionAborted))
            {
                throw new HubException("Not authorized for channel.");
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, ChannelGroup(tenant, channel), Context.ConnectionAborted);
            await EnsurePresenceGroupAsync(tenant, userId);
            await presence.HeartbeatAsync(tenant, userId, Context.ConnectionId, Context.ConnectionAborted);
            await BroadcastPresenceAsync(tenant, userId, PresenceStatus.Online);
        });

    public async Task LeaveChannel(Guid tenantId, Guid channelId)
    {
        var tenant = new TenantId(tenantId);
        var channel = new ChannelId(channelId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, ChannelGroup(tenant, channel), Context.ConnectionAborted);
    }

    public Task Heartbeat(Guid tenantId) =>
        WithRlsAsync(new TenantId(tenantId), CurrentUserId(), async (tenant, userId) =>
        {
            await EnsureHubRateLimitAsync(tenant, userId);
            await EnsureTenantMembershipAsync(tenant, userId);
            await EnsurePresenceGroupAsync(tenant, userId);
            await presence.HeartbeatAsync(tenant, userId, Context.ConnectionId, Context.ConnectionAborted);
            await BroadcastPresenceAsync(tenant, userId, PresenceStatus.Online);
        });

    public Task SetAway(Guid tenantId) =>
        WithRlsAsync(new TenantId(tenantId), CurrentUserId(), async (tenant, userId) =>
        {
            await EnsureHubRateLimitAsync(tenant, userId);
            await EnsureTenantMembershipAsync(tenant, userId);
            await EnsurePresenceGroupAsync(tenant, userId);
            await presence.SetAwayAsync(tenant, userId, Context.ConnectionId, Context.ConnectionAborted);
            await BroadcastPresenceAsync(tenant, userId, PresenceStatus.Away);
        });

    public Task SendTyping(Guid tenantId, Guid channelId, string displayName) =>
        WithRlsAsync(new TenantId(tenantId), CurrentUserId(), async (tenant, userId) =>
        {
            var channel = new ChannelId(channelId);
            await EnsureHubRateLimitAsync(tenant, userId);
            if (!await channels.CanAccessAsync(tenant, channel, userId, Context.ConnectionAborted))
            {
                throw new HubException("Not authorized for channel.");
            }

            if (!await permissions.HasPermissionAsync(tenant, userId, Permissions.Message.Send, Context.ConnectionAborted))
            {
                throw new HubException("Not authorized to send typing.");
            }

            await typing.SetTypingAsync(tenant, channel, userId, displayName, Context.ConnectionAborted);
            // B-071 / W6-2: never fan out typing to the author (OthersInGroup).
            await Clients.OthersInGroup(ChannelGroup(tenant, channel)).SendAsync(
                "Typing",
                new { tenantId, channelId, userId = userId.Value, displayName },
                Context.ConnectionAborted);
        });

    private async Task WithRlsAsync(TenantId tenantId, UserId userId, Func<TenantId, UserId, Task> action)
    {
        tenantContext.SetUser(userId);
        tenantContext.SetTenant(tenantId);
        await RlsSession.EnsureAppliedAsync(dbContext, tenantContext, Context.ConnectionAborted);
        try
        {
            await action(tenantId, userId);
            await RlsSession.CommitAsync(dbContext, Context.ConnectionAborted);
        }
        catch
        {
            await RlsSession.RollbackAsync(dbContext, CancellationToken.None);
            throw;
        }
    }

    private async Task EnsureTenantMembershipAsync(TenantId tenantId, UserId userId)
    {
        var roles = await workspaces.GetRolesAsync(tenantId, userId, Context.ConnectionAborted);
        if (roles.Count == 0)
        {
            throw new HubException("Not authorized for tenant.");
        }
    }

    private async Task EnsurePresenceGroupAsync(TenantId tenantId, UserId userId)
    {
        Context.Items["tenantId"] = tenantId.Value;
        Context.Items["userId"] = userId.Value;
        await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenantId), Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(tenantId, userId), Context.ConnectionAborted);
    }

    private Task BroadcastPresenceAsync(TenantId tenantId, UserId userId, PresenceStatus status) =>
        Clients.Group(TenantGroup(tenantId)).SendAsync(
            "PresenceChanged",
            new { tenantId = tenantId.Value, userId = userId.Value, status = status.ToString().ToLowerInvariant() },
            Context.ConnectionAborted);

    private async Task EnsureHubRateLimitAsync(TenantId tenantId, UserId userId)
    {
        var limits = await rateLimits.ResolveAsync(tenantId, Context.ConnectionAborted);
        var allowed = await rateLimiter.TryAcquireAsync(
            RateLimitKeys.Hub(tenantId, userId),
            limits.HubPerMinute,
            TimeSpan.FromMinutes(1),
            Context.ConnectionAborted);
        if (!allowed)
        {
            throw new HubException("Rate limit exceeded.");
        }
    }

    private UserId CurrentUserId()
    {
        var value = Context.User?.FindFirst("vibechat_user_id")?.Value ?? Context.User?.FindFirst("user_id")?.Value;
        return Guid.TryParse(value, out var id) ? new UserId(id) : UserId.Empty;
    }
}
