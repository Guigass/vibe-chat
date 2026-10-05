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

public sealed class EfOutboxWriter(VibeChatDbContext dbContext, IClock clock) : IOutboxWriter
{
    public void Add(OutboxMessage message)
    {
        message.Id = message.Id == Guid.Empty ? Guid.NewGuid() : message.Id;
        message.OccurredAt = message.OccurredAt == default ? clock.UtcNow : message.OccurredAt;
        dbContext.OutboxMessages.Add(message);
    }
}

public sealed class EfAuditWriter(VibeChatDbContext dbContext, IClock clock) : IAuditWriter
{
    public void Add(AuditEvent auditEvent)
    {
        auditEvent.Id = auditEvent.Id == Guid.Empty ? Guid.NewGuid() : auditEvent.Id;
        auditEvent.OccurredAt = auditEvent.OccurredAt == default ? clock.UtcNow : auditEvent.OccurredAt;
        dbContext.AuditEvents.Add(auditEvent);
    }
}

public sealed class EfIdempotencyStore(VibeChatDbContext dbContext, IClock clock) : IIdempotencyStore
{
    public async Task<IdempotencyRecord?> FindAsync(TenantId tenantId, string key, CancellationToken cancellationToken)
    {
        var entry = await dbContext.IdempotencyEntries.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Key == key, cancellationToken);

        return entry is null
            ? null
            : new IdempotencyRecord(entry.TenantId, entry.Key, entry.RequestHash, entry.ResultJson, entry.CreatedAt);
    }

    public Task StoreAsync(IdempotencyRecord record, CancellationToken cancellationToken)
    {
        dbContext.IdempotencyEntries.Add(new IdempotencyEntry
        {
            Id = Guid.NewGuid(),
            TenantId = record.TenantId,
            Key = record.Key,
            RequestHash = record.RequestHash,
            ResultJson = record.ResultJson,
            CreatedAt = record.CreatedAt == default ? clock.UtcNow : record.CreatedAt
        });

        return Task.CompletedTask;
    }
}
