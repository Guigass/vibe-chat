using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Files;
using VibeChat.Messaging;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure.Messaging;

public sealed record MessageMoveSuccess(
    Guid SourceMessageId,
    Guid DestinationMessageId,
    Guid DestinationChannelId,
    long DestinationSequence,
    bool Tombstone);

public readonly record struct MessageHistoryFailure(string Error, int StatusCode);

public static class MessageHistoryCommands
{
    public static async Task SnapshotAsync(
        VibeChatDbContext db,
        Message message,
        UserId actorId,
        DateTimeOffset at,
        CancellationToken ct)
    {
        var max = await db.MessageVersions
            .Where(x => x.MessageId == message.Id)
            .MaxAsync(x => (int?)x.VersionNumber, ct) ?? 0;
        db.MessageVersions.Add(new MessageVersion
        {
            Id = Guid.NewGuid(),
            TenantId = message.TenantId,
            MessageId = message.Id,
            VersionNumber = max + 1,
            Body = message.Body,
            ActorUserId = actorId,
            CreatedAt = at
        });
    }

    public static async Task<(MessageMoveSuccess? Success, MessageHistoryFailure? Failure)> MoveAsync(
        VibeChatDbContext db,
        IConversationSequenceStore sequences,
        IOutboxWriter outbox,
        IAuditWriter audit,
        Channel sourceChannel,
        Channel destinationChannel,
        Message source,
        UserId actorId,
        string idempotencyKey,
        string? scope,
        bool? leaveTombstone,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var normalizedScope = MessageHistoryPolicies.NormalizeScope(scope);
        if (normalizedScope.Length == 0)
        {
            return Fail(MessageHistoryPolicies.InvalidScope, 400);
        }

        if (sourceChannel.WorkspaceId != destinationChannel.WorkspaceId)
        {
            return Fail(MessageHistoryPolicies.NotMovable, 403);
        }

        if (sourceChannel.Id == destinationChannel.Id)
        {
            return Fail(MessageHistoryPolicies.SameChannel, 400);
        }

        if (destinationChannel.Type == ChannelType.Announcement)
        {
            return Fail(MessageHistoryPolicies.NotMovable, 409);
        }

        var existing = await db.MessageMoves
            .FirstOrDefaultAsync(x => x.TenantId == source.TenantId && x.IdempotencyKey == idempotencyKey, ct);
        if (existing is not null)
        {
            if (existing.SourceMessageId != source.Id || existing.DestinationChannelId != destinationChannel.Id)
            {
                return Fail(MessageHistoryPolicies.IdempotencyConflict, 409);
            }

            return (new MessageMoveSuccess(
                existing.SourceMessageId.Value,
                existing.DestinationMessageId.Value,
                existing.DestinationChannelId.Value,
                existing.DestinationSequence,
                existing.LeaveTombstone), null);
        }

        if (source.MovedToMessageId is not null || MessageHistoryPolicies.IsTombstone(source.Body))
        {
            return Fail(MessageHistoryPolicies.AlreadyMoved, 409);
        }

        var root = await ResolveRootAsync(db, sourceChannel, source, ct);
        if (root is null)
        {
            return Fail(MessageHistoryPolicies.NotMovable, 404);
        }

        var thread = await db.MessageThreads
            .FirstOrDefaultAsync(x => x.TenantId == source.TenantId && x.ParentMessageId == root.Id, ct);
        var replies = thread is null
            ? []
            : await db.Messages
                .Where(x => x.ThreadId == thread.Id && x.ConversationId != sourceChannel.Id && x.DeletedAt == null)
                .OrderBy(x => x.Sequence)
                .ToListAsync(ct);

        if (normalizedScope == MessageHistoryPolicies.ScopeMessage && replies.Count > 0 && source.Id == root.Id)
        {
            return Fail(MessageHistoryPolicies.MessageHasThread, 409);
        }

        if (normalizedScope == MessageHistoryPolicies.ScopeThread && thread is null)
        {
            return Fail(MessageHistoryPolicies.NoThread, 400);
        }

        var moving = normalizedScope == MessageHistoryPolicies.ScopeThread
            ? new[] { root }.Concat(replies).ToArray()
            : new[] { source };
        if (moving.Any(x => x.DeletedAt is not null || MessageHistoryPolicies.IsTombstone(x.Body) || x.Body.StartsWith("<system:", StringComparison.Ordinal)))
        {
            return Fail(MessageHistoryPolicies.NotMovable, 409);
        }

        var movingIds = moving.Select(x => x.Id).ToArray();
        if (await db.Polls.AnyAsync(x => movingIds.Contains(x.MessageId), ct)
            || await db.Announcements.AnyAsync(x => movingIds.Contains(x.MessageId), ct))
        {
            return Fail(MessageHistoryPolicies.NotMovable, 409);
        }

        var policy = await db.MessageLifecyclePolicies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == source.TenantId, ct);
        var tombstone = leaveTombstone ?? policy?.LeaveTombstone ?? true;

