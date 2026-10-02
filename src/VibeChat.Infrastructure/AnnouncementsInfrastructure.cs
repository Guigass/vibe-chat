using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Messaging;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

public sealed record AnnouncementDto(
    Guid MessageId,
    bool RequiresAcknowledgement,
    DateTimeOffset? AcknowledgeBy,
    DateTimeOffset? ClosedAt,
    bool AcknowledgedByMe,
    int AcknowledgementCount,
    bool CanAcknowledge,
    bool CanViewReport);

public sealed record AnnouncementAcknowledgementItemDto(
    Guid UserId,
    string DisplayName,
    DateTimeOffset AcknowledgedAt);

public sealed record AnnouncementReportDto(
    Guid MessageId,
    int Count,
    bool RequiresAcknowledgement,
    DateTimeOffset? AcknowledgeBy,
    DateTimeOffset? ClosedAt,
    AnnouncementAcknowledgementItemDto[] Items,
    string? NextCursor);

public sealed record PendingAnnouncementDto(
    Guid MessageId,
    Guid ChannelId,
    string ChannelName,
    string AuthorName,
    string BodyPreview,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AcknowledgeBy);

public static class AnnouncementCursors
{
    public static string Encode(DateTimeOffset acknowledgedAt, Guid id) =>
        $"{acknowledgedAt.UtcTicks}:{id:N}";

    public static bool TryDecode(string? cursor, out DateTimeOffset acknowledgedAt, out Guid id)
    {
        acknowledgedAt = default;
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        var parts = cursor.Split(':', 2);
        if (parts.Length != 2
            || !long.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var ticks)
            || !Guid.TryParseExact(parts[1], "N", out id))
        {
            return false;
        }

        acknowledgedAt = new DateTimeOffset(ticks, TimeSpan.Zero);
        return true;
    }
}

public interface IAnnouncementWriter
{
    Task<AnnouncementDto> AcknowledgeAsync(
        TenantId tenantId,
        UserId userId,
        ChannelId channelId,
        MessageId messageId,
        CancellationToken cancellationToken);

    Task<AnnouncementDto> CloseAsync(
        TenantId tenantId,
        UserId userId,
        ChannelId channelId,
        MessageId messageId,
        bool asAdmin,
        CancellationToken cancellationToken);
}

internal static class AnnouncementPublication
{
    public static Announcement? Attach(
        VibeChatDbContext db,
        IOutboxWriter outbox,
        IAuditWriter audit,
        ChannelType channelType,
        TenantId tenantId,
        ChannelId channelId,
        Message message,
        UserId actor,
        bool requiresAcknowledgement,
        DateTimeOffset? acknowledgeBy,
        DateTimeOffset now)
    {
        if (channelType != ChannelType.Announcement)
        {
            return null;
        }

        var row = new Announcement
        {
            MessageId = message.Id,
            TenantId = tenantId,
            ChannelId = channelId,
            CreatedByUserId = actor,
            RequiresAcknowledgement = requiresAcknowledgement,
            AcknowledgeBy = acknowledgeBy,
            CreatedAt = now
        };
        db.Announcements.Add(row);

        audit.Add(new AuditEvent
        {
            TenantId = tenantId,
            ActorUserId = actor,
            Action = AuditActions.AnnouncementPublished,
            EntityType = "Announcement",
            EntityId = message.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new
            {
                channelId = channelId.Value,
                sequence = message.Sequence,
                requiresAcknowledgement,
                acknowledgeBy
            }),
            OccurredAt = now
        });

        outbox.Add(new OutboxMessage
        {
            TenantId = tenantId,
            Type = AnnouncementEvents.Published,
            Payload = JsonSerializer.Serialize(new
            {
                tenantId = tenantId.Value,
                channelId = channelId.Value,
                messageId = message.Id.Value,
                sequence = message.Sequence,
                authorId = actor.Value,
                requiresAcknowledgement,
                acknowledgeBy,
                createdAt = now
            })
        });

        return row;
    }
}

public static class AnnouncementQuery
{
    public static async Task<AnnouncementDto?> LoadAsync(
        VibeChatDbContext db,
        MessageId messageId,
        UserId viewerId,
        bool canAcknowledge,
        bool canViewReport,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var map = await LoadByMessageIdsAsync(
            db,
            [messageId],
            viewerId,
            canAcknowledge,
            canViewReport,
            now,
            cancellationToken);
        return map.TryGetValue(messageId.Value, out var dto) ? dto : null;
    }

