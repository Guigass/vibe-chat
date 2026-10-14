using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;

namespace VibeChat.Api.Endpoints;

internal static class ChannelTopicEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static void MapChannelTopics(this RouteGroupBuilder v1)
    {
        v1.MapGet("/channels/{channelId:guid}/topics", ListAsync);
        v1.MapPost("/channels/{channelId:guid}/topics", CreateAsync).RequirePermission(Permissions.Conversation.TopicCreate);
        v1.MapGet("/topics/{topicId:guid}", GetAsync);
        v1.MapPut("/topics/{topicId:guid}", RenameAsync).RequirePermission(Permissions.Conversation.TopicCreate);
        v1.MapPost("/topics/{topicId:guid}/close", (Guid topicId, TopicIdempotencyRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IOptions<TopicChannelOptions> options, IPermissionChecker permissions, IIdempotencyStore idempotency, IClock clock, CancellationToken ct) =>
            SetStateAsync(topicId, TopicState.Closed, request, http, db, tenant, options, permissions, idempotency, clock, ct)).RequirePermission(Permissions.Conversation.TopicManage);
        v1.MapPost("/topics/{topicId:guid}/reopen", (Guid topicId, TopicIdempotencyRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IOptions<TopicChannelOptions> options, IPermissionChecker permissions, IIdempotencyStore idempotency, IClock clock, CancellationToken ct) =>
            SetStateAsync(topicId, TopicState.Open, request, http, db, tenant, options, permissions, idempotency, clock, ct)).RequirePermission(Permissions.Conversation.TopicManage);
        v1.MapPost("/topics/{topicId:guid}/move", MoveAsync).RequirePermission(Permissions.Conversation.TopicManage);
        v1.MapPost("/topics/{topicId:guid}/subscription", FollowAsync).RequirePermission(Permissions.Message.Read);
        v1.MapDelete("/topics/{topicId:guid}/subscription", UnfollowAsync).RequirePermission(Permissions.Message.Read);
        v1.MapPut("/topics/{topicId:guid}/read-cursor", ReadCursorAsync).RequirePermission(Permissions.Message.Read);
        v1.MapGet("/topics/{topicId:guid}/messages", ListMessagesAsync);
        v1.MapPost("/topics/{topicId:guid}/messages", SendAsync).RequirePermission(Permissions.Message.Send);
    }

    private static async Task<IResult> ListAsync(
        Guid channelId,
        string? filter,
        string? tag,
        string? cursor,
        int? limit,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<TopicChannelOptions> options,
        IPermissionChecker permissions,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        await RequestAuth.BeginRlsUserAsync(db, tenant, profile.Id, ct);
        var channel = await RequestAuth.ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
        if (channel is null || channel.Layout != ChannelLayout.Topics)
        {
            return Results.Forbid();
        }

        var rows = await db.ChannelTopics.AsNoTracking()
            .Where(x => x.ChannelId == channel.Id)
            .ToListAsync(ct);
        var follows = await db.TopicSubscriptions.AsNoTracking()
            .Where(x => x.UserId == profile.Id && x.ChannelId == channel.Id)
            .Select(x => x.TopicId)
            .ToListAsync(ct);
        var followSet = follows.ToHashSet();
        var ids = rows.Select(x => new ChannelId(x.Id)).ToArray();
        var sequences = await db.ConversationSequences.AsNoTracking()
            .Where(x => ids.Contains(x.ConversationId))
            .ToDictionaryAsync(x => x.ConversationId.Value, x => x.LastSequence, ct);
        var cursors = await db.ReadCursors.AsNoTracking()
            .Where(x => x.UserId == profile.Id && ids.Contains(x.ChannelId))
            .ToDictionaryAsync(x => x.ChannelId.Value, x => x.LastReadSequence, ct);

        IEnumerable<ChannelTopic> query = rows;
        var wanted = (filter ?? "recent").Trim().ToLowerInvariant();
        query = wanted switch
        {
            "open" => query.Where(x => x.State == TopicState.Open),
            "closed" => query.Where(x => x.State == TopicState.Closed),
            "unread" => query.Where(x => followSet.Contains(x.Id) && Unread(x.Id, sequences, cursors) > 0),
            "recent" => query,
            _ => null!
        };
        if (query is null)
        {
            return Results.BadRequest(new { error = "InvalidTopicFilter" });
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var needle = tag.Trim().ToLowerInvariant();
            query = query.Where(x => TopicPolicies.ReadTags(x.Tags).Contains(needle, StringComparer.Ordinal));
        }

        var ordered = query.OrderByDescending(x => x.LastActivityAt).ThenByDescending(x => x.Id).ToList();
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            if (!TopicPolicies.TryDecodeCursor(cursor, out var activity, out var id))
            {
                return Results.BadRequest(new { error = "InvalidTopicCursor" });
            }

            ordered = ordered.Where(x => x.LastActivityAt < activity || (x.LastActivityAt == activity && x.Id.CompareTo(id) < 0)).ToList();
        }

        var take = Math.Clamp(limit ?? TopicPolicies.DefaultPageSize, 1, TopicPolicies.MaxPageSize);
        var page = ordered.Take(take + 1).ToList();
        var hasMore = page.Count > take;
        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        var names = await ParticipantNamesAsync(db, page, ct);
        var canManage = await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Conversation.TopicManage, ct);
        var items = page.Select(x => ToResponse(x, names, followSet.Contains(x.Id), Unread(x.Id, sequences, cursors))).ToArray();
        string? next = hasMore && page.Count > 0
            ? TopicPolicies.EncodeCursor(page[^1].LastActivityAt, page[^1].Id)
            : null;
        return Results.Ok(new TopicPageResponse(items, next, canManage));
    }

    private static async Task<IResult> CreateAsync(
        Guid channelId,
        CreateTopicRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<TopicChannelOptions> options,
        IIdempotencyStore idempotency,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        if (!TopicPolicies.TryNormalizeTitle(request.Title, out var title))
        {
            return Results.BadRequest(new { error = "InvalidTopicTitle" });
        }

        if (!TopicPolicies.TryNormalizeTags(request.Tags, out var tags, out var tagError))
        {
            return Results.BadRequest(new { error = tagError });
        }

        if (!TryKey(request.IdempotencyKey, "create", channelId, out var key))
        {
            return Results.BadRequest(new { error = "InvalidIdempotencyKey" });
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        await RequestAuth.BeginRlsUserAsync(db, tenant, profile.Id, ct);
        var channel = await RequestAuth.ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
        if (channel is null || channel.Layout != ChannelLayout.Topics)
        {
            return Results.Forbid();
        }

        var hash = Hash("create", title, tags, null);
        var replay = await ReplayAsync(idempotency, channel.TenantId, key, hash, ct);
        if (replay is not null)
        {
            return replay;
        }

        var count = await db.ChannelTopics.CountAsync(x => x.ChannelId == channel.Id, ct);
        if (TopicPolicies.IsOverQuota(count))
        {
            VibeChatMetrics.TopicsRejected.Add(1);
            return Results.Conflict(new { error = "TopicQuotaExceeded" });
        }

        var slugs = await db.ChannelTopics.AsNoTracking()
            .Where(x => x.ChannelId == channel.Id)
            .Select(x => x.Slug)
            .ToListAsync(ct);
        var now = clock.UtcNow;
        var topicId = Guid.NewGuid();
        var topic = new ChannelTopic
        {
            Id = topicId,
            TenantId = channel.TenantId,
            WorkspaceId = channel.WorkspaceId,
            ChannelId = channel.Id,
            ConversationId = new ChannelId(topicId),
            Title = title,
            Slug = TopicPolicies.NextSlug(title, slugs.ToHashSet(StringComparer.Ordinal)),
            State = TopicState.Open,
            Tags = tags,
            ParticipantIds = string.Empty,
            CreatedBy = profile.Id,
            CreatedAt = now,
            LastActivityAt = now
        };
        db.ChannelTopics.Add(topic);
        db.TopicSubscriptions.Add(new TopicSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = channel.TenantId,
            UserId = profile.Id,
            TopicId = topic.Id,
            ChannelId = channel.Id,
            CreatedAt = now
        });
        AddEvent(db, channel, topic, TopicEvents.Created, profile.Id, now);
        AddAudit(db, channel, profile.Id, AuditActions.TopicCreate, topic, now);
        var body = ToResponse(topic, new Dictionary<Guid, string>(), true, 0);
        await RememberAsync(idempotency, channel.TenantId, key, hash, 201, body, now, ct);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/topics/{topic.Id}", body);
    }

    private static async Task<IResult> GetAsync(
        Guid topicId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<TopicChannelOptions> options,
        IPermissionChecker permissions,
        IClock clock,
        CancellationToken ct)
    {
        var loaded = await LoadAsync(topicId, http, db, tenant, options, permissions, clock, ct);
        if (loaded.Error is not null)
        {
            return loaded.Error;
        }

        var names = await ParticipantNamesAsync(db, [loaded.Topic!], ct);
        var following = await db.TopicSubscriptions.AsNoTracking()
            .AnyAsync(x => x.TopicId == loaded.Topic!.Id && x.UserId == loaded.ProfileId, ct);
        var sequence = await db.ConversationSequences.AsNoTracking()
            .Where(x => x.ConversationId == new ChannelId(loaded.Topic!.Id))
            .Select(x => (long?)x.LastSequence)
            .FirstOrDefaultAsync(ct) ?? 0;
        var read = await db.ReadCursors.AsNoTracking()
            .Where(x => x.ChannelId == new ChannelId(loaded.Topic!.Id) && x.UserId == loaded.ProfileId)
            .Select(x => (long?)x.LastReadSequence)
            .FirstOrDefaultAsync(ct) ?? 0;
        return Results.Ok(ToResponse(loaded.Topic!, names, following, Math.Max(0, sequence - read)) with { CanPost = loaded.Topic!.State == TopicState.Open || loaded.CanManage });
    }

    private static async Task<IResult> RenameAsync(
        Guid topicId,
        RenameTopicRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<TopicChannelOptions> options,
        IPermissionChecker permissions,
        IIdempotencyStore idempotency,
        IClock clock,
        CancellationToken ct)
    {
        if (!TopicPolicies.TryNormalizeTitle(request.Title, out var title))
        {
            return Results.BadRequest(new { error = "InvalidTopicTitle" });
        }

        var loaded = await LoadAsync(topicId, http, db, tenant, options, permissions, clock, ct, track: true);
        if (loaded.Error is not null)
        {
            return loaded.Error;
        }

        if (loaded.Topic!.CreatedBy != loaded.ProfileId && !loaded.CanManage)
        {
            return Results.Forbid();
        }

        if (!TryKey(request.IdempotencyKey, "rename", topicId, out var key))
        {
            return Results.BadRequest(new { error = "InvalidIdempotencyKey" });
        }

        var hash = Hash("rename", title, string.Empty, topicId);
        var replay = await ReplayAsync(idempotency, loaded.Topic.TenantId, key, hash, ct);
        if (replay is not null)
        {
            return replay;
        }

        var now = clock.UtcNow;
        loaded.Topic.Title = title;
        AddEvent(db, loaded.Channel!, loaded.Topic, TopicEvents.Renamed, loaded.ProfileId, now);
        AddAudit(db, loaded.Channel!, loaded.ProfileId, AuditActions.TopicRename, loaded.Topic, now);
        var body = ToResponse(loaded.Topic, new Dictionary<Guid, string>(), true, 0);
        await RememberAsync(idempotency, loaded.Topic.TenantId, key, hash, 200, body, now, ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(body);
    }

    private static async Task<IResult> SetStateAsync(
        Guid topicId,
        TopicState state,
        TopicIdempotencyRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<TopicChannelOptions> options,
        IPermissionChecker permissions,
        IIdempotencyStore idempotency,
        IClock clock,
        CancellationToken ct)
    {
        var loaded = await LoadAsync(topicId, http, db, tenant, options, permissions, clock, ct, track: true);
        if (loaded.Error is not null)
        {
            return loaded.Error;
        }

        if (!loaded.CanManage)
        {
            return Results.Forbid();
        }

        if (!TryKey(request.IdempotencyKey, state.ToString(), topicId, out var key))
        {
            return Results.BadRequest(new { error = "InvalidIdempotencyKey" });
        }

        var hash = Hash(state.ToString(), string.Empty, string.Empty, topicId);
        var replay = await ReplayAsync(idempotency, loaded.Topic!.TenantId, key, hash, ct);
        if (replay is not null)
        {
            return replay;
        }

        var now = clock.UtcNow;
        loaded.Topic.State = state;
        loaded.Topic.ClosedAt = state == TopicState.Closed ? now : null;
        loaded.Topic.ClosedBy = state == TopicState.Closed ? loaded.ProfileId : null;
        var eventName = state == TopicState.Closed ? TopicEvents.Closed : TopicEvents.Reopened;
        var action = state == TopicState.Closed ? AuditActions.TopicClose : AuditActions.TopicReopen;
        AddEvent(db, loaded.Channel!, loaded.Topic, eventName, loaded.ProfileId, now);
        AddAudit(db, loaded.Channel!, loaded.ProfileId, action, loaded.Topic, now);
        var body = ToResponse(loaded.Topic, new Dictionary<Guid, string>(), false, 0);
        await RememberAsync(idempotency, loaded.Topic.TenantId, key, hash, 200, body, now, ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(body);
    }

    private static async Task<IResult> MoveAsync(
        Guid topicId,
        MoveTopicRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<TopicChannelOptions> options,
        IPermissionChecker permissions,
        IIdempotencyStore idempotency,
        IClock clock,
        CancellationToken ct)
    {
        var loaded = await LoadAsync(topicId, http, db, tenant, options, permissions, clock, ct, track: true);
        if (loaded.Error is not null)
        {
            return loaded.Error;
        }

        if (!loaded.CanManage)
        {
            return Results.Forbid();
        }

        var destination = await RequestAuth.ResolveChannelAsync(new ChannelId(request.ChannelId), loaded.ProfileId, db, tenant, ct);
        if (destination is null
            || destination.WorkspaceId != loaded.Channel!.WorkspaceId
            || destination.Layout != ChannelLayout.Topics
            || destination.Id == loaded.Channel.Id)
        {
            return Results.BadRequest(new { error = "InvalidTopicMove" });
        }

        if (!await permissions.HasPermissionAsync(destination.TenantId, loaded.ProfileId, Permissions.Conversation.TopicManage, ct))
        {
            return Results.Forbid();
        }

        if (!TryKey(request.IdempotencyKey, "move", topicId, out var key))
        {
            return Results.BadRequest(new { error = "InvalidIdempotencyKey" });
        }

        var hash = Hash("move", destination.Id.Value.ToString("D"), string.Empty, topicId);
        var replay = await ReplayAsync(idempotency, loaded.Topic!.TenantId, key, hash, ct);
        if (replay is not null)
        {
            return replay;
        }

        var count = await db.ChannelTopics.CountAsync(x => x.ChannelId == destination.Id, ct);
        if (TopicPolicies.IsOverQuota(count))
        {
            return Results.Conflict(new { error = "TopicQuotaExceeded" });
        }

        var slugs = await db.ChannelTopics.AsNoTracking()
            .Where(x => x.ChannelId == destination.Id)
            .Select(x => x.Slug)
            .ToListAsync(ct);
        var now = clock.UtcNow;
        loaded.Topic.MovedFromChannelId = loaded.Topic.ChannelId;
        loaded.Topic.ChannelId = destination.Id;
        loaded.Topic.Slug = TopicPolicies.NextSlug(loaded.Topic.Title, slugs.ToHashSet(StringComparer.Ordinal));
        var follows = await db.TopicSubscriptions.Where(x => x.TopicId == loaded.Topic.Id).ToListAsync(ct);
        foreach (var follow in follows)
        {
            follow.ChannelId = destination.Id;
        }

        AddEvent(db, destination, loaded.Topic, TopicEvents.Moved, loaded.ProfileId, now);
        AddAudit(db, destination, loaded.ProfileId, AuditActions.TopicMove, loaded.Topic, now);
        var body = ToResponse(loaded.Topic, new Dictionary<Guid, string>(), false, 0);
        await RememberAsync(idempotency, loaded.Topic.TenantId, key, hash, 200, body, now, ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(body);
    }

    private static async Task<IResult> FollowAsync(
        Guid topicId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<TopicChannelOptions> options,
        IPermissionChecker permissions,
        IClock clock,
        CancellationToken ct)
    {
        var loaded = await LoadAsync(topicId, http, db, tenant, options, permissions, clock, ct);
        if (loaded.Error is not null)
        {
            return loaded.Error;
        }

        var existing = await db.TopicSubscriptions
            .FirstOrDefaultAsync(x => x.TopicId == topicId && x.UserId == loaded.ProfileId, ct);
        var now = clock.UtcNow;
        if (existing is null)
        {
            db.TopicSubscriptions.Add(new TopicSubscription
            {
                Id = Guid.NewGuid(),
                TenantId = loaded.Topic!.TenantId,
                UserId = loaded.ProfileId,
                TopicId = topicId,
                ChannelId = loaded.Topic.ChannelId,
                CreatedAt = now
            });
            var sequence = await db.ConversationSequences.AsNoTracking()
                .Where(x => x.ConversationId == new ChannelId(topicId))
                .Select(x => (long?)x.LastSequence)
                .FirstOrDefaultAsync(ct) ?? 0;
            var cursor = await db.ReadCursors.FirstOrDefaultAsync(
                x => x.ChannelId == new ChannelId(topicId) && x.UserId == loaded.ProfileId, ct);
            if (cursor is null)
            {
                db.ReadCursors.Add(new ReadCursor
                {
                    Id = Guid.NewGuid(),
                    TenantId = loaded.Topic.TenantId,
                    ChannelId = new ChannelId(topicId),
                    UserId = loaded.ProfileId,
                    LastReadSequence = sequence,
                    UpdatedAt = now
                });
            }

            AddEvent(db, loaded.Channel!, loaded.Topic, TopicEvents.Followed, loaded.ProfileId, now);
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(new TopicFollowResponse(topicId, true));
    }

    private static async Task<IResult> UnfollowAsync(
        Guid topicId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<TopicChannelOptions> options,
        IPermissionChecker permissions,
        IClock clock,
        CancellationToken ct)
    {
        var loaded = await LoadAsync(topicId, http, db, tenant, options, permissions, clock, ct);
        if (loaded.Error is not null)
        {
            return loaded.Error;
        }

        var existing = await db.TopicSubscriptions
            .FirstOrDefaultAsync(x => x.TopicId == topicId && x.UserId == loaded.ProfileId, ct);
        if (existing is not null)
        {
            db.TopicSubscriptions.Remove(existing);
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(new TopicFollowResponse(topicId, false));
    }

    private static async Task<IResult> ReadCursorAsync(
        Guid topicId,
        UpsertReadCursorRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<TopicChannelOptions> options,
        IPermissionChecker permissions,
        IClock clock,
        CancellationToken ct)
    {
        var loaded = await LoadAsync(topicId, http, db, tenant, options, permissions, clock, ct);
        if (loaded.Error is not null)
        {
            return loaded.Error;
        }

        var conversationId = new ChannelId(topicId);
        var cursor = await db.ReadCursors.FirstOrDefaultAsync(
            x => x.ChannelId == conversationId && x.UserId == loaded.ProfileId, ct);
        if (cursor is null)
        {
            cursor = new ReadCursor
            {
                Id = Guid.NewGuid(),
                TenantId = loaded.Topic!.TenantId,
                ChannelId = conversationId,
                UserId = loaded.ProfileId
            };
            db.ReadCursors.Add(cursor);
        }

        cursor.LastReadSequence = request.AllowRetrograde
            ? request.LastReadSequence
            : Math.Max(cursor.LastReadSequence, request.LastReadSequence);
        cursor.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new ReadCursorResponse(topicId, loaded.ProfileId.Value, cursor.LastReadSequence, cursor.UpdatedAt));
    }

    private static async Task<IResult> ListMessagesAsync(
        Guid topicId,
        long? after,
        int? limit,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<TopicChannelOptions> options,
        IPermissionChecker permissions,
        IClock clock,
        CancellationToken ct)
    {
        var loaded = await LoadAsync(topicId, http, db, tenant, options, permissions, clock, ct);
        if (loaded.Error is not null)
        {
            return loaded.Error;
        }

        var take = Math.Clamp(limit ?? 50, 1, 100);
        var conversationId = new ChannelId(topicId);
        var messages = await (
            from message in db.Messages.AsNoTracking()
            where message.ConversationId == conversationId && message.DeletedAt == null && (after == null || message.Sequence > after)
            join profile in db.UserProfiles.AsNoTracking() on message.AuthorId equals profile.Id into authors
            from profile in authors.DefaultIfEmpty()
            orderby message.Sequence
            select new TopicMessageResponse(
                message.Id.Value,
                topicId,
                loaded.Channel!.Id.Value,
                message.Sequence,
                message.AuthorId.Value,
                profile != null ? profile.DisplayName : message.AuthorId.Value.ToString(),
                message.Body,
                message.CreatedAt))
            .Take(take)
            .ToListAsync(ct);
        return Results.Ok(messages);
    }

    private static async Task<IResult> SendAsync(
        Guid topicId,
        SendMessageRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<TopicChannelOptions> options,
        IPermissionChecker permissions,
        IMessageWriter writer,
        IRateLimiter rateLimiter,
        RateLimitSettingsResolver rateLimits,
        IClock clock,
        CancellationToken ct)
    {
        var loaded = await LoadAsync(topicId, http, db, tenant, options, permissions, clock, ct);
        if (loaded.Error is not null)
        {
            return loaded.Error;
        }

        if (loaded.Topic!.State == TopicState.Closed && !loaded.CanManage)
        {
            VibeChatMetrics.TopicsRejected.Add(1);
            return Results.Json(new { error = "TopicClosed" }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (request.MessageId == Guid.Empty || string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { error = "InvalidTopicMessage" });
        }

        var body = MessageBodyPolicies.Normalize(request.Body);
        if (MessageBodyPolicies.IsEmpty(body) || !MessageBodyPolicies.IsWithinLimit(body))
        {
            return Results.BadRequest(new { error = "InvalidTopicMessage" });
        }

        var rate = await rateLimits.ResolveAsync(loaded.Channel!.TenantId, ct);
        var allowed = await rateLimiter.TryAcquireAsync(
            RateLimitKeys.SendMessage(loaded.Channel.TenantId, loaded.ProfileId),
            rate.SendPerMinute,
            TimeSpan.FromMinutes(1),
            ct);
        if (!allowed)
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        try
        {
            var result = await writer.SendAsync(new SendMessageCommand(
                loaded.Channel.TenantId,
                loaded.ProfileId,
                loaded.Channel.Id,
                new MessageId(request.MessageId),
                request.IdempotencyKey,
                body,
                null,
                null,
                null,
                TopicId: topicId), ct);
            var author = await db.UserProfiles.AsNoTracking()
                .Where(x => x.Id == loaded.ProfileId)
                .Select(x => x.DisplayName)
                .FirstOrDefaultAsync(ct) ?? loaded.ProfileId.Value.ToString();
            return Results.Accepted(
                $"/api/v1/topics/{topicId}/messages?after={result.Sequence - 1}",
                new TopicMessageResponse(result.MessageId.Value, topicId, loaded.Channel.Id.Value, result.Sequence, loaded.ProfileId.Value, author, body, result.CreatedAt));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<TopicLoad> LoadAsync(
        Guid topicId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<TopicChannelOptions> options,
        IPermissionChecker permissions,
        IClock clock,
        CancellationToken ct,
        bool track = false)
    {
        if (!options.Value.Enabled)
        {
            return new TopicLoad(null, null, default, false, Disabled());
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        await RequestAuth.BeginRlsUserAsync(db, tenant, profile.Id, ct);
        if (!tenant.HasTenant)
        {
            var membership = await db.WorkspaceMembers.IgnoreQueryFilters()
                .Where(x => x.UserId == profile.Id)
                .Select(x => x.TenantId)
                .FirstOrDefaultAsync(ct);
            if (membership.Value != Guid.Empty)
            {
                tenant.SetTenant(membership);
                await RlsSession.EnsureAppliedAsync(db, tenant, ct);
            }
        }

        var query = track ? db.ChannelTopics : db.ChannelTopics.AsNoTracking();
        var topic = await query.FirstOrDefaultAsync(x => x.Id == topicId, ct);
        if (topic is null)
        {
            return new TopicLoad(null, null, profile.Id, false, Results.NotFound(new { error = "TopicNotFound" }));
        }

        var channel = await RequestAuth.ResolveChannelAsync(topic.ChannelId, profile.Id, db, tenant, ct);
        if (channel is null || channel.Layout != ChannelLayout.Topics)
        {
            return new TopicLoad(null, null, profile.Id, false, Results.Forbid());
        }

        var canManage = await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Conversation.TopicManage, ct);
        return new TopicLoad(topic, channel, profile.Id, canManage, null);
    }

    private static async Task<Dictionary<Guid, string>> ParticipantNamesAsync(
        VibeChatDbContext db,
        IReadOnlyList<ChannelTopic> topics,
        CancellationToken ct)
    {
        var ids = topics.SelectMany(x => TopicPolicies.ReadParticipants(x.ParticipantIds)).Distinct().Select(x => new UserId(x)).ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        return await db.UserProfiles.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id.Value, x => x.DisplayName, ct);
    }

    private static TopicResponse ToResponse(
        ChannelTopic topic,
        IReadOnlyDictionary<Guid, string> names,
        bool following,
        long unread) =>
        new(
            topic.Id,
            topic.ChannelId.Value,
            topic.Title,
            topic.Slug,
            topic.State.ToString(),
            TopicPolicies.ReadTags(topic.Tags),
            TopicPolicies.ReadParticipants(topic.ParticipantIds).Select(id => names.TryGetValue(id, out var name) ? name : id.ToString("D")).ToArray(),
            following,
            unread,
            topic.LastActivityAt,
            topic.State == TopicState.Open);

    private static long Unread(Guid topicId, IReadOnlyDictionary<Guid, long> sequences, IReadOnlyDictionary<Guid, long> cursors)
    {
        var sequence = sequences.TryGetValue(topicId, out var current) ? current : 0;
        var read = cursors.TryGetValue(topicId, out var cursor) ? cursor : 0;
        return Math.Max(0, sequence - read);
    }

    private static void AddEvent(VibeChatDbContext db, Channel channel, ChannelTopic topic, string type, UserId actor, DateTimeOffset now)
    {
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = channel.TenantId,
            Type = type,
            OccurredAt = now,
            Payload = JsonSerializer.Serialize(new
            {
                tenantId = channel.TenantId.Value,
                channelId = topic.ChannelId.Value,
                topicId = topic.Id,
                actorId = actor.Value,
                state = topic.State.ToString()
            })
        });
    }

    private static void AddAudit(VibeChatDbContext db, Channel channel, UserId actor, string action, ChannelTopic topic, DateTimeOffset now)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = channel.TenantId,
            ActorUserId = actor,
            Action = action,
            EntityType = "ChannelTopic",
            EntityId = topic.Id.ToString("D"),
            OccurredAt = now,
            MetadataJson = JsonSerializer.Serialize(new { channelId = topic.ChannelId.Value, state = topic.State.ToString() })
        });
    }

    private static bool TryKey(string? raw, string op, Guid scope, out string key)
    {
        key = string.Empty;
        var trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length is < 8 or > 120)
        {
            return false;
        }

        key = $"topic:{op}:{scope:N}:{trimmed}";
        return true;
    }

    private static string Hash(string op, string a, string b, Guid? id)
    {
        var raw = $"{op}|{a}|{b}|{id}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private static async Task<IResult?> ReplayAsync(IIdempotencyStore idempotency, TenantId tenantId, string key, string hash, CancellationToken ct)
    {
        var existing = await idempotency.FindAsync(tenantId, key, ct);
        if (existing is null)
        {
            return null;
        }

        if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
        {
            return Results.Conflict(new { error = "IdempotencyConflict" });
        }

        var envelope = JsonSerializer.Deserialize<IdempotencyEnvelope>(existing.ResultJson, Json);
        return envelope is null
            ? null
            : Results.Content(envelope.Body, "application/json", statusCode: envelope.Status);
    }

    private static Task RememberAsync(
        IIdempotencyStore idempotency,
        TenantId tenantId,
        string key,
        string hash,
        int status,
        TopicResponse body,
        DateTimeOffset now,
        CancellationToken ct) =>
        idempotency.StoreAsync(new IdempotencyRecord(
            tenantId,
            key,
            hash,
            JsonSerializer.Serialize(new IdempotencyEnvelope(status, JsonSerializer.Serialize(body, Json)), Json),
            now), ct);

    private static IResult Disabled() => Results.NotFound(new { error = "TopicsDisabled" });

    private sealed record TopicLoad(ChannelTopic? Topic, Channel? Channel, UserId ProfileId, bool CanManage, IResult? Error);
    private sealed record IdempotencyEnvelope(int Status, string Body);
}

public sealed record CreateTopicRequest(string Title, string IdempotencyKey, string[]? Tags = null);
public sealed record RenameTopicRequest(string Title, string IdempotencyKey);
public sealed record MoveTopicRequest(Guid ChannelId, string IdempotencyKey);
public sealed record TopicIdempotencyRequest(string IdempotencyKey);
public sealed record TopicResponse(
    Guid Id,
    Guid ChannelId,
    string Title,
    string Slug,
    string State,
    string[] Tags,
    string[] ParticipantNames,
    bool Following,
    long UnreadCount,
    DateTimeOffset LastActivityAt,
    bool CanPost);
public sealed record TopicPageResponse(TopicResponse[] Items, string? NextCursor, bool CanManage);
public sealed record TopicFollowResponse(Guid TopicId, bool Following);
public sealed record TopicMessageResponse(
    Guid Id,
    Guid TopicId,
    Guid ChannelId,
    long Sequence,
    Guid AuthorId,
    string AuthorName,
    string Body,
    DateTimeOffset CreatedAt);
