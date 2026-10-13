using Microsoft.EntityFrameworkCore;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.Realtime;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Api.Endpoints;

internal static class InboxEndpoints
{
    internal static void MapInbox(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/inbox", ListAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/workspaces/{workspaceId:guid}/inbox/read", (Delegate)ReadAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/workspaces/{workspaceId:guid}/inbox/unread", (Delegate)UnreadAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPut("/workspaces/{workspaceId:guid}/inbox/snooze", (Delegate)SnoozeAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapDelete("/workspaces/{workspaceId:guid}/inbox/snooze", (Delegate)UnsnoozeAsync)
            .RequirePermission(Permissions.Message.Read);
    }

    private static async Task<IResult> ListAsync(
        Guid workspaceId,
        string? type,
        string? state,
        string? cursor,
        int? limit,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        if (!InboxPolicies.TryParseState(state, out var stateFilter)
            || (type is not null && type.Length > 0 && !InboxPolicies.TryParseKind(type, out _)))
        {
            return Results.BadRequest(new { error = "InvalidInboxFilter" });
        }

        InboxItemKind? kind = null;
        if (!string.IsNullOrWhiteSpace(type))
        {
            InboxPolicies.TryParseKind(type, out var parsed);
            kind = parsed;
        }

        var gate = await OpenWorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        try
        {
            var collected = await InboxReader.CollectAsync(
                db, gate.WorkspaceId, gate.UserId, gate.IsWorkspaceMember, clock.UtcNow, ct);
            var pageSize = limit ?? InboxPolicies.DefaultPageSize;
            var page = InboxReader.Apply(collected, kind, stateFilter, cursor, pageSize);
            return Results.Ok(ToPage(page));
        }
        catch (ArgumentException ex) when (ex.Message == "InvalidCursor")
        {
            return Results.BadRequest(new { error = "InvalidCursor" });
        }
    }

    private static Task<IResult> ReadAsync(
        Guid workspaceId,
        InboxItemRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        IChatPublisher publisher,
        CancellationToken ct) =>
        ChangeReadAsync(workspaceId, request, markUnread: false, http, db, tenant, clock, publisher, ct);

    private static Task<IResult> UnreadAsync(
        Guid workspaceId,
        InboxItemRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        IChatPublisher publisher,
        CancellationToken ct) =>
        ChangeReadAsync(workspaceId, request, markUnread: true, http, db, tenant, clock, publisher, ct);

    private static async Task<IResult> ChangeReadAsync(
        Guid workspaceId,
        InboxItemRequest request,
        bool markUnread,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        IChatPublisher publisher,
        CancellationToken ct)
    {
        if (!InboxPolicies.TryParseItemKey(request.ItemKey, out var kind, out _))
        {
            return Results.BadRequest(new { error = "InvalidInboxItem" });
        }

        if (!InboxPolicies.UsesReadCursor(kind))
        {
            return Results.BadRequest(new { error = "InboxReadNotApplicable" });
        }

        var gate = await OpenWorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var item = await InboxReader.FindAsync(
            db, gate.WorkspaceId, gate.UserId, gate.IsWorkspaceMember, request.ItemKey, clock.UtcNow, ct);
        if (item is null)
        {
            return Results.NotFound(new { error = "InboxItemNotFound" });
        }

        var now = clock.UtcNow;
        if (kind == InboxItemKind.Thread)
        {
            if (item.ThreadId is null || item.Sequence is null)
            {
                return Results.NotFound(new { error = "InboxItemNotFound" });
            }

            var subscription = await db.ThreadSubscriptions.FirstOrDefaultAsync(
                row => row.UserId == gate.UserId && row.ThreadId == item.ThreadId.Value, ct);
            if (subscription is null)
            {
                return Results.NotFound(new { error = "InboxItemNotFound" });
            }

            var target = markUnread ? Math.Max(0, item.Sequence.Value - 1) : item.Sequence.Value;
            subscription.LastReadSeq = markUnread
                ? target
                : Math.Max(subscription.LastReadSeq, target);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new InboxReadResponse(
                item.ItemKey, InboxPolicies.Token(kind), item.ChannelId, item.ThreadId, subscription.LastReadSeq));
        }

        var channel = await RequestAuth.ResolveChannelAsync(new ChannelId(item.ChannelId), gate.UserId, db, tenant, ct);
        if (channel is null)
        {
            return Results.NotFound(new { error = "InboxItemNotFound" });
        }

        long sequence = item.Sequence ?? 0;
        if (kind == InboxItemKind.Direct)
        {
            sequence = await db.ConversationSequences.AsNoTracking()
                .Where(row => row.ConversationId == channel.Id)
                .Select(row => (long?)row.LastSequence)
                .FirstOrDefaultAsync(ct) ?? sequence;
        }

