using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeChat.Api;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Files;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using static VibeChat.Api.Endpoints.ConversationsEndpointHelpers;
using static VibeChat.Api.Endpoints.FilesEndpointHelpers;
using static VibeChat.Api.Endpoints.IdentityEndpointHelpers;

namespace VibeChat.Api.Endpoints;

internal static class MessagingEndpointHelpers
{
    internal static async Task<Message?> FindDirectChannelMessageAsync(
        VibeChatDbContext db,
        ChannelId channelId,
        MessageId messageId,
        CancellationToken ct) =>
        await db.Messages.FirstOrDefaultAsync(
            x => x.Id == messageId && x.ConversationId == channelId,
            ct);

    internal static Task<int> CountPinsAsync(VibeChatDbContext db, ChannelId channelId, CancellationToken ct) =>
        db.PinnedMessages.CountAsync(x => x.ChannelId == channelId, ct);

    internal static object? ValidateSavedNote(string? note, out string? normalized)
    {
        normalized = null;
        if (note is null)
        {
            return null;
        }

        var trimmed = note.Trim();
        if (trimmed.Length == 0)
        {
            normalized = null;
            return null;
        }

        if (trimmed.Length > SavedMessagePolicies.MaxNoteLength)
        {
            return new
            {
                error = "NoteTooLong",
                message = $"Note may be at most {SavedMessagePolicies.MaxNoteLength} characters.",
                max = SavedMessagePolicies.MaxNoteLength
            };
        }

        normalized = trimmed;
        return null;
    }

    internal static bool TryParseSavedCursor(string? cursor, out DateTimeOffset? createdAt, out Guid? id)
    {
        createdAt = null;
        id = null;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return true;
        }

        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split('|', 2);
            if (parts.Length != 2
                || !DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                || !Guid.TryParse(parts[1], out var guid))
            {
                return false;
            }

