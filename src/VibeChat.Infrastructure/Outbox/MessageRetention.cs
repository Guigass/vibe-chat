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

/// <summary>Process-level kill switch + batch knobs for B-047 purge (ADR-018).</summary>
public sealed class MessageRetentionOptions
{
    public const string SectionName = "MessageRetention";

    public bool Enabled { get; set; }
    public int DefaultRetentionDays { get; set; } = MessageRetentionSettings.DefaultRetentionDays;
    public int BatchSize { get; set; } = 500;
    public int IntervalMinutes { get; set; } = 60;
}

/// <summary>
/// Hard-deletes soft-deleted messages past tenant retention (B-047).
/// Requires MessageRetention:Enabled=true and tenant MessageRetentionSettings.Enabled.
/// </summary>
public sealed class MessageRetentionPurgeProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<MessageRetentionPurgeProcessor> logger)
{
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var process = await scope.ServiceProvider.GetRequiredService<ProcessSettingsResolver>()
            .ResolveAsync(cancellationToken);
        if (!process.MessageRetentionEnabled)
        {
            return 0;
        }

        var batchSize = process.RetentionBatchSize;

        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var now = clock.UtcNow;

        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.SetJobRole("retention");
        await RlsSession.EnsureAppliedAsync(db, tenant, cancellationToken);

