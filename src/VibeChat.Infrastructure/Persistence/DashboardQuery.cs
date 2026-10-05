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

public sealed class DashboardQuery(VibeChatDbContext dbContext) : IDashboardQuery
{
    public async Task<DashboardStats> GetStatsAsync(CancellationToken cancellationToken)
    {
        var workspaces = await dbContext.Workspaces.IgnoreQueryFilters().CountAsync(cancellationToken);
        var users = await dbContext.UserProfiles.CountAsync(cancellationToken);
        var channels = await dbContext.Channels.IgnoreQueryFilters().CountAsync(cancellationToken);
        var messages = await dbContext.Messages.IgnoreQueryFilters().CountAsync(cancellationToken);
        var outbox = await dbContext.OutboxMessages.IgnoreQueryFilters().CountAsync(x => x.ProcessedAt == null, cancellationToken);
        return new DashboardStats(workspaces, users, channels, messages, outbox);
    }
}
