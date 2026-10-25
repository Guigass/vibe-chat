using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using static VibeChat.Api.Endpoints.IdentityEndpointHelpers;

namespace VibeChat.Api.Endpoints;

internal static class OfflineSyncEndpoints
{
    internal static void MapOfflineSync(this RouteGroupBuilder v1)
    {
        v1.MapGet("/sync", ReadAsync).RequirePermission(Permissions.Message.Read);
    }

    private static async Task<IResult> ReadAsync(
        HttpContext http,
        Guid? deviceId,
        string? cursor,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<OfflineSyncOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!OfflineSyncOptions.IsActive(options.Value))
        {
            return Results.Json(new { error = OfflineSyncOptions.Disabled }, statusCode: StatusCodes.Status404NotFound);
        }

        if (deviceId is null || deviceId == Guid.Empty)
        {
            return Results.Json(new { error = OfflineSyncProtocol.InvalidDevice }, statusCode: StatusCodes.Status400BadRequest);
        }

        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        await BeginRlsUserAsync(db, tenant, profile.Id, ct);
        var now = clock.UtcNow;
        if (await RequestAuth.IsFullyRevokedAsync(db, profile.Id, ct))
        {
            return Results.Ok(Empty(now, OfflineSyncProtocol.ScopeRevoked));
        }

        var state = OfflineSyncCursor.Read(
            cursor,
            tenant.TenantId.Value,
            profile.Id.Value,
            deviceId.Value,
            now,
            options.Value.CursorKey);
        if (state.Status == OfflineSyncCursorStatus.UpgradeRequired)
        {
            return Results.Json(
                new { error = OfflineSyncProtocol.ProtocolUpgradeRequired, protocolVersion = OfflineSyncProtocol.Version },
                statusCode: StatusCodes.Status409Conflict);
        }

        var snapshot = string.IsNullOrWhiteSpace(cursor)
            || state.Status is OfflineSyncCursorStatus.Expired or OfflineSyncCursorStatus.Rejected;
        var reason = state.Status switch
        {
            OfflineSyncCursorStatus.Expired => OfflineSyncProtocol.CursorExpired,
            OfflineSyncCursorStatus.Rejected => OfflineSyncProtocol.FullResyncRequired,
            _ => null,
        };

        var memberChannels = await db.ChannelMembers.AsNoTracking()
            .Where(member => member.UserId == profile.Id)
            .Select(member => member.ChannelId)
            .Take(OfflineSyncProtocol.CandidateCap + 1)
            .ToListAsync(ct);
        var coverage = memberChannels.Count <= OfflineSyncProtocol.CandidateCap ? "complete" : "partial";
        if (memberChannels.Count > OfflineSyncProtocol.CandidateCap)
        {
            memberChannels = memberChannels.Take(OfflineSyncProtocol.CandidateCap).ToList();
        }

        var threads = await db.MessageThreads.AsNoTracking()
            .Where(thread => memberChannels.Contains(thread.ChannelId))
            .Select(thread => new { thread.Id, thread.ChannelId })
            .Take(OfflineSyncProtocol.CandidateCap)
            .ToListAsync(ct);
        var threadChannel = threads.ToDictionary(thread => thread.Id, thread => thread.ChannelId.Value);
        var allowed = memberChannels
            .Concat(threads.Select(thread => new ChannelId(thread.Id)))
            .Distinct()
            .ToArray();
        var authorized = memberChannels.Select(channel => channel.Value).Distinct().ToArray();

        List<OfflineSyncCandidate> candidates;
        if (snapshot)
        {
            var recent = await db.Messages.AsNoTracking()
                .Where(message => allowed.Contains(message.ConversationId))
                .OrderByDescending(message => message.CreatedAt)
                .Take(OfflineSyncProtocol.PageSize)
                .Select(message => new MessageRow(
                    message.ConversationId,
                    message.Id,
                    message.Sequence,
                    message.Body,
                    message.EditedAt,
                    message.DeletedAt,
                    message.CreatedAt))
                .ToListAsync(ct);
            candidates = recent.Select(row => ToMessage(row, threadChannel)).ToList();
            candidates.AddRange(await RecentTasks(db, memberChannels, ct));
        }
        else
        {
            var since = state.Watermark;
            var messages = await db.Messages.AsNoTracking()
                .Where(message => allowed.Contains(message.ConversationId))
                .Where(message => message.CreatedAt >= since
                    || (message.EditedAt != null && message.EditedAt >= since)
                    || (message.DeletedAt != null && message.DeletedAt >= since))
                .OrderBy(message => message.CreatedAt)
                .Take(OfflineSyncProtocol.CandidateCap)
                .Select(message => new MessageRow(
                    message.ConversationId,
                    message.Id,
                    message.Sequence,
                    message.Body,
                    message.EditedAt,
                    message.DeletedAt,
                    message.CreatedAt))
                .ToListAsync(ct);
            candidates = messages.Select(row => ToMessage(row, threadChannel)).ToList();
            var tasks = await db.MessageTasks.AsNoTracking()
                .Where(task => memberChannels.Contains(task.ChannelId) && task.UpdatedAt >= since)
                .OrderBy(task => task.UpdatedAt)
                .Take(OfflineSyncProtocol.CandidateCap)
                .Select(task => new { task.Id, task.ChannelId, task.Title, task.Version, task.SourceRedacted, task.UpdatedAt })
                .ToListAsync(ct);
            candidates.AddRange(tasks.Select(task => new OfflineSyncCandidate(
                "task",
                task.ChannelId.Value,
                task.ChannelId.Value,
                task.Id,
                0,
                task.Version,
                task.SourceRedacted ? null : task.Title,
                task.SourceRedacted,
                task.UpdatedAt)));
        }