        var policies = await db.MessageRetentionSettings.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.Enabled)
            .ToListAsync(cancellationToken);
        if (policies.Count == 0)
        {
            await RlsSession.CommitAsync(db, cancellationToken);
            return 0;
        }

        var purgedTotal = 0;
        foreach (var policy in policies)
        {
            tenant.SetTenant(policy.TenantId);
            await RlsSession.EnsureAppliedAsync(db, tenant, cancellationToken);

            var days = Math.Clamp(
                policy.RetentionDays <= 0 ? process.RetentionDefaultDays : policy.RetentionDays,
                MessageRetentionSettings.MinRetentionDays,
                MessageRetentionSettings.MaxRetentionDays);
            var cutoff = now.AddDays(-days);

            var candidates = await db.Messages.IgnoreQueryFilters()
                .Where(x => x.TenantId == policy.TenantId
                    && x.DeletedAt != null
                    && x.DeletedAt < cutoff)
                .OrderBy(x => x.DeletedAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            if (candidates.Count == 0)
            {
                continue;
            }

            var messageIds = candidates.Select(x => x.Id).ToList();
            var messageIdGuids = candidates.Select(x => x.Id.Value).ToHashSet();
            var reactions = await db.Reactions.IgnoreQueryFilters()
                .Where(x => x.TenantId == policy.TenantId && messageIds.Contains(x.MessageId))
                .ToListAsync(cancellationToken);
            if (reactions.Count > 0)
            {
                db.Reactions.RemoveRange(reactions);
            }

            var pins = await db.PinnedMessages.IgnoreQueryFilters()
                .Where(x => x.TenantId == policy.TenantId && messageIds.Contains(x.MessageId))
                .ToListAsync(cancellationToken);
            if (pins.Count > 0)
            {
                db.PinnedMessages.RemoveRange(pins);
            }

            var saved = await db.SavedMessages.IgnoreQueryFilters()
                .Where(x => x.TenantId == policy.TenantId && messageIds.Contains(x.MessageId))
                .ToListAsync(cancellationToken);
            if (saved.Count > 0)
            {
                db.SavedMessages.RemoveRange(saved);
            }

            var reminderCandidates = await db.Reminders.IgnoreQueryFilters()
                .Where(x => x.TenantId == policy.TenantId && x.MessageId != null)
                .ToListAsync(cancellationToken);
            var reminders = reminderCandidates
                .Where(x => x.MessageId is MessageId reminderMessageId && messageIds.Contains(reminderMessageId))
                .ToList();
            if (reminders.Count > 0)
            {
                db.Reminders.RemoveRange(reminders);
            }

            var mentions = await db.MessageMentions.IgnoreQueryFilters()
                .Where(x => x.TenantId == policy.TenantId && messageIds.Contains(x.MessageId))
                .ToListAsync(cancellationToken);
            if (mentions.Count > 0)
            {
                db.MessageMentions.RemoveRange(mentions);
            }

            var pollVotes = await db.PollVotes.IgnoreQueryFilters()
                .Where(x => x.TenantId == policy.TenantId && messageIds.Contains(x.PollId))
                .ToListAsync(cancellationToken);
            if (pollVotes.Count > 0)
            {
                db.PollVotes.RemoveRange(pollVotes);
            }

            var pollOptions = await db.PollOptions.IgnoreQueryFilters()
                .Where(x => x.TenantId == policy.TenantId && messageIds.Contains(x.PollId))
                .ToListAsync(cancellationToken);
            if (pollOptions.Count > 0)
            {
                db.PollOptions.RemoveRange(pollOptions);
            }

            var polls = await db.Polls.IgnoreQueryFilters()
                .Where(x => x.TenantId == policy.TenantId && messageIds.Contains(x.MessageId))
                .ToListAsync(cancellationToken);
            if (polls.Count > 0)
            {
                db.Polls.RemoveRange(polls);
            }

            // Shared StorageKey (B-085): remove this message's attachment row and keep siblings.
            // Sole reference: detach metadata (B-047) — no MinIO delete in this slice.
            var attachmentCandidates = await db.Attachments.IgnoreQueryFilters()
                .Where(x => x.TenantId == policy.TenantId && x.MessageId != null)
                .ToListAsync(cancellationToken);
            var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
            foreach (var attachment in attachmentCandidates
                .Where(a => a.MessageId is { } mid && messageIdGuids.Contains(mid.Value)))
            {
                var siblings = await db.Attachments.IgnoreQueryFilters()
                    .Where(x => x.TenantId == policy.TenantId
                        && x.StorageKey == attachment.StorageKey
                        && x.Id != attachment.Id)
                    .ToListAsync(cancellationToken);
                if (siblings.Count == 0)
                {
                    // Sole row (B-090): delete original + thumbnail blobs, then detach metadata (B-047).
                    await storage.DeleteObjectAsync(attachment.StorageKey, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(attachment.ThumbnailKey))
                    {
                        await storage.DeleteObjectAsync(attachment.ThumbnailKey, cancellationToken);
                    }

                    attachment.MessageId = null;
                    attachment.ReferenceCount = 1;
                    attachment.ThumbnailKey = null;
                    attachment.ThumbnailStatus = null;
                }
                else
                {
                    var next = Math.Max(1, siblings.Count);
                    foreach (var sibling in siblings)
                    {
                        sibling.ReferenceCount = next;
                    }

                    // Shared blob still referenced — drop this row only; never delete MinIO here.
                    db.Attachments.Remove(attachment);
                }
            }

            db.Messages.RemoveRange(candidates);
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid(),
                TenantId = policy.TenantId,
                ActorUserId = null,
                Action = AuditActions.MessagePurge,
                EntityType = "Message",
                EntityId = null,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    count = candidates.Count,
                    retentionDays = days,
                    cutoff
                }),
                OccurredAt = now
            });

            await db.SaveChangesAsync(cancellationToken);
            purgedTotal += candidates.Count;
            logger.LogInformation(
                "Purged {Count} soft-deleted messages for tenant {TenantId} (retention {Days}d)",
                candidates.Count,
                policy.TenantId.Value,
                days);
        }

        await RlsSession.CommitAsync(db, cancellationToken);
        return purgedTotal;
    }
}

public sealed class MessageRetentionPurgeDispatcher(
    MessageRetentionPurgeProcessor processor,
    IServiceScopeFactory scopeFactory,
    ILogger<MessageRetentionPurgeDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var intervalMinutes = ProcessSettingsDefaults.RetentionIntervalMinutes;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var process = await scope.ServiceProvider.GetRequiredService<ProcessSettingsResolver>()
                    .ResolveAsync(stoppingToken);
                intervalMinutes = process.RetentionIntervalMinutes;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to resolve retention interval");
            }

            try
            {
                await processor.ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Message retention purge loop failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }
}
