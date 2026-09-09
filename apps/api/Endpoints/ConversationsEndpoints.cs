using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeChat.Api;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Directory;
using VibeChat.Files;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.Realtime;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using static VibeChat.Api.Endpoints.ConversationsEndpointHelpers;
using static VibeChat.Api.Endpoints.IdentityEndpointHelpers;
using static VibeChat.Api.Endpoints.MessagingEndpointHelpers;

namespace VibeChat.Api.Endpoints;

internal static class ConversationsEndpoints
{
    internal static void MapChannelLists(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/channels", async (Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var memberChannelIds = await db.ChannelMembers
                .Where(x => x.UserId == profile.Id)
                .Select(x => x.ChannelId)
                .ToListAsync(ct);

            var channels = await db.Channels
                .Where(x => x.WorkspaceId == workspace.Id
                    && (x.Type == ChannelType.Public || x.Type == ChannelType.Announcement || memberChannelIds.Contains(x.Id)))
                .OrderBy(x => x.Type == ChannelType.Direct || x.Type == ChannelType.GroupDm ? 1 : 0)
                .ThenBy(x => x.Name)
                .ToListAsync(ct);

            var peerByChannel = await ResolveDirectPeersAsync(channels, profile.Id, db, ct);
            var groupByChannel = await GroupDmEndpoints.ResolveInfosAsync(channels, profile.Id, db, ct);
            var response = channels.Select(x =>
            {
                peerByChannel.TryGetValue(x.Id, out var peer);
                groupByChannel.TryGetValue(x.Id, out var group);
                var displayName = x.Type == ChannelType.Direct && peer is not null
                    ? peer.DisplayName
                    : group.Names is { Length: > 0 }
                        ? group.DisplayName
                        : x.Name;
                return new ChannelResponse(
                    x.Id.Value,
                    x.WorkspaceId.Value,
                    displayName,
                    x.Type.ToString(),
                    peer?.UserId.Value,
                    peer?.DisplayName,
                    x.SpaceId,
                    x.Topic,
                    group.Count == 0 ? null : group.Count,
                    group.Names is { Length: > 0 } ? group.Names : null,
                    group.UserIds is { Length: > 0 } ? group.UserIds : null);
            }).ToArray();
            return Results.Ok(response);
        });

        v1.MapGet("/workspaces/{workspaceId:guid}/channels/unread", async (Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var memberChannelIds = await db.ChannelMembers
                .Where(x => x.UserId == profile.Id)
                .Select(x => x.ChannelId)
                .ToListAsync(ct);

            var channels = await db.Channels
                .Where(x => x.WorkspaceId == workspace.Id
                    && (x.Type == ChannelType.Public || x.Type == ChannelType.Announcement || memberChannelIds.Contains(x.Id)))
                .Select(x => x.Id)
                .ToListAsync(ct);

            var cursors = await db.ReadCursors
                .Where(x => x.UserId == profile.Id && channels.Contains(x.ChannelId))
                .ToDictionaryAsync(x => x.ChannelId, x => x.LastReadSequence, ct);
            var joinedByChannel = await db.ChannelMembers
                .Where(x => x.UserId == profile.Id && channels.Contains(x.ChannelId))
                .ToDictionaryAsync(x => x.ChannelId, x => x.JoinedSeq, ct);

            var summaries = new List<ChannelUnreadSummaryResponse>(channels.Count);
            foreach (var channelId in channels)
            {
                var lastRead = cursors.TryGetValue(channelId, out var seq) ? seq : 0L;
                var joinedSeq = joinedByChannel.TryGetValue(channelId, out var joined) ? joined : 0L;
                var floor = Math.Max(lastRead, joinedSeq);
                var unreadCount = await db.Messages.CountAsync(
                    x => x.ConversationId == channelId && x.Sequence > floor && x.DeletedAt == null,
                    ct);
                var mentionCount = await (
                    from mention in db.MessageMentions.AsNoTracking()
                    join message in db.Messages.AsNoTracking() on mention.MessageId equals message.Id
                    where mention.ChannelId == channelId
                        && mention.MentionedUserId == profile.Id
                        && message.Sequence > floor
                        && message.DeletedAt == null
                    select mention.MessageId
                ).Distinct().CountAsync(ct);
                summaries.Add(new ChannelUnreadSummaryResponse(
                    channelId.Value,
                    unreadCount,
                    mentionCount,
                    lastRead));
            }

            return Results.Ok(summaries);
        });
    }

    internal static void MapChannelMembers(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/channels/{channelId:guid}/members", async (
            Guid workspaceId,
            Guid channelId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null || channel.WorkspaceId != new WorkspaceId(workspaceId))
            {
                return Results.Forbid();
            }

            var query = http.Request.Query["query"].ToString().Trim();
            var queryLower = query.ToLowerInvariant();

            var members = channel.Type is ChannelType.Public or ChannelType.Announcement
                ? await (
                    from m in db.WorkspaceMembers.AsNoTracking()
                    where m.WorkspaceId == channel.WorkspaceId
                    join u in db.UserProfiles.AsNoTracking() on m.UserId equals u.Id
                    orderby u.DisplayName
                    select new ChannelMemberResponse(u.Id.Value, u.DisplayName, u.Email)
                ).ToArrayAsync(ct)
                : await (
                    from cm in db.ChannelMembers.AsNoTracking()
                    where cm.ChannelId == channel.Id
                    join u in db.UserProfiles.AsNoTracking() on cm.UserId equals u.Id
                    orderby u.DisplayName
                    select new ChannelMemberResponse(u.Id.Value, u.DisplayName, u.Email)
                ).ToArrayAsync(ct);

            if (!string.IsNullOrWhiteSpace(queryLower))
            {
                members = members
                    .Where(x =>
                        x.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || x.Email.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .Take(8)
                    .ToArray();
            }
            else
            {
                members = members.Take(8).ToArray();
            }

            return Results.Ok(members);
        });
    }

    internal static void MapChannelCreation(this RouteGroupBuilder v1)
    {
        v1.MapPost("/workspaces/{workspaceId:guid}/dms", async (Guid workspaceId, OpenDirectMessageRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            if (request.UserId == Guid.Empty || request.UserId == profile.Id.Value)
            {
                return Results.BadRequest(new { error = "userId must reference another workspace member." });
            }

            var peerId = new UserId(request.UserId);
            var peerMember = await db.WorkspaceMembers.FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.UserId == peerId, ct);
            var peerProfile = await db.UserProfiles.FirstOrDefaultAsync(x => x.Id == peerId, ct);
            if (peerMember is null || peerProfile is null)
            {
                return Results.NotFound(new { error = "Peer user is not a member of this workspace." });
            }

            var existing = await FindDirectChannelAsync(workspace.Id, profile.Id, peerId, db, ct);
            if (existing is not null)
            {
                return Results.Ok(new ChannelResponse(existing.Id.Value, existing.WorkspaceId.Value, peerProfile.DisplayName, existing.Type.ToString(), peerProfile.Id.Value, peerProfile.DisplayName, Topic: existing.Topic));
            }

            var channel = new Channel
            {
                Id = ChannelId.New(),
                TenantId = workspace.TenantId,
                WorkspaceId = workspace.Id,
                Name = BuildDirectChannelName(profile.Id, peerId),
                Type = ChannelType.Direct,
                CreatedAt = clock.UtcNow,
                CreatedBy = profile.Id
            };
            db.Channels.Add(channel);
            db.ChannelMembers.AddRange(
                new ChannelMember { Id = Guid.NewGuid(), TenantId = workspace.TenantId, ChannelId = channel.Id, UserId = profile.Id, JoinedAt = clock.UtcNow },
                new ChannelMember { Id = Guid.NewGuid(), TenantId = workspace.TenantId, ChannelId = channel.Id, UserId = peerId, JoinedAt = clock.UtcNow });
            await db.SaveChangesAsync(ct);

            return Results.Created(
                $"/api/v1/channels/{channel.Id.Value}",
                new ChannelResponse(channel.Id.Value, channel.WorkspaceId.Value, peerProfile.DisplayName, channel.Type.ToString(), peerProfile.Id.Value, peerProfile.DisplayName));
        }).AllowPermissionGateExempt("membership-only open DM (B-021)");

        GroupDmEndpoints.Map(v1);

        v1.MapPost("/workspaces/{workspaceId:guid}/channels", async (Guid workspaceId, CreateChannelRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IAuditWriter audit, IClock clock, CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var name = (request.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.BadRequest(new { error = "name is required." });
            }

            if (Enum.TryParse<ChannelType>(request.Type, true, out var parsed) && parsed is ChannelType.Direct or ChannelType.GroupDm)
            {
                return Results.BadRequest(new
                {
                    error = parsed is ChannelType.GroupDm
                        ? "Use POST /workspaces/{id}/group-dms to open group direct messages."
                        : "Use POST /workspaces/{id}/dms to open direct messages.",
                });
            }

            Guid? spaceId = null;
            if (request.SpaceId is Guid requestedSpaceId)
            {
                var space = await db.Spaces.FirstOrDefaultAsync(x => x.Id == requestedSpaceId && x.WorkspaceId == workspace.Id, ct);
                if (space is null)
                {
                    return Results.BadRequest(new { error = "spaceId must reference a space in this workspace." });
                }

                spaceId = space.Id;
            }

            var channel = new Channel
            {
                Id = ChannelId.New(),
                TenantId = workspace.TenantId,
                WorkspaceId = workspace.Id,
                SpaceId = spaceId,
                Name = name,
                Type = Enum.TryParse<ChannelType>(request.Type, true, out var type) ? type : ChannelType.Public,
                CreatedAt = clock.UtcNow,
                CreatedBy = profile.Id
            };
            db.Channels.Add(channel);
            db.ChannelMembers.Add(new ChannelMember { Id = Guid.NewGuid(), TenantId = workspace.TenantId, ChannelId = channel.Id, UserId = profile.Id, JoinedAt = clock.UtcNow });
            audit.Add(new AuditEvent
            {
                TenantId = workspace.TenantId,
                ActorUserId = profile.Id,
                Action = AuditActions.ChannelCreate,
                EntityType = "Channel",
                EntityId = channel.Id.ToString(),
                MetadataJson = JsonSerializer.Serialize(new { workspaceId, type = channel.Type.ToString(), spaceId })
            });
            await db.SaveChangesAsync(ct);
            return Results.Created(
                $"/api/v1/channels/{channel.Id.Value}",
                new ChannelResponse(channel.Id.Value, channel.WorkspaceId.Value, channel.Name, channel.Type.ToString(), null, null, channel.SpaceId, channel.Topic));
        }).RequirePermission(Permissions.Channel.Create);

        v1.MapPut("/workspaces/{workspaceId:guid}/channels/{channelId:guid}/topic", async (
            Guid workspaceId,
            Guid channelId,
            UpdateChannelTopicRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null || channel.WorkspaceId != workspace.Id)
            {
                return Results.Forbid();
            }

            if (channel.Type == ChannelType.Direct)
            {
                return Results.BadRequest(new { error = "Direct messages do not support a topic." });
            }

            var topic = (request.Topic ?? string.Empty).Trim();
            if (topic.Length > 250)
            {
                return Results.BadRequest(new { error = "topic must be at most 250 characters." });
            }

            channel.Topic = string.IsNullOrEmpty(topic) ? null : topic;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new ChannelResponse(
                channel.Id.Value,
                channel.WorkspaceId.Value,
                channel.Name,
                channel.Type.ToString(),
                SpaceId: channel.SpaceId,
                Topic: channel.Topic));
        }).RequirePermission(Permissions.Channel.Create);
    }

    internal static void MapThreads(this RouteGroupBuilder v1)
    {
        v1.MapPost("/channels/{channelId:guid}/messages/{messageId:guid}/threads", async (
            Guid channelId,
            Guid messageId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var parent = await db.Messages.FirstOrDefaultAsync(
                x => x.Id == new MessageId(messageId) && x.ConversationId == channel.Id,
                ct);
            if (parent is null || parent.DeletedAt is not null)
            {
                return Results.NotFound();
            }

            MessageThread thread;
            if (parent.ThreadId is Guid existingThreadId)
            {
                thread = await db.MessageThreads.FirstAsync(x => x.Id == existingThreadId, ct);
            }
            else
            {
                var existing = await db.MessageThreads.FirstOrDefaultAsync(
                    x => x.TenantId == channel.TenantId && x.ParentMessageId == parent.Id,
                    ct);
                if (existing is not null)
                {
                    thread = existing;
                    parent.ThreadId = thread.Id;
                }
                else
                {
                    thread = new MessageThread
                    {
                        Id = Guid.NewGuid(),
                        TenantId = channel.TenantId,
                        ChannelId = channel.Id,
                        ParentMessageId = parent.Id,
                        CreatedBy = profile.Id,
                        CreatedAt = clock.UtcNow
                    };
                    db.MessageThreads.Add(thread);
                    parent.ThreadId = thread.Id;

                    // B-102: the root message's author auto-follows their own new thread, plus anyone
                    // who opted into "seguir todas as threads" for this channel. Thread is brand new here
                    // (zero prior subscriptions), so a local set is enough to avoid a duplicate insert.
                    var now = clock.UtcNow;
                    var alreadySubscribed = new HashSet<Guid>();
                    db.ThreadSubscriptions.Add(new ThreadSubscription
                    {
                        Id = Guid.NewGuid(),
                        TenantId = channel.TenantId,
                        UserId = parent.AuthorId,
                        ThreadId = thread.Id,
                        ChannelId = channel.Id,
                        Source = ThreadSubscriptionSource.Author,
                        LastReadSeq = 0,
                        CreatedAt = now
                    });
                    alreadySubscribed.Add(parent.AuthorId.Value);

                    var followAllUserIds = await db.ChannelMembers.AsNoTracking()
                        .Where(x => x.TenantId == channel.TenantId && x.ChannelId == channel.Id)
                        .Join(
                            db.ChannelNotificationPreferences.AsNoTracking()
                                .Where(x => x.ChannelId == channel.Id && x.FollowAllThreads),
                            cm => cm.UserId,
                            p => p.UserId,
                            (cm, p) => cm.UserId)
                        .ToArrayAsync(ct);
                    foreach (var userId in followAllUserIds)
                    {
                        if (!alreadySubscribed.Add(userId.Value))
                        {
                            continue;
                        }

                        db.ThreadSubscriptions.Add(new ThreadSubscription
                        {
                            Id = Guid.NewGuid(),
                            TenantId = channel.TenantId,
                            UserId = userId,
                            ThreadId = thread.Id,
                            ChannelId = channel.Id,
                            Source = ThreadSubscriptionSource.Manual,
                            LastReadSeq = 0,
                            CreatedAt = now
                        });
                    }
                }

                await db.SaveChangesAsync(ct);
            }

            var replyCount = await db.Messages.CountAsync(
                x => x.ThreadId == thread.Id && x.ConversationId == new ChannelId(thread.Id),
                ct);
            var subscription = await db.ThreadSubscriptions.AsNoTracking()
                .Where(x => x.ThreadId == thread.Id && x.UserId == profile.Id)
                .Select(x => (ThreadSubscriptionSource?)x.Source)
                .FirstOrDefaultAsync(ct);
            return Results.Ok(new ThreadResponse(
                thread.Id,
                thread.ChannelId.Value,
                thread.ParentMessageId.Value,
                thread.CreatedBy.Value,
                thread.CreatedAt,
                replyCount,
                Following: subscription is not null,
                FollowSource: subscription?.ToString()));
        }).RequirePermission(Permissions.Message.Send);

        v1.MapGet("/threads/{threadId:guid}", async (
            Guid threadId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            await BeginRlsUserAsync(db, tenant, profile.Id, ct);
            var thread = await db.MessageThreads.AsNoTracking().FirstOrDefaultAsync(x => x.Id == threadId, ct);
            if (thread is null)
            {
                return Results.NotFound();
            }

            var channel = await ResolveChannelAsync(thread.ChannelId, profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var parent = await (
                from m in db.Messages.AsNoTracking()
                where m.Id == thread.ParentMessageId
                join u in db.UserProfiles on m.AuthorId equals u.Id into authors
                from u in authors.DefaultIfEmpty()
                select new
                {
                    Message = m,
                    AuthorName = u != null ? u.DisplayName : m.AuthorId.Value.ToString()
                }).FirstOrDefaultAsync(ct);

            var replyCount = await db.Messages.CountAsync(
                x => x.ThreadId == thread.Id && x.ConversationId == new ChannelId(thread.Id),
                ct);

            MessageResponse? parentResponse = null;
            if (parent is not null)
            {
                var attachments = await LoadAttachmentsByMessageAsync(db, channel.Id, [parent.Message.Id], ct);
                var reactions = await LoadReactionSummariesByMessageAsync(db, [parent.Message.Id], profile.Id, ct);
                var parentReplyTo = await LoadReplyToByIdsAsync(db, [parent.Message.ReplyToMessageId], ct);
                parentResponse = new MessageResponse(
                    parent.Message.Id.Value,
                    channel.Id.Value,
                    parent.Message.Sequence,
                    parent.Message.AuthorId.Value,
                    parent.Message.DeletedAt == null ? parent.Message.Body : string.Empty,
                    parent.Message.CreatedAt,
                    parent.Message.EditedAt,
                    parent.Message.DeletedAt,
                    parent.AuthorName,
                    parent.Message.DeletedAt == null && attachments.TryGetValue(parent.Message.Id.Value, out var atts) ? atts : [],
                    thread.Id,
                    parent.Message.ReplyToMessageId?.Value,
                    replyCount,
                    channel.Id.Value,
                    parent.Message.DeletedAt == null && reactions.TryGetValue(parent.Message.Id.Value, out var rx) ? rx : [],
                    parent.Message.ReplyToMessageId is MessageId prtid
                        && parentReplyTo.TryGetValue(prtid.Value, out var replyTo)
                        ? replyTo
                        : null);
            }

            var subscription = await db.ThreadSubscriptions.AsNoTracking()
                .Where(x => x.ThreadId == thread.Id && x.UserId == profile.Id)
                .Select(x => (ThreadSubscriptionSource?)x.Source)
                .FirstOrDefaultAsync(ct);

            return Results.Ok(new ThreadResponse(
                thread.Id,
                thread.ChannelId.Value,
                thread.ParentMessageId.Value,
                thread.CreatedBy.Value,
                thread.CreatedAt,
                replyCount,
                parentResponse,
                Following: subscription is not null,
                FollowSource: subscription?.ToString()));
        });

        v1.MapGet("/threads/{threadId:guid}/messages", async (
            Guid threadId,
            long? after,
            int? limit,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            await BeginRlsUserAsync(db, tenant, profile.Id, ct);
            var thread = await db.MessageThreads.AsNoTracking().FirstOrDefaultAsync(x => x.Id == threadId, ct);
            if (thread is null)
            {
                return Results.NotFound();
            }

            var channel = await ResolveChannelAsync(thread.ChannelId, profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var conversationId = new ChannelId(thread.Id);
            var take = Math.Clamp(limit ?? 50, 1, 100);
            var rows = await (
                from m in db.Messages
                where m.ConversationId == conversationId && m.Sequence > (after ?? 0)
                join u in db.UserProfiles on m.AuthorId equals u.Id into authors
                from u in authors.DefaultIfEmpty()
                orderby m.Sequence
                select new
                {
                    Id = m.Id,
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
                    AuthorName = u != null ? u.DisplayName : m.AuthorId.Value.ToString()
                })
                .Take(take)
                .ToArrayAsync(ct);

            var messageIds = rows.Select(x => x.Id).ToArray();
            var attachmentsByMessage = await LoadAttachmentsByMessageAsync(db, channel.Id, messageIds, ct);
            var reactionsByMessage = await LoadReactionSummariesByMessageAsync(db, messageIds, profile.Id, ct);
            var replyToById = await LoadReplyToByIdsAsync(db, rows.Select(x => x.ReplyToMessageId), ct);
            var forwardedFromById = await LoadForwardedFromByIdsAsync(
                db,
                rows.Select(x => (x.ForwardedFromMessageId, x.ForwardedFromChannelId)),
                profile.Id,
                ct);
            var linkPreviewByMessage = await LoadLinkPreviewsByMessageAsync(db, messageIds, ct);
            var pollsByMessage = await PollQuery.LoadByMessageIdsAsync(db, messageIds, profile.Id, canVote: false, includeVoters: true, ct);
            var messages = rows.Select(x => new MessageResponse(
                x.Id.Value,
                channel.Id.Value,
                x.Sequence,
                x.AuthorId.Value,
                x.DeletedAt == null ? x.Body : string.Empty,
                x.CreatedAt,
                x.EditedAt,
                x.DeletedAt,
                x.AuthorName,
                x.DeletedAt == null && attachmentsByMessage.TryGetValue(x.Id.Value, out var atts) ? atts : [],
                x.ThreadId ?? thread.Id,
                x.ReplyToMessageId?.Value,
                0,
                thread.Id,
                x.DeletedAt == null && reactionsByMessage.TryGetValue(x.Id.Value, out var rx) ? rx : [],
                x.ReplyToMessageId is MessageId rtid && replyToById.TryGetValue(rtid.Value, out var replyTo) ? replyTo : null,
                x.ForwardedFromMessageId?.Value,
                x.ForwardedFromChannelId?.Value,
                x.ForwardedFromMessageId is MessageId ffid && forwardedFromById.TryGetValue(ffid.Value, out var forwarded) ? forwarded : null,
                x.DeletedAt == null && linkPreviewByMessage.TryGetValue(x.Id.Value, out var preview) ? preview : null,
                false,
                x.DeletedAt == null && pollsByMessage.TryGetValue(x.Id.Value, out var poll) ? poll : null)).ToArray();
            return Results.Ok(messages);
        });

        v1.MapPost("/threads/{threadId:guid}/messages", async (
            Guid threadId,
            SendMessageRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IMessageWriter writer,
            IRateLimiter rateLimiter,
            RateLimitSettingsResolver rateLimits,
            FilesSettingsResolver filesSettings,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            await BeginRlsUserAsync(db, tenant, profile.Id, ct);
            var thread = await db.MessageThreads.AsNoTracking().FirstOrDefaultAsync(x => x.Id == threadId, ct);
            if (thread is null)
            {
                return Results.NotFound();
            }

            var channel = await ResolveChannelAsync(thread.ChannelId, profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var rate = await rateLimits.ResolveAsync(channel.TenantId, ct);
            var allowed = await rateLimiter.TryAcquireAsync(
                RateLimitKeys.SendMessage(channel.TenantId, profile.Id),
                rate.SendPerMinute,
                TimeSpan.FromMinutes(1),
                ct);
            if (!allowed)
            {
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            var hasAttachments = request.AttachmentIds is { Length: > 0 };
            if (request.MessageId == Guid.Empty || string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                return Results.BadRequest(new { error = "messageId, idempotencyKey and body or attachments are required." });
            }

            var normalizedBody = MessageBodyPolicies.Normalize(request.Body);
            if (MessageBodyPolicies.IsEmpty(normalizedBody) && !hasAttachments)
            {
                return Results.BadRequest(new { error = "messageId, idempotencyKey and body or attachments are required." });
            }

            if (!MessageBodyPolicies.IsWithinLimit(normalizedBody))
            {
                return Results.BadRequest(MessageBodyPolicies.TooLongPayload());
            }

            var files = await filesSettings.ResolveAsync(channel.TenantId, ct);
            var maxAttachments = files.MaxAttachmentsPerMessage;
            var attachmentCount = request.AttachmentIds?.Length ?? 0;
            if (!AttachmentPolicies.IsWithinAttachmentCount(attachmentCount, maxAttachments))
            {
                return Results.BadRequest(new { error = "TooManyAttachments", max = maxAttachments });
            }

            try
            {
                var result = await writer.SendAsync(new SendMessageCommand(
                    channel.TenantId,
                    profile.Id,
                    channel.Id,
                    new MessageId(request.MessageId),
                    request.IdempotencyKey,
                    normalizedBody,
                    request.ReplyToMessageId is null ? new MessageId(thread.ParentMessageId.Value) : new MessageId(request.ReplyToMessageId.Value),
                    thread.Id,
                    request.AttachmentIds), ct);

                var attachments = await db.Attachments.AsNoTracking()
                    .Where(x => x.MessageId == result.MessageId)
                    .OrderBy(x => x.CreatedAt)
                    .Select(x => new AttachmentResponse(
                        x.Id,
                        x.FileName,
                        x.ContentType,
                        x.SizeBytes,
                        x.Status.ToString(),
                        x.Kind.ToString(),
                        x.DurationMs,
                        x.Waveform,
                        x.ThumbnailStatus != null ? x.ThumbnailStatus.ToString() : null,
                        x.Width,
                        x.Height,
                        x.PageCount))
                    .ToArrayAsync(ct);

                var effectiveReplyTo = request.ReplyToMessageId ?? thread.ParentMessageId.Value;
                var replyToById = await LoadReplyToByIdsAsync(db, [new MessageId(effectiveReplyTo)], ct);
                replyToById.TryGetValue(effectiveReplyTo, out var replyTo);

                return Results.Accepted(
                    $"/api/v1/threads/{thread.Id}/messages?after={result.Sequence - 1}",
                    new MessageResponse(
                        result.MessageId.Value,
                        channel.Id.Value,
                        result.Sequence,
                        profile.Id.Value,
                        normalizedBody,
                        result.CreatedAt,
                        null,
                        null,
                        profile.DisplayName,
                        attachments,
                        thread.Id,
                        effectiveReplyTo,
                        0,
                        thread.Id,
                        null,
                        replyTo));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException ex) when (ex is MentionAllForbiddenException)
            {
                return Results.Json(
                    new { error = "MentionAllForbidden", message = "Channel-wide mention is not allowed in this channel." },
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
        }).RequirePermission(Permissions.Message.Send);

        // B-102: follow/unfollow a thread, list followed threads, advance the per-thread read cursor,
        // and share a thread reply back to its channel as a reference (reuses the B-085 forward writer).
        v1.MapPost("/threads/{threadId:guid}/subscription", async (
            Guid threadId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            await BeginRlsUserAsync(db, tenant, profile.Id, ct);
            var thread = await db.MessageThreads.AsNoTracking().FirstOrDefaultAsync(x => x.Id == threadId, ct);
            if (thread is null)
            {
                return Results.NotFound();
            }

            var channel = await ResolveChannelAsync(thread.ChannelId, profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var subscription = await db.ThreadSubscriptions
                .FirstOrDefaultAsync(x => x.ThreadId == threadId && x.UserId == profile.Id, ct);
            if (subscription is null)
            {
                var currentSeq = await db.ConversationSequences.AsNoTracking()
                    .Where(x => x.ConversationId == new ChannelId(threadId))
                    .Select(x => (long?)x.LastSequence)
                    .FirstOrDefaultAsync(ct) ?? 0;
                subscription = new ThreadSubscription
                {
                    Id = Guid.NewGuid(),
                    TenantId = channel.TenantId,
                    UserId = profile.Id,
                    ThreadId = threadId,
                    ChannelId = channel.Id,
                    Source = ThreadSubscriptionSource.Manual,
                    LastReadSeq = currentSeq,
                    CreatedAt = clock.UtcNow
                };
                db.ThreadSubscriptions.Add(subscription);
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new ThreadSubscriptionResponse(threadId, true, subscription.Source.ToString(), subscription.LastReadSeq));
        }).RequirePermission(Permissions.Message.Read);

        v1.MapDelete("/threads/{threadId:guid}/subscription", async (
            Guid threadId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            await BeginRlsUserAsync(db, tenant, profile.Id, ct);
            var thread = await db.MessageThreads.AsNoTracking().FirstOrDefaultAsync(x => x.Id == threadId, ct);
            if (thread is null)
            {
                return Results.NotFound();
            }

            var channel = await ResolveChannelAsync(thread.ChannelId, profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var subscription = await db.ThreadSubscriptions
                .FirstOrDefaultAsync(x => x.ThreadId == threadId && x.UserId == profile.Id, ct);
            if (subscription is not null)
            {
                db.ThreadSubscriptions.Remove(subscription);
                await db.SaveChangesAsync(ct);
            }

            return Results.NoContent();
        }).RequirePermission(Permissions.Message.Read);

        v1.MapPut("/threads/{threadId:guid}/subscription/read-cursor", async (
            Guid threadId,
            UpsertThreadReadCursorRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            await BeginRlsUserAsync(db, tenant, profile.Id, ct);
            var thread = await db.MessageThreads.AsNoTracking().FirstOrDefaultAsync(x => x.Id == threadId, ct);
            if (thread is null)
            {
                return Results.NotFound();
            }

            var channel = await ResolveChannelAsync(thread.ChannelId, profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var subscription = await db.ThreadSubscriptions
                .FirstOrDefaultAsync(x => x.ThreadId == threadId && x.UserId == profile.Id, ct);
            if (subscription is null)
            {
                return Results.NotFound();
            }

            subscription.LastReadSeq = request.AllowRetrograde
                ? request.LastReadSequence
                : Math.Max(subscription.LastReadSeq, request.LastReadSequence);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new ThreadSubscriptionResponse(threadId, true, subscription.Source.ToString(), subscription.LastReadSeq));
        }).RequirePermission(Permissions.Message.Read);

        v1.MapGet("/workspaces/{workspaceId:guid}/threads/following", async (
            Guid workspaceId,
            int? limit,
            string? cursor,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var pageSize = Math.Clamp(limit ?? ThreadSubscriptionPolicies.DefaultPageSize, 1, ThreadSubscriptionPolicies.MaxPageSize);
            if (!TryParseOffsetCursor(cursor, out var offset))
            {
                return Results.BadRequest(new { error = "InvalidCursor" });
            }

            // Membership re-checked per row (not just at the workspace gate) so a thread in a channel the
            // user has left silently drops off the list instead of hiding the whole page (spec: "sair do
            // canal esconde as threads dele") — same pattern as the B-093 saved-messages list below.
            var rows = await (
                from sub in db.ThreadSubscriptions.AsNoTracking()
                join channel in db.Channels.AsNoTracking() on sub.ChannelId equals channel.Id
                join thread in db.MessageThreads.AsNoTracking() on sub.ThreadId equals thread.Id
                join parentMsg in db.Messages.AsNoTracking() on thread.ParentMessageId equals parentMsg.Id into parentJoin
                from parentMsg in parentJoin.DefaultIfEmpty()
                where sub.UserId == profile.Id
                    && channel.WorkspaceId == workspace.Id
                    && (
                        (
                            (channel.Type == ChannelType.Public || channel.Type == ChannelType.Announcement)
                            && db.WorkspaceMembers.Any(wm =>
                                wm.TenantId == workspace.TenantId && wm.WorkspaceId == channel.WorkspaceId && wm.UserId == profile.Id)
                        )
                        || (
                            channel.Type != ChannelType.Public
                            && channel.Type != ChannelType.Announcement
                            && db.ChannelMembers.Any(cm =>
                                cm.TenantId == workspace.TenantId && cm.ChannelId == channel.Id && cm.UserId == profile.Id)
                        )
                    )
                select new
                {
                    sub.ThreadId,
                    ChannelId = channel.Id,
                    ChannelName = channel.Name,
                    ChannelType = channel.Type,
                    sub.LastReadSeq,
                    sub.CreatedAt,
                    RootBody = parentMsg != null ? parentMsg.Body : string.Empty,
                    RootDeleted = parentMsg == null || parentMsg.DeletedAt != null
                }).ToListAsync(ct);

            if (rows.Count == 0)
            {
                return Results.Ok(new FollowedThreadsPageResponse([], null));
            }

            var threadIds = rows.Select(x => x.ThreadId).Distinct().ToArray();
            var conversationIds = threadIds.Select(x => new ChannelId(x)).ToArray();
            var currentSeqByThread = await db.ConversationSequences.AsNoTracking()
                .Where(x => conversationIds.Contains(x.ConversationId))
                .ToDictionaryAsync(x => x.ConversationId.Value, x => x.LastSequence, ct);
            var lastActivityByThread = await db.Messages.AsNoTracking()
                .Where(m => m.ThreadId != null && threadIds.Contains(m.ThreadId.Value))
                .GroupBy(m => m.ThreadId!.Value)
                .Select(g => new { ThreadId = g.Key, LastAt = g.Max(x => x.CreatedAt) })
                .ToDictionaryAsync(x => x.ThreadId, x => x.LastAt, ct);

            var channelStubs = rows
                .GroupBy(x => x.ChannelId.Value)
                .Select(g =>
                {
                    var first = g.First();
                    return new Channel { Id = first.ChannelId, Type = first.ChannelType, Name = first.ChannelName };
                })
                .ToArray();
            var peers = await ResolveDirectPeersAsync(channelStubs, profile.Id, db, ct);

            var merged = rows
                .Select(x =>
                {
                    var currentSeq = currentSeqByThread.TryGetValue(x.ThreadId, out var seq) ? seq : 0L;
                    var unread = Math.Max(0, currentSeq - x.LastReadSeq);
                    var lastActivityAt = lastActivityByThread.TryGetValue(x.ThreadId, out var at) ? at : x.CreatedAt;
                    var channelName = x.ChannelType == ChannelType.Direct
                        ? (peers.TryGetValue(x.ChannelId, out var peer) && !string.IsNullOrWhiteSpace(peer.DisplayName) ? peer.DisplayName : "DM")
                        : x.ChannelName;
                    return new FollowedThreadResponse(
                        x.ThreadId,
                        x.ChannelId.Value,
                        channelName,
                        x.ChannelType.ToString(),
                        x.RootDeleted ? "Mensagem removida" : TruncateReplyPreview(x.RootBody),
                        x.RootDeleted,
                        unread,
                        lastActivityAt);
                })
                .OrderByDescending(x => x.LastActivityAt)
                .ThenByDescending(x => x.ThreadId)
                .ToArray();

            var page = merged.Skip(offset).Take(pageSize + 1).ToArray();
            string? nextCursor = null;
            if (page.Length > pageSize)
            {
                page = page.Take(pageSize).ToArray();
                nextCursor = EncodeOffsetCursor(offset + pageSize);
            }

            return Results.Ok(new FollowedThreadsPageResponse(page, nextCursor));
        }).RequirePermission(Permissions.Message.Read);

        v1.MapPost("/threads/{threadId:guid}/messages/{messageId:guid}/share-to-channel", async (
            Guid threadId,
            Guid messageId,
            ShareToChannelRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IMessageWriter writer,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            await BeginRlsUserAsync(db, tenant, profile.Id, ct);
            var thread = await db.MessageThreads.AsNoTracking().FirstOrDefaultAsync(x => x.Id == threadId, ct);
            if (thread is null)
            {
                return Results.NotFound();
            }

            var channel = await ResolveChannelAsync(thread.ChannelId, profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var target = await db.Messages.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == new MessageId(messageId) && x.ThreadId == threadId, ct);
            if (target is null || target.DeletedAt is not null)
            {
                return Results.NotFound();
            }

            try
            {
                // Reuses the B-085 forward writer with the thread's own channel as the single target —
                // "share to channel" is a same-channel forward that references the reply, not a copy.
                var result = await writer.ForwardAsync(new ForwardMessageCommand(
                    channel.TenantId,
                    profile.Id,
                    channel.WorkspaceId,
                    target.Id,
                    request.IdempotencyKey,
                    [channel.Id],
                    null), ct);

                var shared = result.Messages[0];
                return Results.Accepted(
                    $"/api/v1/channels/{shared.ChannelId}/messages?after={shared.Sequence - 1}",
                    new ShareToChannelResponse(shared.MessageId.Value, shared.ChannelId.Value, shared.Sequence, shared.CreatedAt));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
        }).RequirePermission(Permissions.Message.Send);
    }

    internal static void MapReadCursor(this RouteGroupBuilder v1)
    {
        v1.MapPut("/channels/{channelId:guid}/read-cursor", async (Guid channelId, UpsertReadCursorRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IChatPublisher publisher, IClock clock, CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var cursor = await db.ReadCursors.FirstOrDefaultAsync(x => x.ChannelId == channel.Id && x.UserId == profile.Id, ct);
            if (cursor is null)
            {
                cursor = new ReadCursor { Id = Guid.NewGuid(), TenantId = channel.TenantId, ChannelId = channel.Id, UserId = profile.Id };
                db.ReadCursors.Add(cursor);
            }

            cursor.LastReadSequence = request.AllowRetrograde
                ? request.LastReadSequence
                : Math.Max(cursor.LastReadSequence, request.LastReadSequence);
            cursor.UpdatedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            var payload = new
            {
                tenantId = channel.TenantId.Value,
                channelId,
                userId = profile.Id.Value,
                lastReadSequence = cursor.LastReadSequence,
            };
            await publisher.PublishAsync(new RealtimeMessage("ReadCursorChanged", channel.TenantId, channel.Id, payload), ct);
            return Results.Ok(new ReadCursorResponse(channel.Id.Value, profile.Id.Value, cursor.LastReadSequence, cursor.UpdatedAt));
        }).RequirePermission(Permissions.Message.Read);
    }

    internal static void MapUnreadCount(this RouteGroupBuilder v1)
    {
        v1.MapGet("/channels/{channelId:guid}/unread-count", async (Guid channelId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var lastRead = await db.ReadCursors.Where(x => x.ChannelId == channel.Id && x.UserId == profile.Id).Select(x => (long?)x.LastReadSequence).FirstOrDefaultAsync(ct) ?? 0;
            var count = await db.Messages.CountAsync(x => x.ConversationId == channel.Id && x.Sequence > lastRead && x.DeletedAt == null, ct);
            var mentionCount = await (
                from mention in db.MessageMentions.AsNoTracking()
                join message in db.Messages.AsNoTracking() on mention.MessageId equals message.Id
                where mention.ChannelId == channel.Id
                    && mention.MentionedUserId == profile.Id
                    && message.Sequence > lastRead
                    && message.DeletedAt == null
                select mention.MessageId
            ).Distinct().CountAsync(ct);
            return Results.Ok(new { channelId, unreadCount = count, mentionCount });
        });
    }
}