        var next = markUnread ? Math.Max(0, sequence - 1) : sequence;
        var cursor = await db.ReadCursors.FirstOrDefaultAsync(
            row => row.ChannelId == channel.Id && row.UserId == gate.UserId, ct);
        if (cursor is null)
        {
            cursor = new ReadCursor
            {
                Id = Guid.NewGuid(),
                TenantId = channel.TenantId,
                ChannelId = channel.Id,
                UserId = gate.UserId,
            };
            db.ReadCursors.Add(cursor);
        }

        cursor.LastReadSequence = markUnread ? next : Math.Max(cursor.LastReadSequence, next);
        cursor.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await publisher.PublishAsync(new RealtimeMessage(
            "ReadCursorChanged",
            channel.TenantId,
            channel.Id,
            new
            {
                tenantId = channel.TenantId.Value,
                channelId = channel.Id.Value,
                userId = gate.UserId.Value,
                lastReadSequence = cursor.LastReadSequence,
            }), ct);

        return Results.Ok(new InboxReadResponse(
            item.ItemKey, InboxPolicies.Token(kind), channel.Id.Value, null, cursor.LastReadSequence));
    }

    private static async Task<IResult> SnoozeAsync(
        Guid workspaceId,
        InboxSnoozeRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        if (!InboxPolicies.TryParseItemKey(request.ItemKey, out _, out _))
        {
            return Results.BadRequest(new { error = "InvalidInboxItem" });
        }

        var now = clock.UtcNow;
        if (!InboxPolicies.IsSnoozeUntilAllowed(request.Until, now))
        {
            return Results.BadRequest(new { error = "InvalidSnoozeUntil" });
        }

        var gate = await OpenWorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var item = await InboxReader.FindAsync(
            db, gate.WorkspaceId, gate.UserId, gate.IsWorkspaceMember, request.ItemKey, now, ct);
        if (item is null)
        {
            return Results.NotFound(new { error = "InboxItemNotFound" });
        }

        var until = request.Until.ToUniversalTime();
        var existing = await db.InboxSnoozes.FirstOrDefaultAsync(
            row => row.UserId == gate.UserId && row.ItemKey == item.ItemKey, ct);
        if (existing is null)
        {
            db.InboxSnoozes.Add(new InboxSnooze
            {
                Id = Guid.NewGuid(),
                TenantId = gate.TenantId,
                WorkspaceId = gate.WorkspaceId,
                UserId = gate.UserId,
                ItemKey = item.ItemKey,
                SnoozeUntil = until,
                CreatedAt = now,
            });
        }
        else
        {
            existing.SnoozeUntil = until;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(new InboxSnoozeResponse(item.ItemKey, until));
    }

    private static async Task<IResult> UnsnoozeAsync(
        Guid workspaceId,
        string? itemKey,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        if (!InboxPolicies.TryParseItemKey(itemKey, out _, out _))
        {
            return Results.BadRequest(new { error = "InvalidInboxItem" });
        }

        var gate = await OpenWorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var existing = await db.InboxSnoozes.FirstOrDefaultAsync(
            row => row.UserId == gate.UserId && row.WorkspaceId == gate.WorkspaceId && row.ItemKey == itemKey, ct);
        if (existing is not null)
        {
            db.InboxSnoozes.Remove(existing);
            await db.SaveChangesAsync(ct);
        }

        return Results.NoContent();
    }

    private static async Task<WorkspaceGate> OpenWorkspaceAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var (workspace, isGuest) = await RequestAuth.ResolveWorkspaceOrGuestAsync(
            new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return new WorkspaceGate(default, default, default, false, Results.Forbid());
        }

        return new WorkspaceGate(workspace.TenantId, workspace.Id, profile.Id, !isGuest, null);
    }

    private static InboxPageResponse ToPage(InboxListResult page)
    {
        var items = page.Items.Select(item => new InboxItemResponse(
            item.ItemKey,
            InboxPolicies.Token(item.Kind),
            item.Priority,
            item.Reason,
            item.ChannelId,
            item.ChannelName,
            item.MessageId,
            item.ThreadId,
            item.Sequence,
            item.BodyPreview,
            item.MessageRemoved,
            item.SortAt,
            item.SnoozedUntil,
            new InboxNavigationResponse(item.ChannelId, item.MessageId, item.ThreadId, item.Sequence))).ToArray();
        var counts = page.Counts;
        return new InboxPageResponse(
            items,
            page.NextCursor,
            new InboxCountsResponse(counts.Announcement, counts.Mention, counts.Direct, counts.Thread, counts.Saved, counts.Total));
    }

    private readonly record struct WorkspaceGate(
        TenantId TenantId,
        WorkspaceId WorkspaceId,
        UserId UserId,
        bool IsWorkspaceMember,
        IResult? Error);
}