        IReadOnlyList<OfflineSyncCandidate> page;
        var hasMore = false;
        var nextWatermark = now;
        var nextId = Guid.Empty;
        if (snapshot)
        {
            page = candidates;
        }
        else
        {
            page = OfflineSyncPage.Continue(
                candidates,
                state.Watermark,
                state.LastId,
                OfflineSyncProtocol.PageSize,
                out hasMore,
                out var continuedWatermark,
                out nextId);
            if (hasMore)
            {
                nextWatermark = continuedWatermark;
            }
        }

        var (changes, tombstones) = OfflineSyncPage.Split(page);
        var issued = OfflineSyncCursor.Issue(
            tenant.TenantId.Value,
            profile.Id.Value,
            deviceId.Value,
            nextWatermark,
            nextId,
            now,
            options.Value.CursorKey);
        return Results.Ok(new SyncBody(
            OfflineSyncProtocol.Version,
            now,
            issued,
            changes.Select(Map).ToArray(),
            tombstones.Select(Map).ToArray(),
            hasMore,
            reason is null ? null : new RebuildBody(reason),
            authorized,
            coverage));
    }

    private static async Task<List<OfflineSyncCandidate>> RecentTasks(
        VibeChatDbContext db,
        List<ChannelId> memberChannels,
        CancellationToken ct)
    {
        var tasks = await db.MessageTasks.AsNoTracking()
            .Where(task => memberChannels.Contains(task.ChannelId))
            .OrderByDescending(task => task.UpdatedAt)
            .Take(OfflineSyncProtocol.PageSize)
            .Select(task => new { task.Id, task.ChannelId, task.Title, task.Version, task.SourceRedacted, task.UpdatedAt })
            .ToListAsync(ct);
        return tasks.Select(task => new OfflineSyncCandidate(
            "task",
            task.ChannelId.Value,
            task.ChannelId.Value,
            task.Id,
            0,
            task.Version,
            task.SourceRedacted ? null : task.Title,
            task.SourceRedacted,
            task.UpdatedAt)).ToList();
    }

    private static OfflineSyncCandidate ToMessage(MessageRow row, Dictionary<Guid, Guid> threadChannel)
    {
        var conversationId = row.ConversationId.Value;
        var channelId = threadChannel.TryGetValue(conversationId, out var parent) ? parent : conversationId;
        var deleted = row.DeletedAt is not null;
        var activity = row.DeletedAt ?? row.EditedAt ?? row.CreatedAt;
        return new OfflineSyncCandidate(
            "message",
            conversationId,
            channelId,
            row.Id.Value,
            row.Sequence,
            OfflineSyncProtocol.MessageVersion(row.EditedAt),
            deleted ? null : row.Body,
            deleted,
            activity);
    }

    private static SyncItem Map(OfflineSyncCandidate row) => new(
        row.Kind,
        row.ConversationId,
        row.ChannelId,
        row.Id,
        row.Seq,
        row.Version,
        row.Body,
        row.Deleted ? "deleted" : null);

    private static SyncBody Empty(DateTimeOffset now, string reason) => new(
        OfflineSyncProtocol.Version,
        now,
        string.Empty,
        [],
        [],
        false,
        new RebuildBody(reason),
        [],
        "complete");

    private sealed record MessageRow(
        ChannelId ConversationId,
        MessageId Id,
        long Sequence,
        string Body,
        DateTimeOffset? EditedAt,
        DateTimeOffset? DeletedAt,
        DateTimeOffset CreatedAt);

    private sealed record SyncItem(
        string Kind,
        Guid ConversationId,
        Guid ChannelId,
        Guid Id,
        long Seq,
        long Version,
        string? Body,
        string? Reason);

    private sealed record RebuildBody(string Reason);

    private sealed record SyncBody(
        int ProtocolVersion,
        DateTimeOffset ServerTime,
        string Cursor,
        SyncItem[] Changes,
        SyncItem[] Tombstones,
        bool HasMore,
        RebuildBody? Rebuild,
        Guid[] AuthorizedConversationIds,
        string ConversationCoverage);
}