    public static async Task<Dictionary<Guid, AnnouncementDto>> LoadByMessageIdsAsync(
        VibeChatDbContext db,
        IReadOnlyCollection<MessageId> messageIds,
        UserId viewerId,
        bool canAcknowledge,
        bool canViewReport,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (messageIds.Count == 0)
        {
            return [];
        }

        var rows = await db.Announcements.AsNoTracking()
            .Where(x => messageIds.Contains(x.MessageId))
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(x => x.MessageId).ToArray();
        var counts = await db.AnnouncementAcknowledgements.AsNoTracking()
            .Where(x => ids.Contains(x.MessageId))
            .GroupBy(x => x.MessageId)
            .Select(g => new { MessageId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.MessageId, x => x.Count, cancellationToken);
        var mine = await db.AnnouncementAcknowledgements.AsNoTracking()
            .Where(x => ids.Contains(x.MessageId) && x.UserId == viewerId)
            .Select(x => x.MessageId)
            .ToListAsync(cancellationToken);
        var mineSet = mine.ToHashSet();

        return rows.ToDictionary(
            x => x.MessageId.Value,
            x => ToDto(x, mineSet.Contains(x.MessageId), counts.GetValueOrDefault(x.MessageId), canAcknowledge, canViewReport, now));
    }

    public static AnnouncementDto ToDto(
        Announcement row,
        bool acknowledgedByMe,
        int count,
        bool canAcknowledge,
        bool canViewReport,
        DateTimeOffset now)
    {
        var open = AnnouncementPolicies.IsOpen(row.ClosedAt, row.AcknowledgeBy, now);
        return new AnnouncementDto(
            row.MessageId.Value,
            row.RequiresAcknowledgement,
            row.AcknowledgeBy,
            row.ClosedAt,
            acknowledgedByMe,
            count,
            row.RequiresAcknowledgement && open && canAcknowledge && !acknowledgedByMe,
            canViewReport);
    }

    public static async Task<Dictionary<Guid, int>> PendingCountsAsync(
        VibeChatDbContext db,
        IReadOnlyCollection<ChannelId> channelIds,
        UserId userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (channelIds.Count == 0)
        {
            return [];
        }

        var rows = await (
            from announcement in db.Announcements.AsNoTracking()
            join message in db.Messages.AsNoTracking() on announcement.MessageId equals message.Id
            where channelIds.Contains(announcement.ChannelId)
                && announcement.RequiresAcknowledgement
                && announcement.ClosedAt == null
                && (announcement.AcknowledgeBy == null || announcement.AcknowledgeBy > now)
                && message.DeletedAt == null
                && !db.AnnouncementAcknowledgements.Any(a =>
                    a.MessageId == announcement.MessageId && a.UserId == userId)
            group announcement by announcement.ChannelId
            into grouped
            select new { ChannelId = grouped.Key, Count = grouped.Count() }
        ).ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.ChannelId.Value, x => x.Count);
    }
}