        var authorIds = moving.Select(x => x.AuthorId).Distinct().ToArray();
        var names = await db.UserProfiles.AsNoTracking()
            .Where(x => authorIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

        Message? destinationRoot = null;
        MessageThread? destinationThread = null;
        foreach (var piece in moving)
        {
            var isReply = destinationRoot is not null && piece.Id != root.Id && normalizedScope == MessageHistoryPolicies.ScopeThread;
            var conversationId = isReply ? new ChannelId(destinationThread!.Id) : destinationChannel.Id;
            var sequence = await sequences.NextAsync(source.TenantId, conversationId, ct);
            var copy = new Message
            {
                Id = new MessageId(Guid.NewGuid()),
                TenantId = piece.TenantId,
                ConversationId = conversationId,
                Sequence = sequence,
                AuthorId = piece.AuthorId,
                Body = piece.Body,
                ThreadId = isReply ? destinationThread!.Id : null,
                MovedFromMessageId = piece.Id,
                MovedFromChannelId = sourceChannel.Id,
                CreatedAt = piece.CreatedAt
            };
            db.Messages.Add(copy);
            await CopyVersionsAsync(db, piece, copy, actorId, now, ct);
            await CopyAttachmentsAsync(db, piece, copy, destinationChannel.Id, ct);

            if (!isReply && normalizedScope == MessageHistoryPolicies.ScopeThread && thread is not null && piece.Id == root.Id)
            {
                destinationThread = new MessageThread
                {
                    Id = Guid.NewGuid(),
                    TenantId = source.TenantId,
                    ChannelId = destinationChannel.Id,
                    ParentMessageId = copy.Id,
                    CreatedBy = piece.AuthorId,
                    CreatedAt = now
                };
                db.MessageThreads.Add(destinationThread);
                copy.ThreadId = destinationThread.Id;
            }

            destinationRoot ??= copy;
            piece.MovedToMessageId = copy.Id;
            piece.MovedToChannelId = destinationChannel.Id;
            if (tombstone)
            {
                piece.Body = MessageHistoryPolicies.TombstoneBody;
                piece.EditedAt = now;
            }
            else
            {
                piece.DeletedAt = now;
                piece.DeletedBy = actorId;
            }

            var authorName = names.TryGetValue(piece.AuthorId, out var known) ? known : piece.AuthorId.Value.ToString();
            outbox.Add(new OutboxMessage
            {
                TenantId = source.TenantId,
                Type = nameof(MessageMovedEvent),
                Payload = JsonSerializer.Serialize(new
                {
                    tenantId = source.TenantId.Value,
                    channelId = destinationChannel.Id.Value,
                    conversationId = copy.ConversationId.Value,
                    threadId = copy.ThreadId,
                    parentMessageId = isReply ? destinationRoot!.Id.Value : (Guid?)null,
                    messageId = copy.Id.Value,
                    clientMessageId = copy.Id.Value,
                    authorId = copy.AuthorId.Value,
                    authorName,
                    sequence = copy.Sequence,
                    body = copy.Body,
                    createdAt = copy.CreatedAt,
                    movedFromMessageId = piece.Id.Value,
                    movedFromChannelId = sourceChannel.Id.Value
                })
            });

            if (tombstone)
            {
                outbox.Add(new OutboxMessage
                {
                    TenantId = source.TenantId,
                    Type = nameof(MessageEditedEvent),
                    Payload = JsonSerializer.Serialize(new
                    {
                        tenantId = source.TenantId.Value,
                        channelId = sourceChannel.Id.Value,
                        conversationId = piece.ConversationId.Value,
                        threadId = piece.ThreadId,
                        messageId = piece.Id.Value,
                        sequence = piece.Sequence,
                        body = piece.Body,
                        editedAt = now
                    })
                });
            }
            else
            {
                outbox.Add(new OutboxMessage
                {
                    TenantId = source.TenantId,
                    Type = nameof(MessageDeletedEvent),
                    Payload = JsonSerializer.Serialize(new
                    {
                        tenantId = source.TenantId.Value,
                        channelId = sourceChannel.Id.Value,
                        messageId = piece.Id.Value,
                        sequence = piece.Sequence,
                        deletedAt = now
                    })
                });
            }
        }

        var recorded = destinationRoot!;
        db.MessageMoves.Add(new MessageMove
        {
            Id = Guid.NewGuid(),
            TenantId = source.TenantId,
            IdempotencyKey = idempotencyKey,
            SourceMessageId = source.Id,
            SourceChannelId = sourceChannel.Id,
            DestinationMessageId = recorded.Id,
            DestinationChannelId = destinationChannel.Id,
            DestinationSequence = recorded.Sequence,
            ActorUserId = actorId,
            Scope = normalizedScope,
            LeaveTombstone = tombstone,
            CreatedAt = now
        });
        audit.Add(new AuditEvent
        {
            TenantId = source.TenantId,
            ActorUserId = actorId,
            Action = AuditActions.MessageMove,
            EntityType = "Message",
            EntityId = source.Id.Value.ToString(),
            MetadataJson = JsonSerializer.Serialize(new
            {
                sourceChannelId = sourceChannel.Id.Value,
                destinationChannelId = destinationChannel.Id.Value,
                destinationMessageId = recorded.Id.Value,
                scope = normalizedScope,
                tombstone,
                count = moving.Length
            })
        });

        return (new MessageMoveSuccess(
            source.Id.Value,
            recorded.Id.Value,
            destinationChannel.Id.Value,
            recorded.Sequence,
            tombstone), null);
    }

