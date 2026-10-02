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

public sealed class MessageWriter(
    VibeChatDbContext dbContext,
    ITenantContext tenantContext,
    IConversationSequenceStore sequences,
    IIdempotencyStore idempotencyStore,
    IOutboxWriter outbox,
    IAuditWriter audit,
    IClock clock,
    IPermissionChecker permissions,
    IChannelMembershipReader channels,
    IPresenceService presence,
    FilesSettingsResolver filesSettings) : IMessageWriter
{
    public async Task<MessageSendResult> SendAsync(SendMessageCommand command, CancellationToken cancellationToken)
    {
        tenantContext.SetTenant(command.TenantId);
        tenantContext.SetUser(command.UserId);
        await RlsSession.EnsureAppliedAsync(dbContext, tenantContext, cancellationToken);

        var parentChannelId = command.ChannelId;
        var conversationId = command.ChannelId;
        Guid? threadId = command.ThreadId;
        Guid? threadParentMessageId = null;

        if (threadId is Guid resolvedThreadId)
        {
            var thread = await dbContext.MessageThreads.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == resolvedThreadId && x.TenantId == command.TenantId, cancellationToken);
            if (thread is null)
            {
                VibeChatMetrics.MessagesRejected.Add(1);
                throw new InvalidOperationException("Thread not found.");
            }

            parentChannelId = thread.ChannelId;
            conversationId = new ChannelId(thread.Id);
            threadParentMessageId = thread.ParentMessageId.Value;
            if (command.ChannelId != ChannelId.Empty && command.ChannelId != parentChannelId)
            {
                VibeChatMetrics.MessagesRejected.Add(1);
                throw new UnauthorizedAccessException("Thread does not belong to the requested channel.");
            }
        }

        if (!await channels.CanAccessAsync(command.TenantId, parentChannelId, command.UserId, cancellationToken)
            || !await permissions.HasPermissionAsync(command.TenantId, command.UserId, Permissions.Message.Send, cancellationToken))
        {
            VibeChatMetrics.MessagesRejected.Add(1);
            throw new UnauthorizedAccessException("User cannot send messages to this channel.");
        }

        var parentChannel = await dbContext.Channels.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == parentChannelId && x.TenantId == command.TenantId, cancellationToken);
        if (parentChannel is null)
        {
            VibeChatMetrics.MessagesRejected.Add(1);
            throw new UnauthorizedAccessException("User cannot send messages to this channel.");
        }

        var announcementChannel = parentChannel.Type == ChannelType.Announcement;
        if (announcementChannel
            && !await permissions.HasPermissionAsync(command.TenantId, command.UserId, Permissions.Announcement.Publish, cancellationToken))
        {
            VibeChatMetrics.MessagesRejected.Add(1);
            throw new UnauthorizedAccessException("AnnouncementPublishForbidden");
        }

        AnnouncementPolicies.ValidateRequest(
            command.RequiresAcknowledgement,
            command.AcknowledgeBy,
            announcementChannel,
            clock.UtcNow);

        object? replyToPayload = null;
        if (command.ReplyToMessageId is MessageId replyToId)
        {
            var replyTarget = await (
                from m in dbContext.Messages.AsNoTracking()
                where m.Id == replyToId && m.TenantId == command.TenantId
                join u in dbContext.UserProfiles on m.AuthorId equals u.Id into authors
                from u in authors.DefaultIfEmpty()
                select new
                {
                    Message = m,
                    AuthorName = u != null ? u.DisplayName : m.AuthorId.Value.ToString()
                }).FirstOrDefaultAsync(cancellationToken);

            if (replyTarget is null)
            {
                throw new ArgumentException("ReplyToNotFound");
            }

            var target = replyTarget.Message;
            var sameChannelConversation = target.ConversationId == parentChannelId;
            var sameThreadConversation = threadId is not null && target.ConversationId == conversationId;
            var isThreadParent = threadParentMessageId is Guid parentId
                && target.Id.Value == parentId
                && target.ConversationId == parentChannelId;

            if (!sameChannelConversation && !sameThreadConversation && !isThreadParent)
            {
                throw new ArgumentException("ReplyToDifferentChannel");
            }

            replyToPayload = new
            {
                messageId = target.Id.Value,
                authorName = target.DeletedAt is null ? replyTarget.AuthorName : string.Empty,
                preview = target.DeletedAt is null
                    ? TruncateReplyPreview(target.Body)
                    : string.Empty,
                deleted = target.DeletedAt is not null
            };
        }

        var attachmentIds = (command.AttachmentIds ?? [])
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToArray();
        var body = MessageBodyPolicies.Normalize(command.Body);
        if (MessageBodyPolicies.IsEmpty(body) && attachmentIds.Length == 0)
        {
            throw new ArgumentException("Message body or attachments are required.");
        }

        if (!MessageBodyPolicies.IsWithinLimit(body))
        {
            throw new ArgumentException("MessageBodyTooLong");
        }

        var files = await filesSettings.ResolveAsync(command.TenantId, cancellationToken);
        var maxAttachments = files.MaxAttachmentsPerMessage;
        if (!AttachmentPolicies.IsWithinAttachmentCount(attachmentIds.Length, maxAttachments))
        {
            throw new ArgumentException("TooManyAttachments");
        }

        var normalized = command with
        {
            ChannelId = parentChannelId,
            ThreadId = threadId,
            Body = body,
            AttachmentIds = attachmentIds
        };
        var hash = MessageIdempotency.ComputeRequestHash(normalized);
        var existing = await idempotencyStore.FindAsync(command.TenantId, command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            var idempotentResult = JsonSerializer.Deserialize<MessageSendResult>(existing.ResultJson)!;
            return idempotentResult with { Idempotent = true };
        }

        Attachment[] attachments = [];
        if (attachmentIds.Length > 0)
        {
            attachments = await dbContext.Attachments
                .Where(x => attachmentIds.Contains(x.Id)
                    && x.TenantId == command.TenantId
                    && x.ChannelId == parentChannelId
                    && x.UploadedBy == command.UserId
                    && x.Status == AttachmentStatus.Ready
                    && x.MessageId == null)
                .ToArrayAsync(cancellationToken);

            if (attachments.Length != attachmentIds.Length)
            {
                throw new InvalidOperationException("One or more attachments are invalid or not ready.");
            }
        }

        var sequence = await sequences.NextAsync(command.TenantId, conversationId, cancellationToken);
        var now = clock.UtcNow;

        var message = new Message
        {
            Id = command.MessageId,
            TenantId = command.TenantId,
            ConversationId = conversationId,
            Sequence = sequence,
            AuthorId = command.UserId,
            Body = body,
            ReplyToMessageId = command.ReplyToMessageId,
            ThreadId = threadId,
            CreatedAt = now
        };

        dbContext.Messages.Add(message);
        foreach (var attachment in attachments)
        {
            attachment.MessageId = message.Id;
        }

        var mentionContext = await BuildMentionsAsync(
            command.TenantId,
            parentChannelId,
            message.Id,
            command.UserId,
            body,
            now,
            cancellationToken);

        // B-102: replying/being mentioned in a thread auto-follows it, in this same transaction.
        if (threadId is Guid subscribedThreadId)
        {
            await UpsertThreadSubscriptionAsync(
                command.TenantId, subscribedThreadId, parentChannelId, command.UserId,
                ThreadSubscriptionSource.Reply, sequence, now, cancellationToken);

            foreach (var mentionedUserId in mentionContext.MentionedUserIds)
            {
                await UpsertThreadSubscriptionAsync(
                    command.TenantId, subscribedThreadId, parentChannelId, new UserId(mentionedUserId),
                    ThreadSubscriptionSource.Mention, null, now, cancellationToken);
            }
        }

        var result = new MessageSendResult(message.Id, message.Sequence, message.CreatedAt, false);

        var authorName = await dbContext.UserProfiles.AsNoTracking()
            .Where(x => x.Id == command.UserId)
            .Select(x => x.DisplayName)
            .FirstOrDefaultAsync(cancellationToken) ?? command.UserId.Value.ToString();

        var announcementSnapshot = parentChannel.Type == ChannelType.Announcement
            ? new
            {
                messageId = message.Id.Value,
                requiresAcknowledgement = command.RequiresAcknowledgement,
                acknowledgeBy = command.AcknowledgeBy,
                closedAt = (DateTimeOffset?)null,
                acknowledgedByMe = false,
                acknowledgementCount = 0,
                canAcknowledge = false,
                canViewReport = false
            }
            : null;

        outbox.Add(new OutboxMessage
        {
            TenantId = command.TenantId,
            Type = nameof(MessageCreatedEvent),
            Payload = JsonSerializer.Serialize(new
            {
                tenantId = command.TenantId.Value,
                channelId = parentChannelId.Value,
                conversationId = conversationId.Value,
                threadId,
                parentMessageId = threadParentMessageId,
                messageId = command.MessageId.Value,
                // Same UUID the client sent as messageId — reconciles optimistic UI (BUG-001).
                clientMessageId = command.MessageId.Value,
                announcement = announcementSnapshot,
                replyToMessageId = command.ReplyToMessageId?.Value,
                replyTo = replyToPayload,
                authorId = command.UserId.Value,
                authorName,
                authorIsBot = command.AuthorIsBot,
                sequence,
                body,
                createdAt = now,
                mentionedUserIds = mentionContext.MentionedUserIds,
                mentionKinds = mentionContext.MentionKinds,
                attachments = attachments.Select(x => new
                {
                    id = x.Id,
                    fileName = x.FileName,
                    contentType = x.ContentType,
                    sizeBytes = x.SizeBytes,
                    kind = x.Kind.ToString(),
                    durationMs = x.DurationMs,
                    waveform = x.Waveform
                })
            })
        });

        audit.Add(new AuditEvent
        {
            TenantId = command.TenantId,
            ActorUserId = command.UserId,
            Action = AuditActions.MessageSend,
            EntityType = "Message",
            EntityId = command.MessageId.ToString(),
            MetadataJson = JsonSerializer.Serialize(new
            {
                channelId = parentChannelId.Value,
                threadId,
                sequence,
                bodyHash = ComputeHash(body),
                attachmentIds
            })
        });

        AnnouncementPublication.Attach(
            dbContext,
            outbox,
            audit,
            parentChannel.Type,
            command.TenantId,
            parentChannelId,
            message,
            command.UserId,
            command.RequiresAcknowledgement,
            command.AcknowledgeBy,
            now);

        await idempotencyStore.StoreAsync(new IdempotencyRecord(command.TenantId, command.IdempotencyKey, hash, JsonSerializer.Serialize(result), now), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        // RLS txn + SET LOCAL are committed by HTTP middleware / hub filter / worker batch Commit.
        VibeChatMetrics.MessagesSent.Add(1);
        return result;
    }

    public async Task<ForwardMessageResult> ForwardAsync(ForwardMessageCommand command, CancellationToken cancellationToken)
    {
        tenantContext.SetTenant(command.TenantId);
        tenantContext.SetUser(command.UserId);
        await RlsSession.EnsureAppliedAsync(dbContext, tenantContext, cancellationToken);

        var targetIds = (command.TargetChannelIds ?? [])
            .Where(x => x.Value != Guid.Empty)
            .Distinct()
            .Take(MessageForwardPolicies.MaxTargets + 1)
            .ToArray();
        if (targetIds.Length == 0 || targetIds.Length > MessageForwardPolicies.MaxTargets)
        {
            throw new ArgumentException("ForwardTargetCountInvalid");
        }

        var comment = MessageBodyPolicies.Normalize(command.Comment);
        if (!MessageBodyPolicies.IsWithinLimit(comment))
        {
            throw new ArgumentException("MessageBodyTooLong");
        }

        var hash = MessageIdempotency.ComputeForwardRequestHash(command with
        {
            TargetChannelIds = targetIds,
            Comment = comment
        });
        var existing = await idempotencyStore.FindAsync(command.TenantId, command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            var idempotentResult = JsonSerializer.Deserialize<ForwardMessageResult>(existing.ResultJson)!;
            return idempotentResult with { Idempotent = true };
        }

        if (!await permissions.HasPermissionAsync(command.TenantId, command.UserId, Permissions.Message.Send, cancellationToken))
        {
            throw new UnauthorizedAccessException("User cannot send messages.");
        }

        var source = await dbContext.Messages
            .FirstOrDefaultAsync(x => x.Id == command.SourceMessageId && x.TenantId == command.TenantId, cancellationToken);
        if (source is null || source.DeletedAt is not null)
        {
            throw new UnauthorizedAccessException("Source message not accessible.");
        }

        var sourceChannelId = source.ConversationId;
        if (source.ThreadId is Guid sourceThreadId)
        {
            var thread = await dbContext.MessageThreads.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == sourceThreadId && x.TenantId == command.TenantId, cancellationToken);
            if (thread is null)
            {
                throw new UnauthorizedAccessException("Source message not accessible.");
            }

            sourceChannelId = thread.ChannelId;
        }

        var sourceChannel = await dbContext.Channels.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sourceChannelId && x.TenantId == command.TenantId, cancellationToken);
        if (sourceChannel is null || sourceChannel.WorkspaceId != command.WorkspaceId)
        {
            throw new UnauthorizedAccessException("Source message not accessible.");
        }

        if (!await channels.CanAccessAsync(command.TenantId, sourceChannelId, command.UserId, cancellationToken))
        {
            throw new UnauthorizedAccessException("User cannot access source channel.");
        }

        var announcementTargets = new HashSet<ChannelId>();
        foreach (var targetId in targetIds)
        {
            var target = await dbContext.Channels.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == targetId && x.TenantId == command.TenantId, cancellationToken);
            if (target is null
                || target.WorkspaceId != command.WorkspaceId
                || !await channels.CanAccessAsync(command.TenantId, targetId, command.UserId, cancellationToken))
            {
                throw new UnauthorizedAccessException("User cannot forward to one or more target channels.");
            }

            if (target.Type == ChannelType.Announcement)
            {
                if (!await permissions.HasPermissionAsync(command.TenantId, command.UserId, Permissions.Announcement.Publish, cancellationToken))
                {
                    throw new UnauthorizedAccessException("AnnouncementPublishForbidden");
                }

                announcementTargets.Add(targetId);
            }
        }

        var sourceAttachments = await dbContext.Attachments
            .Where(x => x.TenantId == command.TenantId
                && x.MessageId == source.Id
                && x.Status == AttachmentStatus.Ready)
            .OrderBy(x => x.CreatedAt)
            .ToArrayAsync(cancellationToken);

        var body = !MessageBodyPolicies.IsEmpty(comment)
            ? comment
            : (source.DeletedAt is null ? MessageBodyPolicies.Normalize(source.Body) : string.Empty);
        if (MessageBodyPolicies.IsEmpty(body) && sourceAttachments.Length == 0)
        {
            throw new ArgumentException("Message body or attachments are required.");
        }

        var authorName = await dbContext.UserProfiles.AsNoTracking()
            .Where(x => x.Id == command.UserId)
            .Select(x => x.DisplayName)
            .FirstOrDefaultAsync(cancellationToken) ?? command.UserId.Value.ToString();

        var sourceAuthorName = await dbContext.UserProfiles.AsNoTracking()
            .Where(x => x.Id == source.AuthorId)
            .Select(x => x.DisplayName)
            .FirstOrDefaultAsync(cancellationToken) ?? source.AuthorId.Value.ToString();

        var isDirect = sourceChannel.Type == ChannelType.Direct;
        string sourceChannelName;
        if (isDirect)
        {
            var peerName = await dbContext.ChannelMembers.AsNoTracking()
                .Where(x => x.ChannelId == sourceChannelId && x.UserId != command.UserId)
                .Join(
                    dbContext.UserProfiles.AsNoTracking(),
                    m => m.UserId,
                    u => u.Id,
                    (_, u) => u.DisplayName)
                .FirstOrDefaultAsync(cancellationToken);
            sourceChannelName = string.IsNullOrWhiteSpace(peerName) ? "DM" : peerName;
        }
        else
        {
            sourceChannelName = sourceChannel.Name;
        }

        var forwardedFromPayload = new
        {
            messageId = source.Id.Value,
            channelId = sourceChannelId.Value,
            channelName = sourceChannelName,
            authorName = sourceAuthorName,
            createdAt = source.CreatedAt,
            isDirect,
            // B-102: set when forwarding a thread reply back to its own channel ("share to channel") —
            // lets the client render a "shared from thread" link instead of a generic forward card.
            threadId = source.ThreadId
        };

        var now = clock.UtcNow;
        var created = new List<ForwardedMessageResult>(targetIds.Length);

        foreach (var targetId in targetIds)
        {
            var messageId = MessageId.New();
            var sequence = await sequences.NextAsync(command.TenantId, targetId, cancellationToken);
            var message = new Message
            {
                Id = messageId,
                TenantId = command.TenantId,
                ConversationId = targetId,
                Sequence = sequence,
                AuthorId = command.UserId,
                Body = body,
                ForwardedFromMessageId = source.Id,
                ForwardedFromChannelId = sourceChannelId,
                CreatedAt = now
            };
            dbContext.Messages.Add(message);
            if (announcementTargets.Contains(targetId))
            {
                AnnouncementPublication.Attach(
                    dbContext,
                    outbox,
                    audit,
                    ChannelType.Announcement,
                    command.TenantId,
                    targetId,
                    message,
                    command.UserId,
                    requiresAcknowledgement: false,
                    acknowledgeBy: null,
                    now);
            }

            var clonedAttachments = new List<Attachment>(sourceAttachments.Length);
            foreach (var original in sourceAttachments)
            {
                var siblings = await dbContext.Attachments
                    .Where(x => x.TenantId == command.TenantId && x.StorageKey == original.StorageKey)
                    .ToListAsync(cancellationToken);
                var pendingClones = dbContext.ChangeTracker.Entries<Attachment>()
                    .Where(e => e.State == EntityState.Added && e.Entity.StorageKey == original.StorageKey)
                    .Select(e => e.Entity)
                    .ToList();
                var nextCount = AttachmentReferencePolicies.CountAfterAdd(siblings.Count + pendingClones.Count);
                foreach (var sibling in siblings)
                {
                    sibling.ReferenceCount = nextCount;
                }

                foreach (var pending in pendingClones)
                {
                    pending.ReferenceCount = nextCount;
                }

                var clone = new Attachment
                {
                    Id = Guid.NewGuid(),
                    TenantId = command.TenantId,
                    ChannelId = targetId,
                    MessageId = messageId,
                    UploadedBy = original.UploadedBy,
                    FileName = original.FileName,
                    ContentType = original.ContentType,
                    SizeBytes = original.SizeBytes,
                    StorageKey = original.StorageKey,
                    ChecksumSha256 = original.ChecksumSha256,
                    Status = AttachmentStatus.Ready,
                    Kind = original.Kind,
                    ReferenceCount = nextCount,
                    DurationMs = original.DurationMs,
                    Waveform = original.Waveform,
                    ThumbnailKey = original.ThumbnailKey,
                    Width = original.Width,
                    Height = original.Height,
                    ThumbnailStatus = original.ThumbnailStatus,
                    PageCount = original.PageCount,
                    CreatedAt = now,
                    ReadyAt = original.ReadyAt ?? now
                };
                dbContext.Attachments.Add(clone);
                clonedAttachments.Add(clone);
            }

            var mentionContext = await BuildMentionsAsync(
                command.TenantId,
                targetId,
                messageId,
                command.UserId,
                body,
                now,
                cancellationToken);

            outbox.Add(new OutboxMessage
            {
                TenantId = command.TenantId,
                Type = nameof(MessageCreatedEvent),
                Payload = JsonSerializer.Serialize(new
                {
                    tenantId = command.TenantId.Value,
                    channelId = targetId.Value,
                    conversationId = targetId.Value,
                    threadId = (Guid?)null,
                    parentMessageId = (Guid?)null,
                    messageId = messageId.Value,
                    clientMessageId = messageId.Value,
                    replyToMessageId = (Guid?)null,
                    replyTo = (object?)null,
                    forwardedFromMessageId = source.Id.Value,
                    forwardedFromChannelId = sourceChannelId.Value,
                    forwardedFrom = forwardedFromPayload,
                    authorId = command.UserId.Value,
                    authorName,
                    sequence,
                    body,
                    createdAt = now,
                    mentionedUserIds = mentionContext.MentionedUserIds,
                    mentionKinds = mentionContext.MentionKinds,
                    attachments = clonedAttachments.Select(x => new
                    {
                        id = x.Id,
                        fileName = x.FileName,
                        contentType = x.ContentType,
                        sizeBytes = x.SizeBytes,
                        kind = x.Kind.ToString(),
                        durationMs = x.DurationMs,
                        waveform = x.Waveform
                    })
                })
            });

            created.Add(new ForwardedMessageResult(messageId, targetId, sequence, now));
            VibeChatMetrics.MessagesSent.Add(1);
        }

        audit.Add(new AuditEvent
        {
            TenantId = command.TenantId,
            ActorUserId = command.UserId,
            Action = AuditActions.MessageForward,
            EntityType = "Message",
            EntityId = command.SourceMessageId.ToString(),
            MetadataJson = JsonSerializer.Serialize(new
            {
                sourceMessageId = command.SourceMessageId.Value,
                sourceChannelId = sourceChannelId.Value,
                targetChannelIds = targetIds.Select(x => x.Value).ToArray(),
                messageIds = created.Select(x => x.MessageId.Value).ToArray()
            })
        });

        var result = new ForwardMessageResult(created, false);
        await idempotencyStore.StoreAsync(
            new IdempotencyRecord(command.TenantId, command.IdempotencyKey, hash, JsonSerializer.Serialize(result), now),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return result;
    }

    private sealed record MentionBuildResult(
        IReadOnlyList<Guid> MentionedUserIds,
        IReadOnlyList<string> MentionKinds);

    private async Task<MentionBuildResult> BuildMentionsAsync(
        TenantId tenantId,
        ChannelId channelId,
        MessageId messageId,
        UserId authorId,
        string body,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var parsedTokens = MentionTokens.ParseBody(body);
        if (parsedTokens.Count == 0)
        {
            return new MentionBuildResult([], []);
        }

        var memberIds = await dbContext.ChannelMembers.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ChannelId == channelId)
            .Select(x => x.UserId)
            .ToArrayAsync(cancellationToken);
        var memberSet = memberIds.ToHashSet();

        var publicChannel = await dbContext.Channels.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Id == channelId)
            .Select(x => x.Type)
            .FirstOrDefaultAsync(cancellationToken);
        if (publicChannel is ChannelType.Public or ChannelType.Announcement)
        {
            var workspaceMembers = await dbContext.WorkspaceMembers.AsNoTracking()
                .Where(x => x.TenantId == tenantId)
                .Join(
                    dbContext.Channels.AsNoTracking().Where(c => c.Id == channelId),
                    wm => wm.WorkspaceId,
                    ch => ch.WorkspaceId,
                    (wm, _) => wm.UserId)
                .Distinct()
                .ToArrayAsync(cancellationToken);
            memberSet = workspaceMembers.ToHashSet();
        }

        var mentionedUsers = new HashSet<UserId>();
        var mentionKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<MessageMention>();

        foreach (var token in parsedTokens)
        {
            switch (token.Kind)
            {
                case MentionKind.User when token.UserId is { } userId:
                    if (!memberSet.Contains(userId) || userId == authorId)
                    {
                        continue;
                    }

                    mentionKinds.Add(nameof(MentionKind.User));
                    mentionedUsers.Add(userId);
                    rows.Add(new MessageMention
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        MessageId = messageId,
                        ChannelId = channelId,
                        MentionedUserId = userId,
                        Kind = MentionKind.User,
                        CreatedAt = now
                    });
                    break;

                case MentionKind.Here:
                    mentionKinds.Add(nameof(MentionKind.Here));
                    rows.Add(new MessageMention
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        MessageId = messageId,
                        ChannelId = channelId,
                        MentionedUserId = null,
                        Kind = MentionKind.Here,
                        CreatedAt = now
                    });

                    var online = await presence.GetStatusesAsync(tenantId, memberIds, cancellationToken);
                    foreach (var memberId in memberIds)
                    {
                        if (memberId == authorId)
                        {
                            continue;
                        }

                        if (online.TryGetValue(memberId, out var status)
                            && status is PresenceStatus.Online or PresenceStatus.Away
                            && mentionedUsers.Add(memberId))
                        {
                            rows.Add(new MessageMention
                            {
                                Id = Guid.NewGuid(),
                                TenantId = tenantId,
                                MessageId = messageId,
                                ChannelId = channelId,
                                MentionedUserId = memberId,
                                Kind = MentionKind.User,
                                CreatedAt = now
                            });
                        }
                    }
                    break;

                case MentionKind.Channel:
                    if (!await permissions.HasPermissionAsync(tenantId, authorId, Permissions.Channel.MentionAll, cancellationToken))
                    {
                        throw new MentionAllForbiddenException();
                    }

                    mentionKinds.Add(nameof(MentionKind.Channel));
                    rows.Add(new MessageMention
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        MessageId = messageId,
                        ChannelId = channelId,
                        MentionedUserId = null,
                        Kind = MentionKind.Channel,
                        CreatedAt = now
                    });

                    foreach (var memberId in memberIds)
                    {
                        if (memberId == authorId)
                        {
                            continue;
                        }

                        if (mentionedUsers.Add(memberId))
                        {
                            rows.Add(new MessageMention
                            {
                                Id = Guid.NewGuid(),
                                TenantId = tenantId,
                                MessageId = messageId,
                                ChannelId = channelId,
                                MentionedUserId = memberId,
                                Kind = MentionKind.User,
                                CreatedAt = now
                            });
                        }
                    }
                    break;
            }
        }

        if (rows.Count > 0)
        {
            dbContext.MessageMentions.AddRange(rows);
        }

        return new MentionBuildResult(
            mentionedUsers.Select(x => x.Value).ToArray(),
            mentionKinds.ToArray());
    }

    /// <summary>
    /// B-102: find-or-create a thread follow. Never lowers <see cref="ThreadSubscription.LastReadSeq"/> —
    /// callers pass null when the event (e.g. a mention) shouldn't mark anything as read.
    /// </summary>
    private async Task UpsertThreadSubscriptionAsync(
        TenantId tenantId,
        Guid threadId,
        ChannelId channelId,
        UserId userId,
        ThreadSubscriptionSource source,
        long? bumpLastReadSeqTo,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.ThreadSubscriptions
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ThreadId == threadId && x.UserId == userId, cancellationToken);
        if (existing is null)
        {
            dbContext.ThreadSubscriptions.Add(new ThreadSubscription
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                ThreadId = threadId,
                ChannelId = channelId,
                Source = source,
                LastReadSeq = bumpLastReadSeqTo ?? 0,
                CreatedAt = now
            });
            return;
        }

        if (bumpLastReadSeqTo is long seq && seq > existing.LastReadSeq)
        {
            existing.LastReadSeq = seq;
        }
    }

    private static string ComputeHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string TruncateReplyPreview(string body, int maxLength = 140)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var flat = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (flat.Length <= maxLength)
        {
            return flat;
        }

        return flat[..(maxLength - 1)] + "…";
    }
}