            createdAt = at;
            id = guid;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal static string EncodeSavedCursor(DateTimeOffset createdAt, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{createdAt.ToString("O", CultureInfo.InvariantCulture)}|{id:D}"));

    /// <summary>
    /// B-102 followed-threads list: a personal, attention-bounded list, so an opaque offset cursor
    /// (over an in-memory sort by last activity) is simpler than a DB-level keyset and cheap enough.
    /// </summary>
    internal static bool TryParseOffsetCursor(string? cursor, out int offset)
    {
        offset = 0;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return true;
        }

        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset) && offset >= 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal static string EncodeOffsetCursor(int offset) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(offset.ToString(CultureInfo.InvariantCulture)));

    internal static async Task<(Message Message, Channel Channel)?> ResolveReadableMessageAsync(
        VibeChatDbContext db,
        ITenantContext tenant,
        Workspace workspace,
        UserId userId,
        MessageId messageId,
        CancellationToken ct)
    {
        var message = await db.Messages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == messageId, ct);
        if (message is null || message.TenantId != workspace.TenantId)
        {
            return null;
        }

        ChannelId channelId;
        if (message.ThreadId is Guid threadId)
        {
            var thread = await db.MessageThreads.AsNoTracking().FirstOrDefaultAsync(x => x.Id == threadId, ct);
            if (thread is null)
            {
                return null;
            }

            channelId = thread.ChannelId;
        }
        else
        {
            channelId = message.ConversationId;
        }

        var channel = await ResolveChannelAsync(channelId, userId, db, tenant, ct);
        if (channel is null || channel.WorkspaceId != workspace.Id)
        {
            return null;
        }

        return (message, channel);
    }

    internal static async Task<SavedMessageResponse> ToSavedMessageResponseAsync(
        VibeChatDbContext db,
        SavedMessage saved,
        Message message,
        Channel channel,
        UserId viewerId,
        CancellationToken ct)
    {
        var removed = message.DeletedAt is not null;
        var authorName = string.Empty;
        if (!removed)
        {
            authorName = await db.UserProfiles.AsNoTracking()
                .Where(x => x.Id == message.AuthorId)
                .Select(x => x.DisplayName)
                .FirstOrDefaultAsync(ct) ?? string.Empty;
        }

        var channelName = await ResolveSavedChannelDisplayNameAsync(db, channel, viewerId, ct);

        return new SavedMessageResponse(
            saved.MessageId.Value,
            saved.ChannelId.Value,
            channelName,
            channel.Type.ToString(),
            message.Sequence,
            message.AuthorId.Value,
            authorName,
            removed ? "Mensagem removida" : TruncateReplyPreview(message.Body),
            saved.Note,
            saved.CompletedAt,
            saved.CreatedAt,
            removed);
    }

    internal static async Task<string> ResolveSavedChannelDisplayNameAsync(
        VibeChatDbContext db,
        Channel channel,
        UserId viewerId,
        CancellationToken ct)
    {
        if (channel.Type != ChannelType.Direct)
        {
            return channel.Name;
        }

        var peers = await ResolveDirectPeersAsync([channel], viewerId, db, ct);
        if (peers.TryGetValue(channel.Id, out var peer) && !string.IsNullOrWhiteSpace(peer.DisplayName))
        {
            return peer.DisplayName;
        }

        return "DM";
    }

    internal static Task EmitPinChangedAsync(
        IOutboxWriter outbox,
        TenantId tenantId,
        ChannelId channelId,
        MessageId messageId,
        UserId byUserId,
        bool pinned)
    {
        outbox.Add(new OutboxMessage
        {
            TenantId = tenantId,
            Type = nameof(PinChangedEvent),
            Payload = JsonSerializer.Serialize(new
            {
                tenantId = tenantId.Value,
                channelId = channelId.Value,
                messageId = messageId.Value,
                pinned,
                byUserId = byUserId.Value
            })
        });
        return Task.CompletedTask;
    }

    internal static async Task AppendPinSystemEventAsync(
        VibeChatDbContext db,
        IConversationSequenceStore sequences,
        IOutboxWriter outbox,
        IClock clock,
        TenantId tenantId,
        ChannelId channelId,
        UserId actorId,
        string actorName,
        MessageId targetMessageId,
        bool pinned,
        CancellationToken ct)
    {
        var systemMessageId = new MessageId(Guid.NewGuid());
        var sequence = await sequences.NextAsync(tenantId, channelId, ct);
        var now = clock.UtcNow;
        var body = pinned
            ? SystemEventTokens.PinBody(targetMessageId)
            : SystemEventTokens.UnpinBody(targetMessageId);

        db.Messages.Add(new Message
        {
            Id = systemMessageId,
            TenantId = tenantId,
            ConversationId = channelId,
            Sequence = sequence,
            AuthorId = actorId,
            Body = body,
            CreatedAt = now
        });

        outbox.Add(new OutboxMessage
        {
            TenantId = tenantId,
            Type = nameof(MessageCreatedEvent),
            Payload = JsonSerializer.Serialize(new
            {
                tenantId = tenantId.Value,
                channelId = channelId.Value,
                conversationId = channelId.Value,
                messageId = systemMessageId.Value,
                clientMessageId = systemMessageId.Value,
                authorId = actorId.Value,
                authorName = actorName,
                sequence,
                body,
                createdAt = now,
                mentionedUserIds = Array.Empty<Guid>(),
                mentionKinds = Array.Empty<string>(),
                attachments = Array.Empty<object>()
            })
        });
    }

    internal static async Task<Message?> FindMessageInChannelAsync(
        VibeChatDbContext db,
        ChannelId channelId,
        MessageId messageId,
        CancellationToken ct)
    {
        var message = await db.Messages.FirstOrDefaultAsync(x => x.Id == messageId, ct);
        if (message is null)
        {
            return null;
        }

        if (message.ConversationId == channelId)
        {
            return message;
        }

        if (message.ThreadId is not Guid threadId)
        {
            return null;
        }

        var belongs = await db.MessageThreads.AnyAsync(
            x => x.Id == threadId && x.ChannelId == channelId,
            ct);
        return belongs ? message : null;
    }

    internal static string TruncateReplyPreview(string body, int maxLength = 140)
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

    internal static async Task<Dictionary<Guid, ReplyToResponse>> LoadReplyToByIdsAsync(
        VibeChatDbContext db,
        IEnumerable<MessageId?> replyToIds,
        CancellationToken ct)
    {
        var ids = replyToIds
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, ReplyToResponse>();
        }

        var rows = await (
            from m in db.Messages.AsNoTracking()
            where ids.Contains(m.Id)
            join u in db.UserProfiles on m.AuthorId equals u.Id into authors
            from u in authors.DefaultIfEmpty()
            select new
            {
                m.Id,
                m.Body,
                m.DeletedAt,
                AuthorName = u != null ? u.DisplayName : m.AuthorId.Value.ToString()
            }).ToArrayAsync(ct);

        return rows.ToDictionary(
            x => x.Id.Value,
            x => new ReplyToResponse(
                x.Id.Value,
                x.DeletedAt is null ? x.AuthorName : string.Empty,
                x.DeletedAt is null ? TruncateReplyPreview(x.Body) : string.Empty,
                x.DeletedAt is not null));
    }

    internal static async Task<Dictionary<Guid, ForwardedFromResponse>> LoadForwardedFromByIdsAsync(
        VibeChatDbContext db,
        IEnumerable<(MessageId? MessageId, ChannelId? ChannelId)> pairs,
        UserId currentUserId,
        CancellationToken ct)
    {
        var list = pairs
            .Where(x => x.MessageId is not null && x.ChannelId is not null)
            .Select(x => (MessageId: x.MessageId!.Value, ChannelId: x.ChannelId!.Value))
            .GroupBy(x => x.MessageId.Value)
            .Select(g => g.First())
            .ToArray();
        if (list.Length == 0)
        {
            return new Dictionary<Guid, ForwardedFromResponse>();
        }

        var messageIds = list.Select(x => x.MessageId).Distinct().ToArray();
        var channelIds = list.Select(x => x.ChannelId).Distinct().ToArray();

        var messages = await (
            from m in db.Messages.AsNoTracking().IgnoreQueryFilters()
            where messageIds.Contains(m.Id)
            join u in db.UserProfiles.IgnoreQueryFilters() on m.AuthorId equals u.Id into authors
            from u in authors.DefaultIfEmpty()
            select new
            {
                m.Id,
                m.CreatedAt,
                m.ThreadId,
                AuthorName = u != null ? u.DisplayName : m.AuthorId.Value.ToString()
            }).ToDictionaryAsync(x => x.Id.Value, ct);

        var channels = await db.Channels.AsNoTracking().IgnoreQueryFilters()
            .Where(c => channelIds.Contains(c.Id))
            .ToListAsync(ct);
        var channelById = channels.ToDictionary(c => c.Id.Value);
        var peerByChannel = await ResolveDirectPeersAsync(channels, currentUserId, db, ct);

        var result = new Dictionary<Guid, ForwardedFromResponse>();
        foreach (var item in list)
        {
            messages.TryGetValue(item.MessageId.Value, out var msg);
            channelById.TryGetValue(item.ChannelId.Value, out var channel);
            var isDirect = channel?.Type == ChannelType.Direct;
            string channelName;
            if (channel is null)
            {
                channelName = item.ChannelId.Value.ToString();
            }
            else if (isDirect)
            {
                channelName = peerByChannel.TryGetValue(channel.Id, out var peer)
                    ? peer.DisplayName
                    : "DM";
            }
            else
            {
                channelName = channel.Name;
            }

            result[item.MessageId.Value] = new ForwardedFromResponse(
                item.MessageId.Value,
                item.ChannelId.Value,
                channelName,
                msg?.AuthorName ?? string.Empty,
                msg?.CreatedAt ?? default,
                isDirect,
                msg?.ThreadId);
        }

        return result;
    }

    internal static async Task<Dictionary<Guid, AttachmentResponse[]>> LoadAttachmentsByMessageAsync(
        VibeChatDbContext db,
        ChannelId channelId,
        IReadOnlyCollection<MessageId> messageIds,
        CancellationToken ct)
    {
        if (messageIds.Count == 0)
        {
            return new Dictionary<Guid, AttachmentResponse[]>();
        }

        var wanted = messageIds.Select(x => x.Value).ToHashSet();
        var rows = await db.Attachments.AsNoTracking()
            .Where(x => x.ChannelId == channelId && x.MessageId != null && x.Status == AttachmentStatus.Ready)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

        return rows
            .Where(x => x.MessageId is not null && wanted.Contains(x.MessageId.Value.Value))
            .GroupBy(x => x.MessageId!.Value.Value)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => ToAttachmentResponse(x)).ToArray());
    }

    internal static async Task<ChannelMessagesResponse> ListChannelMessagesAsync(
        Channel channel,
        UserProfile profile,
        VibeChatDbContext db,
        int take,
        long? after,
        long? before,
        long? around,
        bool canVote,
        long minSeq,
        CancellationToken ct)
    {
        ChannelMessageRow[] rows;
        var hasMoreBefore = false;
        var hasMoreAfter = false;

        if (around.HasValue)
        {
            var anchor = around.Value;
            var afterCount = take / 2;
            var beforeCount = Math.Max(0, take - afterCount - 1);

            var beforeFetched = await QueryChannelMessageRowsAsync(
                db,
                channel.Id,
                minSeq,
                q => q.Where(m => m.Sequence <= anchor).OrderByDescending(m => m.Sequence).Take(beforeCount + 1),
                ct);
            hasMoreBefore = beforeFetched.Length > beforeCount;
            var beforeRows = hasMoreBefore ? beforeFetched.Take(beforeCount).ToArray() : beforeFetched;
            Array.Reverse(beforeRows);

            var afterFetched = await QueryChannelMessageRowsAsync(
                db,
                channel.Id,
                minSeq,
                q => q.Where(m => m.Sequence > anchor).OrderBy(m => m.Sequence).Take(afterCount + 1),
                ct);
            hasMoreAfter = afterFetched.Length > afterCount;
            var afterRows = hasMoreAfter ? afterFetched.Take(afterCount).ToArray() : afterFetched;

            rows = beforeRows.Concat(afterRows).OrderBy(x => x.Sequence).ToArray();
            if (rows.Length > 0)
            {
                var pageMin = rows[0].Sequence;
                var maxSeq = rows[rows.Length - 1].Sequence;
                hasMoreBefore = hasMoreBefore || await db.Messages.AsNoTracking()
                    .AnyAsync(m => m.ConversationId == channel.Id && m.Sequence < pageMin && m.Sequence > minSeq, ct);
                hasMoreAfter = hasMoreAfter || await db.Messages.AsNoTracking()
                    .AnyAsync(m => m.ConversationId == channel.Id && m.Sequence > maxSeq, ct);
            }
        }
        else if (before.HasValue)
        {
            var fetched = await QueryChannelMessageRowsAsync(
                db,
                channel.Id,
                minSeq,
                q => q.Where(m => m.Sequence < before.Value).OrderByDescending(m => m.Sequence).Take(take + 1),
                ct);
            hasMoreBefore = fetched.Length > take;
            rows = (hasMoreBefore ? fetched.Take(take) : fetched).Reverse().ToArray();
            if (rows.Length > 0)
            {
                var maxSeq = rows[rows.Length - 1].Sequence;
                hasMoreAfter = await db.Messages.AsNoTracking()
                    .AnyAsync(m => m.ConversationId == channel.Id && m.Sequence > maxSeq, ct);
            }
        }
        else if (after.HasValue)
        {
            var fetched = await QueryChannelMessageRowsAsync(
                db,
                channel.Id,
                minSeq,
                q => q.Where(m => m.Sequence > after.Value).OrderBy(m => m.Sequence).Take(take + 1),
                ct);
            hasMoreAfter = fetched.Length > take;
            rows = (hasMoreAfter ? fetched.Take(take) : fetched).ToArray();
            if (rows.Length > 0)
            {
                hasMoreBefore = await db.Messages.AsNoTracking()
                    .AnyAsync(m => m.ConversationId == channel.Id && m.Sequence < rows[0].Sequence && m.Sequence > minSeq, ct);
            }
        }
        else
        {
            var fetched = await QueryChannelMessageRowsAsync(
                db,
                channel.Id,
                minSeq,
                q => q.OrderByDescending(m => m.Sequence).Take(take + 1),
                ct);
            hasMoreBefore = fetched.Length > take;
            rows = (hasMoreBefore ? fetched.Take(take) : fetched).Reverse().ToArray();
            hasMoreAfter = false;
        }

        var messageIds = rows.Select(x => x.Id).ToArray();
        var threadIds = rows.Where(x => x.ThreadId is not null).Select(x => x.ThreadId!.Value).Distinct().ToArray();
        var replyCounts = threadIds.Length == 0
            ? new Dictionary<Guid, int>()
            : await db.Messages.AsNoTracking()
                .Where(m => m.ThreadId != null && threadIds.Contains(m.ThreadId.Value) && m.ConversationId != channel.Id)
                .GroupBy(m => m.ThreadId!.Value)
                .Select(g => new { ThreadId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ThreadId, x => x.Count, ct);

        var attachmentsByMessage = await LoadAttachmentsByMessageAsync(db, channel.Id, messageIds, ct);
        var reactionsByMessage = await LoadReactionSummariesByMessageAsync(db, messageIds, profile.Id, ct);
        var replyToById = await LoadReplyToByIdsAsync(db, rows.Select(x => x.ReplyToMessageId), ct);
        var forwardedFromById = await LoadForwardedFromByIdsAsync(
            db,
            rows.Select(x => (x.ForwardedFromMessageId, x.ForwardedFromChannelId)),
            profile.Id,
            ct);
        var linkPreviewByMessage = await LoadLinkPreviewsByMessageAsync(db, messageIds, ct);
        var pollsByMessage = await PollQuery.LoadByMessageIdsAsync(db, messageIds, profile.Id, canVote, includeVoters: true, ct);
        var pinnedIds = messageIds.Length == 0
            ? new HashSet<Guid>()
            : (await db.PinnedMessages.AsNoTracking()
                .Where(x => x.ChannelId == channel.Id && messageIds.Contains(x.MessageId))
                .Select(x => x.MessageId.Value)
                .ToListAsync(ct)).ToHashSet();

        var messages = rows.Select(x => new MessageResponse(
            x.Id.Value,
            x.ChannelId.Value,
            x.Sequence,
            x.AuthorId.Value,
            x.DeletedAt == null ? x.Body : string.Empty,
            x.CreatedAt,
            x.EditedAt,
            x.DeletedAt,
            x.AuthorName,
            x.DeletedAt == null && attachmentsByMessage.TryGetValue(x.Id.Value, out var atts) ? atts : [],
            x.ThreadId,
            x.ReplyToMessageId?.Value,
            x.ThreadId is Guid tid && replyCounts.TryGetValue(tid, out var count) ? count : 0,
            x.ChannelId.Value,
            x.DeletedAt == null && reactionsByMessage.TryGetValue(x.Id.Value, out var rx) ? rx : [],
            x.ReplyToMessageId is MessageId rtid && replyToById.TryGetValue(rtid.Value, out var replyTo) ? replyTo : null,
            x.ForwardedFromMessageId?.Value,
            x.ForwardedFromChannelId?.Value,
            x.ForwardedFromMessageId is MessageId ffid && forwardedFromById.TryGetValue(ffid.Value, out var forwarded) ? forwarded : null,
            x.DeletedAt == null && linkPreviewByMessage.TryGetValue(x.Id.Value, out var preview) ? preview : null,
            pinnedIds.Contains(x.Id.Value),
            x.DeletedAt == null && pollsByMessage.TryGetValue(x.Id.Value, out var poll) ? poll : null)).ToArray();

        return new ChannelMessagesResponse(messages, hasMoreBefore, hasMoreAfter);
    }

    internal static async Task<Dictionary<Guid, LinkPreviewResponse>> LoadLinkPreviewsByMessageAsync(
        VibeChatDbContext db,
        IReadOnlyCollection<MessageId> messageIds,
        CancellationToken ct)
    {
        if (messageIds.Count == 0)
        {
            return new Dictionary<Guid, LinkPreviewResponse>();
        }

        // Materialize as List so EF translates Contains (HashSet.Contains is not SQL-translatable).
        var wantedIds = messageIds.ToList();
        var rows = await (
            from link in db.MessageLinkPreviews.AsNoTracking()
            join preview in db.LinkPreviews.AsNoTracking() on link.LinkPreviewId equals preview.Id
            where link.RemovedAt == null
                  && preview.Status == LinkPreviewStatus.Ready
                  && wantedIds.Contains(link.MessageId)
            select new { link.MessageId, Preview = preview }
        ).ToListAsync(ct);

        return rows
            .GroupBy(x => x.MessageId.Value)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var preview = g.First().Preview;
                    return new LinkPreviewResponse(
                        preview.Id,
                        preview.Url,
                        preview.Title,
                        preview.Description,
                        preview.SiteName,
                        !string.IsNullOrWhiteSpace(preview.ImageKey),
                        preview.Status.ToString());
                });
    }

    internal static async Task<ChannelMessageRow[]> QueryChannelMessageRowsAsync(
        VibeChatDbContext db,
        ChannelId conversationId,
        long minSeq,
        Func<IQueryable<Message>, IQueryable<Message>> shape,
        CancellationToken ct)
    {
        var shaped = shape(db.Messages.Where(m => m.ConversationId == conversationId && m.Sequence > minSeq));
        return await (
            from m in shaped
            join u in db.UserProfiles on m.AuthorId equals u.Id into authors
            from u in authors.DefaultIfEmpty()
            select new ChannelMessageRow(
                m.Id,
                m.ConversationId,
                m.Sequence,
                m.AuthorId,
                m.Body,
                m.CreatedAt,
                m.EditedAt,
                m.DeletedAt,
                m.ThreadId,
                m.ReplyToMessageId,
                m.ForwardedFromMessageId,
                m.ForwardedFromChannelId,
                u != null ? u.DisplayName : m.AuthorId.Value.ToString()))
            .ToArrayAsync(ct);
    }

    internal static async Task<Dictionary<Guid, ReactionSummaryResponse[]>> LoadReactionSummariesByMessageAsync(
        VibeChatDbContext db,
        IReadOnlyCollection<MessageId> messageIds,
        UserId currentUserId,
        CancellationToken ct)
    {
        if (messageIds.Count == 0)
        {
            return new Dictionary<Guid, ReactionSummaryResponse[]>();
        }

        var wanted = messageIds.ToHashSet();
        var rows = await db.Reactions.AsNoTracking()
            .Where(x => wanted.Contains(x.MessageId))
            .ToListAsync(ct);

        return rows
            .GroupBy(x => x.MessageId.Value)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => x.Emoji)
                    .OrderBy(x => x.Key, StringComparer.Ordinal)
                    .Select(emojiGroup => new ReactionSummaryResponse(
                        emojiGroup.Key,
                        emojiGroup.Count(),
                        emojiGroup.Any(x => x.UserId == currentUserId)))
                    .ToArray());
    }

    internal static async Task<ReactionSnapshot[]> BuildReactionSnapshotAsync(
        VibeChatDbContext db,
        MessageId messageId,
        UserId currentUserId,
        string toggledEmoji,
        bool added,
        CancellationToken ct)
    {
        var rows = await db.Reactions.AsNoTracking()
            .Where(x => x.MessageId == messageId)
            .ToListAsync(ct);

        // Reflect in-memory toggle before SaveChanges for the response/outbox payload.
        if (added)
        {
            rows.Add(new Reaction
            {
                Id = Guid.NewGuid(),
                MessageId = messageId,
                UserId = currentUserId,
                Emoji = toggledEmoji
            });
        }
        else
        {
            rows = rows
                .Where(x => !(x.Emoji == toggledEmoji && x.UserId == currentUserId))
                .ToList();
        }

        return rows
            .GroupBy(x => x.Emoji)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(g => new ReactionSnapshot(
                g.Key,
                g.Count(),
                g.Select(x => x.UserId.Value).Distinct().ToArray()))
            .ToArray();
    }
}