public sealed class AnnouncementWriter(
    VibeChatDbContext dbContext,
    ITenantContext tenantContext,
    IOutboxWriter outbox,
    IAuditWriter audit,
    IClock clock,
    IPermissionChecker permissions,
    IChannelMembershipReader channels) : IAnnouncementWriter
{
    public async Task<AnnouncementDto> AcknowledgeAsync(
        TenantId tenantId,
        UserId userId,
        ChannelId channelId,
        MessageId messageId,
        CancellationToken cancellationToken)
    {
        tenantContext.SetTenant(tenantId);
        tenantContext.SetUser(userId);
        await RlsSession.EnsureAppliedAsync(dbContext, tenantContext, cancellationToken);

        var announcement = await RequireInChannelAsync(tenantId, channelId, messageId, cancellationToken);
        if (!announcement.RequiresAcknowledgement)
        {
            throw new AnnouncementAcknowledgementNotRequiredException();
        }

        var now = clock.UtcNow;
        if (!AnnouncementPolicies.IsOpen(announcement.ClosedAt, announcement.AcknowledgeBy, now))
        {
            throw new AnnouncementClosedException();
        }

        if (!await channels.CanAccessAsync(tenantId, channelId, userId, cancellationToken)
            || !await permissions.HasPermissionAsync(tenantId, userId, Permissions.Announcement.Acknowledge, cancellationToken))
        {
            throw new UnauthorizedAccessException("User cannot acknowledge this announcement.");
        }

        var existing = await dbContext.AnnouncementAcknowledgements
            .AsNoTracking()
            .AnyAsync(x => x.MessageId == messageId && x.UserId == userId, cancellationToken);
        if (!existing)
        {
            var prior = await dbContext.AnnouncementAcknowledgements
                .CountAsync(x => x.MessageId == messageId, cancellationToken);
            dbContext.AnnouncementAcknowledgements.Add(new AnnouncementAcknowledgement
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                MessageId = messageId,
                ChannelId = channelId,
                UserId = userId,
                AcknowledgedAt = now
            });
            outbox.Add(new OutboxMessage
            {
                TenantId = tenantId,
                Type = AnnouncementEvents.Acknowledged,
                Payload = JsonSerializer.Serialize(new
                {
                    tenantId = tenantId.Value,
                    channelId = channelId.Value,
                    messageId = messageId.Value,
                    acknowledgedByUserId = userId.Value,
                    acknowledgementCount = prior + 1,
                    acknowledgedAt = now
                })
            });

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Repeat confirmation: the unique row already exists, so the count does not grow.
                dbContext.ChangeTracker.Clear();
            }
        }

        return (await AnnouncementQuery.LoadAsync(
            dbContext,
            messageId,
            userId,
            canAcknowledge: true,
            canViewReport: false,
            clock.UtcNow,
            cancellationToken))!;
    }

    public async Task<AnnouncementDto> CloseAsync(
        TenantId tenantId,
        UserId userId,
        ChannelId channelId,
        MessageId messageId,
        bool asAdmin,
        CancellationToken cancellationToken)
    {
        tenantContext.SetTenant(tenantId);
        tenantContext.SetUser(userId);
        await RlsSession.EnsureAppliedAsync(dbContext, tenantContext, cancellationToken);

        var announcement = await dbContext.Announcements
            .FirstOrDefaultAsync(x => x.MessageId == messageId && x.TenantId == tenantId && x.ChannelId == channelId, cancellationToken)
            ?? throw new AnnouncementNotFoundException();

        var canPublish = await permissions.HasPermissionAsync(tenantId, userId, Permissions.Announcement.Publish, cancellationToken);
        if (!canPublish && !asAdmin)
        {
            throw new UnauthorizedAccessException("User cannot close this announcement.");
        }

        if (!await channels.CanAccessAsync(tenantId, channelId, userId, cancellationToken))
        {
            throw new UnauthorizedAccessException("User cannot close this announcement.");
        }

        if (announcement.ClosedAt is null)
        {
            var now = clock.UtcNow;
            announcement.ClosedAt = now;
            audit.Add(new AuditEvent
            {
                TenantId = tenantId,
                ActorUserId = userId,
                Action = AuditActions.AnnouncementClosed,
                EntityType = "Announcement",
                EntityId = messageId.ToString(),
                MetadataJson = JsonSerializer.Serialize(new { channelId = channelId.Value }),
                OccurredAt = now
            });
            outbox.Add(new OutboxMessage
            {
                TenantId = tenantId,
                Type = AnnouncementEvents.Acknowledged,
                Payload = JsonSerializer.Serialize(new
                {
                    tenantId = tenantId.Value,
                    channelId = channelId.Value,
                    messageId = messageId.Value,
                    closedAt = now,
                    acknowledgementCount = await dbContext.AnnouncementAcknowledgements.CountAsync(
                        x => x.MessageId == messageId, cancellationToken)
                })
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var canAck = await permissions.HasPermissionAsync(tenantId, userId, Permissions.Announcement.Acknowledge, cancellationToken);
        return (await AnnouncementQuery.LoadAsync(
            dbContext, messageId, userId, canAck, canViewReport: true, clock.UtcNow, cancellationToken))!;
    }

    private async Task<Announcement> RequireInChannelAsync(
        TenantId tenantId,
        ChannelId channelId,
        MessageId messageId,
        CancellationToken cancellationToken)
    {
        var announcement = await dbContext.Announcements.AsNoTracking()
            .FirstOrDefaultAsync(x => x.MessageId == messageId && x.TenantId == tenantId, cancellationToken)
            ?? throw new AnnouncementNotFoundException();
        if (announcement.ChannelId != channelId)
        {
            throw new AnnouncementNotFoundException();
        }

        return announcement;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