    private static async Task<Message?> ResolveRootAsync(
        VibeChatDbContext db,
        Channel sourceChannel,
        Message source,
        CancellationToken ct)
    {
        if (source.ConversationId == sourceChannel.Id)
        {
            return source;
        }

        if (source.ThreadId is not Guid threadId)
        {
            return null;
        }

        var thread = await db.MessageThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == threadId && x.ChannelId == sourceChannel.Id, ct);
        if (thread is null)
        {
            return null;
        }

        return await db.Messages.FirstOrDefaultAsync(x => x.Id == thread.ParentMessageId, ct) ?? source;
    }

    private static async Task CopyVersionsAsync(
        VibeChatDbContext db,
        Message source,
        Message destination,
        UserId actorId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var prior = await db.MessageVersions
            .Where(x => x.MessageId == source.Id)
            .OrderBy(x => x.VersionNumber)
            .ToListAsync(ct);
        foreach (var version in prior)
        {
            db.MessageVersions.Add(CloneVersion(version, destination.Id));
        }

        var next = prior.Count == 0 ? 1 : prior[^1].VersionNumber + 1;
        var snapshot = new MessageVersion
        {
            Id = Guid.NewGuid(),
            TenantId = source.TenantId,
            MessageId = source.Id,
            VersionNumber = next,
            Body = source.Body,
            ActorUserId = actorId,
            CreatedAt = now
        };
        db.MessageVersions.Add(snapshot);
    }

    private static MessageVersion CloneVersion(MessageVersion version, MessageId messageId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = version.TenantId,
        MessageId = messageId,
        VersionNumber = version.VersionNumber,
        Body = version.Body,
        ActorUserId = version.ActorUserId,
        CreatedAt = version.CreatedAt
    };

    private static async Task CopyAttachmentsAsync(
        VibeChatDbContext db,
        Message source,
        Message destination,
        ChannelId destinationChannelId,
        CancellationToken ct)
    {
        var attachments = await db.Attachments
            .Where(x => x.MessageId == source.Id && x.Status == AttachmentStatus.Ready)
            .ToListAsync(ct);
        foreach (var attachment in attachments)
        {
            var siblings = await db.Attachments
                .Where(x => x.TenantId == attachment.TenantId && x.StorageKey == attachment.StorageKey)
                .ToListAsync(ct);
            var next = AttachmentReferencePolicies.CountAfterAdd(siblings.Count);
            foreach (var sibling in siblings)
            {
                sibling.ReferenceCount = next;
            }

            db.Attachments.Add(new Attachment
            {
                Id = Guid.NewGuid(),
                TenantId = attachment.TenantId,
                ChannelId = destinationChannelId,
                MessageId = destination.Id,
                UploadedBy = attachment.UploadedBy,
                FileName = attachment.FileName,
                ContentType = attachment.ContentType,
                SizeBytes = attachment.SizeBytes,
                StorageKey = attachment.StorageKey,
                ChecksumSha256 = attachment.ChecksumSha256,
                Status = attachment.Status,
                Kind = attachment.Kind,
                ReferenceCount = next,
                DurationMs = attachment.DurationMs,
                Waveform = attachment.Waveform,
                ThumbnailKey = attachment.ThumbnailKey,
                Width = attachment.Width,
                Height = attachment.Height,
                ThumbnailStatus = attachment.ThumbnailStatus,
                PageCount = attachment.PageCount,
                CreatedAt = attachment.CreatedAt,
                ReadyAt = attachment.ReadyAt
            });
        }
    }

    private static (MessageMoveSuccess? Success, MessageHistoryFailure? Failure) Fail(string error, int status) =>
        (null, new MessageHistoryFailure(error, status));
}
