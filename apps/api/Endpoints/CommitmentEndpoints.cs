using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Api.Endpoints;

internal static class CommitmentEndpoints
{
    internal static void MapCommitments(this RouteGroupBuilder v1)
    {
        v1.MapPost("/workspaces/{workspaceId:guid}/commitments", CreateAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapGet("/workspaces/{workspaceId:guid}/commitments", ListAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapGet("/workspaces/{workspaceId:guid}/commitments/{commitmentId:guid}", GetAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPatch("/workspaces/{workspaceId:guid}/commitments/{commitmentId:guid}", PatchAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/workspaces/{workspaceId:guid}/commitments/{commitmentId:guid}/close", CloseAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/workspaces/{workspaceId:guid}/commitments/{commitmentId:guid}/reopen", ReopenAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapGet("/workspaces/{workspaceId:guid}/commitments/{commitmentId:guid}/history", HistoryAsync)
            .RequirePermission(Permissions.Message.Read);
    }

    private static async Task<IResult> CreateAsync(
        Guid workspaceId,
        CreateCommitmentRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOutboxWriter outbox,
        IAuditWriter audit,
        IClock clock,
        CancellationToken ct)
    {
        var key = CommitmentPolicies.Normalize(request.IdempotencyKey);
        if (!CommitmentPolicies.IsValidIdempotencyKey(key))
        {
            return Bad("InvalidIdempotencyKey");
        }

        if (!CommitmentPolicies.TryParseKind(request.Kind, out var kind))
        {
            return Bad("InvalidCommitmentKind");
        }

        var title = CommitmentPolicies.Normalize(request.Title);
        if (!CommitmentPolicies.IsValidTitle(title))
        {
            return Bad("InvalidTitle");
        }

        var description = NormalizeOptional(request.Description);
        if (!CommitmentPolicies.IsValidDescription(description))
        {
            return Bad("InvalidDescription");
        }

        if (!CommitmentPolicies.TryParseVisibility(request.Visibility, out var visibility))
        {
            return Bad("InvalidVisibility");
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        if (!CommitmentPolicies.IsValidDue(request.DueAt, clock.UtcNow))
        {
            return Bad("InvalidDueAt");
        }

        var existing = await db.Commitments.FirstOrDefaultAsync(
            row => row.CreatedBy == profile.Id && row.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            return await VisibleOrForbid(db, tenant, existing, profile.Id, ct);
        }

        var resolved = await MessagingEndpointHelpers.ResolveReadableMessageAsync(
            db, tenant, workspace, profile.Id, new MessageId(request.MessageId), ct);
        if (resolved is null)
        {
            return Results.Forbid();
        }

        var (message, channel) = resolved.Value;
        if (message.DeletedAt is not null)
        {
            return Results.NotFound();
        }

        if (!CommitmentPolicies.IsConvertible(message.Body))
        {
            return Bad("NotConvertible");
        }

        if (request.OwnerUserId is Guid ownerId
            && !await CanReadChannelAsync(db, channel, new UserId(ownerId), ct))
        {
            return Bad("AssigneeNotMember");
        }

        var threadId = message.ThreadId ?? await db.MessageThreads.AsNoTracking()
            .Where(row => row.ParentMessageId == message.Id)
            .Select(row => (Guid?)row.Id)
            .FirstOrDefaultAsync(ct);

        var now = clock.UtcNow;
        var item = new Commitment
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            ChannelId = channel.Id,
            Kind = kind,
            Title = title,
            Description = description,
            OwnerUserId = request.OwnerUserId is Guid owner ? new UserId(owner) : null,
            DueAt = request.DueAt,
            State = CommitmentState.Open,
            SourceMessageId = message.Id,
            SourceThreadId = threadId,
            SourceVersion = message.Sequence,
            ExcerptHash = CommitmentPolicies.ExcerptHash(message.Body),
            Method = CommitmentPolicies.ConvertMethod,
            CreatedBy = profile.Id,
            AuthzSnapshot = Snapshot(channel, profile.Id),
            Visibility = visibility,
            IdempotencyKey = key,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Commitments.Add(item);
        AddChange(db, item, profile.Id, CommitmentChangeActions.Created, now);
        AddAudit(audit, item, profile.Id, AuditActions.CommitmentCreate, now);
        AddOutbox(outbox, item, "created", now);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            var raced = await db.Commitments.FirstOrDefaultAsync(
                row => row.CreatedBy == profile.Id && row.IdempotencyKey == key, ct);
            if (raced is null)
            {
                throw;
            }

            return await VisibleOrForbid(db, tenant, raced, profile.Id, ct);
        }

        return Results.Ok(await ToResponseAsync(db, item, profile.Id, sourceRemoved: false, ct));
    }

    private static async Task<IResult> ListAsync(
        Guid workspaceId,
        Guid? channelId,
        Guid? ownerId,
        string? kind,
        string? state,
        string? cursor,
        int? limit,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        if (!CommitmentPolicies.TryParseState(state, out var stateFilter)
            || (kind is not null && kind.Length > 0 && !CommitmentPolicies.TryParseKind(kind, out _)))
        {
            return Bad("InvalidCommitmentFilter");
        }

        CommitmentKind? kindFilter = null;
        if (!string.IsNullOrWhiteSpace(kind))
        {
            CommitmentPolicies.TryParseKind(kind, out var parsedKind);
            kindFilter = parsedKind;
        }

        if (!MessagingEndpointHelpers.TryParseSavedCursor(cursor, out var cursorAt, out var cursorId))
        {
            return Bad("InvalidCursor");
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var pageSize = Math.Clamp(limit ?? CommitmentPolicies.DefaultPageSize, 1, CommitmentPolicies.MaxPageSize);
        var query = db.Commitments.AsNoTracking().Where(row => row.WorkspaceId == workspace.Id);
        if (channelId is Guid channel)
        {
            query = query.Where(row => row.ChannelId == new ChannelId(channel));
        }

        if (ownerId is Guid owner)
        {
            query = query.Where(row => row.OwnerUserId == new UserId(owner));
        }

        if (kindFilter is CommitmentKind wanted)
        {
            query = query.Where(row => row.Kind == wanted);
        }

        if (stateFilter is CommitmentState wantedState)
        {
            query = query.Where(row => row.State == wantedState);
        }

        if (cursorAt is DateTimeOffset at && cursorId is Guid id)
        {
            query = query.Where(row => row.CreatedAt < at || (row.CreatedAt == at && row.Id.CompareTo(id) > 0));
        }

        var scanned = await query
            .OrderByDescending(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .Take(CommitmentPolicies.MaxScan)
            .ToListAsync(ct);

        var visible = new List<Commitment>(pageSize + 1);
        var channels = new Dictionary<ChannelId, Channel>();
        foreach (var row in scanned)
        {
            if (!channels.TryGetValue(row.ChannelId, out var channelRow))
            {
                channelRow = await db.Channels.AsNoTracking().FirstOrDefaultAsync(x => x.Id == row.ChannelId, ct);
                if (channelRow is not null)
                {
                    channels[row.ChannelId] = channelRow;
                }
            }

            if (channelRow is null || !await CanReadChannelAsync(db, channelRow, profile.Id, ct))
            {
                continue;
            }

            if (!CommitmentPolicies.CanSee(row, profile.Id, canReadChannel: true))
            {
                continue;
            }

            visible.Add(row);
            if (visible.Count > pageSize)
            {
                break;
            }
        }

        var page = visible.Take(pageSize).ToArray();
        var responses = new CommitmentResponse[page.Length];
        for (var i = 0; i < page.Length; i++)
        {
            var removed = await IsSourceRemovedAsync(db, page[i], ct);
            responses[i] = await ToResponseAsync(db, page[i], profile.Id, removed, ct);
        }

        string? next = visible.Count > pageSize
            ? MessagingEndpointHelpers.EncodeSavedCursor(page[^1].CreatedAt, page[^1].Id)
            : null;
        return Results.Ok(new CommitmentPageResponse(responses, next));
    }

    private static async Task<IResult> GetAsync(
        Guid workspaceId,
        Guid commitmentId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        var gate = await OpenVisibleAsync(workspaceId, commitmentId, http, db, tenant, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var removed = await IsSourceRemovedAsync(db, gate.Item!, ct);
        return Results.Ok(await ToResponseAsync(db, gate.Item!, gate.UserId, removed, ct));
    }

    private static async Task<IResult> PatchAsync(
        Guid workspaceId,
        Guid commitmentId,
        PatchCommitmentRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IOutboxWriter outbox,
        IAuditWriter audit,
        IClock clock,
        CancellationToken ct)
    {
        var gate = await OpenMutableAsync(workspaceId, commitmentId, http, db, tenant, permissions, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var item = gate.Item!;
        if (request.Title is not null)
        {
            var title = CommitmentPolicies.Normalize(request.Title);
            if (!CommitmentPolicies.IsValidTitle(title))
            {
                return Bad("InvalidTitle");
            }

            item.Title = title;
        }

        if (request.Description is not null)
        {
            var description = NormalizeOptional(request.Description);
            if (!CommitmentPolicies.IsValidDescription(description))
            {
                return Bad("InvalidDescription");
            }

            item.Description = description;
        }

        if (request.ClearOwner)
        {
            item.OwnerUserId = null;
        }
        else if (request.OwnerUserId is Guid ownerId)
        {
            var channel = await db.Channels.AsNoTracking().FirstAsync(x => x.Id == item.ChannelId, ct);
            if (!await CanReadChannelAsync(db, channel, new UserId(ownerId), ct))
            {
                return Bad("AssigneeNotMember");
            }

            item.OwnerUserId = new UserId(ownerId);
        }

        if (request.ClearDue)
        {
            item.DueAt = null;
        }
        else if (request.DueAt is DateTimeOffset due)
        {
            if (!CommitmentPolicies.IsValidDue(due, clock.UtcNow))
            {
                return Bad("InvalidDueAt");
            }

            item.DueAt = due;
        }

        if (request.Visibility is not null)
        {
            if (!CommitmentPolicies.TryParseVisibility(request.Visibility, out var visibility))
            {
                return Bad("InvalidVisibility");
            }

            item.Visibility = visibility;
        }

        var now = clock.UtcNow;
        item.UpdatedAt = now;
        AddChange(db, item, gate.UserId, CommitmentChangeActions.Updated, now);
        AddAudit(audit, item, gate.UserId, AuditActions.CommitmentUpdate, now);
        AddOutbox(outbox, item, "updated", now);
        await db.SaveChangesAsync(ct);
        var removed = await IsSourceRemovedAsync(db, item, ct);
        return Results.Ok(await ToResponseAsync(db, item, gate.UserId, removed, ct));
    }

    private static Task<IResult> CloseAsync(
        Guid workspaceId,
        Guid commitmentId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IOutboxWriter outbox,
        IAuditWriter audit,
        IClock clock,
        CancellationToken ct) =>
        SetClosedAsync(workspaceId, commitmentId, closed: true, http, db, tenant, permissions, outbox, audit, clock, ct);

    private static Task<IResult> ReopenAsync(
        Guid workspaceId,
        Guid commitmentId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IOutboxWriter outbox,
        IAuditWriter audit,
        IClock clock,
        CancellationToken ct) =>
        SetClosedAsync(workspaceId, commitmentId, closed: false, http, db, tenant, permissions, outbox, audit, clock, ct);

    private static async Task<IResult> HistoryAsync(
        Guid workspaceId,
        Guid commitmentId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        var gate = await OpenVisibleAsync(workspaceId, commitmentId, http, db, tenant, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var rows = await db.CommitmentChanges.AsNoTracking()
            .Where(row => row.CommitmentId == commitmentId)
            .OrderBy(row => row.At)
            .ToListAsync(ct);
        var names = await ActorNamesAsync(db, rows.Select(row => row.ActorUserId).Distinct().ToArray(), ct);
        var items = rows.Select(row => new CommitmentChangeResponse(
            row.Action,
            row.ActorUserId.Value,
            names.GetValueOrDefault(row.ActorUserId, string.Empty),
            row.At)).ToArray();
        return Results.Ok(new CommitmentHistoryResponse(items));
    }

    private static async Task<IResult> SetClosedAsync(
        Guid workspaceId,
        Guid commitmentId,
        bool closed,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IOutboxWriter outbox,
        IAuditWriter audit,
        IClock clock,
        CancellationToken ct)
    {
        var gate = await OpenMutableAsync(workspaceId, commitmentId, http, db, tenant, permissions, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var item = gate.Item!;
        var now = clock.UtcNow;
        if (closed)
        {
            if (item.State != CommitmentState.Closed)
            {
                item.State = CommitmentState.Closed;
                item.ClosedAt = now;
                item.ClosedBy = gate.UserId;
                item.UpdatedAt = now;
                AddChange(db, item, gate.UserId, CommitmentChangeActions.Closed, now);
                AddAudit(audit, item, gate.UserId, AuditActions.CommitmentClose, now);
                AddOutbox(outbox, item, "closed", now);
                await db.SaveChangesAsync(ct);
            }
        }
        else if (item.State != CommitmentState.Open)
        {
            item.State = CommitmentState.Open;
            item.ClosedAt = null;
            item.ClosedBy = null;
            item.UpdatedAt = now;
            AddChange(db, item, gate.UserId, CommitmentChangeActions.Reopened, now);
            AddAudit(audit, item, gate.UserId, AuditActions.CommitmentReopen, now);
            AddOutbox(outbox, item, "reopened", now);
            await db.SaveChangesAsync(ct);
        }

        var removed = await IsSourceRemovedAsync(db, item, ct);
        return Results.Ok(await ToResponseAsync(db, item, gate.UserId, removed, ct));
    }

    private static async Task<ItemGate> OpenVisibleAsync(
        Guid workspaceId,
        Guid commitmentId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return new ItemGate(null, profile.Id, Results.Forbid());
        }

        var item = await db.Commitments.FirstOrDefaultAsync(
            row => row.Id == commitmentId && row.WorkspaceId == workspace.Id, ct);
        if (item is null)
        {
            return new ItemGate(null, profile.Id, Results.NotFound());
        }

        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(x => x.Id == item.ChannelId, ct);
        var canRead = channel is not null && await CanReadChannelAsync(db, channel, profile.Id, ct);
        if (!CommitmentPolicies.CanSee(item, profile.Id, canRead))
        {
            return new ItemGate(null, profile.Id, Results.NotFound());
        }

        return new ItemGate(item, profile.Id, null);
    }

    private static async Task<ItemGate> OpenMutableAsync(
        Guid workspaceId,
        Guid commitmentId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        CancellationToken ct)
    {
        var gate = await OpenVisibleAsync(workspaceId, commitmentId, http, db, tenant, clock, ct);
        if (gate.Error is not null || gate.Item is null)
        {
            return gate;
        }

        var isAdmin = await permissions.HasPermissionAsync(
            gate.Item.TenantId, gate.UserId, Permissions.Workspace.Admin, ct);
        if (!CommitmentPolicies.CanMutate(gate.Item, gate.UserId, isAdmin))
        {
            return new ItemGate(null, gate.UserId, Results.Forbid());
        }

        return gate;
    }

    private static async Task<IResult> VisibleOrForbid(
        VibeChatDbContext db,
        ITenantContext tenant,
        Commitment item,
        UserId viewerId,
        CancellationToken ct)
    {
        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(x => x.Id == item.ChannelId, ct);
        var canRead = channel is not null && await CanReadChannelAsync(db, channel, viewerId, ct);
        if (!CommitmentPolicies.CanSee(item, viewerId, canRead))
        {
            return Results.Forbid();
        }

        var removed = await IsSourceRemovedAsync(db, item, ct);
        return Results.Ok(await ToResponseAsync(db, item, viewerId, removed, ct));
    }

    private static async Task<bool> CanReadChannelAsync(
        VibeChatDbContext db,
        Channel channel,
        UserId userId,
        CancellationToken ct)
    {
        var isWorkspaceMember = await db.WorkspaceMembers.IgnoreQueryFilters().AnyAsync(
            row => row.TenantId == channel.TenantId
                && row.WorkspaceId == channel.WorkspaceId
                && row.UserId == userId, ct);
        var isChannelMember = await db.ChannelMembers.IgnoreQueryFilters().AnyAsync(
            row => row.TenantId == channel.TenantId
                && row.ChannelId == channel.Id
                && row.UserId == userId
                && row.LeftAt == null, ct);
        if (!isWorkspaceMember && !isChannelMember)
        {
            return false;
        }

        return !channel.Type.RequiresChannelMembership() || isChannelMember;
    }

    private static async Task<bool> IsSourceRemovedAsync(VibeChatDbContext db, Commitment item, CancellationToken ct)
    {
        if (item.SourceRedacted)
        {
            return true;
        }

        var deletedAt = await db.Messages.AsNoTracking()
            .Where(row => row.Id == item.SourceMessageId)
            .Select(row => row.DeletedAt)
            .FirstOrDefaultAsync(ct);
        return deletedAt is not null;
    }

    private static async Task<CommitmentResponse> ToResponseAsync(
        VibeChatDbContext db,
        Commitment item,
        UserId viewerId,
        bool sourceRemoved,
        CancellationToken ct)
    {
        var channel = await db.Channels.AsNoTracking().FirstAsync(x => x.Id == item.ChannelId, ct);
        var channelName = await MessagingEndpointHelpers.ResolveSavedChannelDisplayNameAsync(db, channel, viewerId, ct);
        string? ownerName = null;
        if (item.OwnerUserId is UserId ownerId)
        {
            ownerName = await db.UserProfiles.AsNoTracking()
                .Where(row => row.Id == ownerId)
                .Select(row => row.DisplayName)
                .FirstOrDefaultAsync(ct);
        }

        return new CommitmentResponse(
            item.Id,
            CommitmentPolicies.KindToken(item.Kind),
            item.Title,
            item.Description,
            item.OwnerUserId?.Value,
            ownerName,
            item.DueAt,
            CommitmentPolicies.StateToken(item.State),
            item.ChannelId.Value,
            channelName,
            item.SourceMessageId.Value,
            item.SourceThreadId,
            item.SourceVersion,
            sourceRemoved || item.SourceRedacted,
            CommitmentPolicies.VisibilityToken(item.Visibility),
            item.CreatedBy.Value,
            item.CreatedAt,
            item.UpdatedAt,
            item.ClosedAt,
            item.ExcerptHash);
    }

    private static async Task<Dictionary<UserId, string>> ActorNamesAsync(
        VibeChatDbContext db,
        UserId[] ids,
        CancellationToken ct)
    {
        if (ids.Length == 0)
        {
            return [];
        }

        return await db.UserProfiles.AsNoTracking()
            .Where(row => ids.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.DisplayName, ct);
    }

    private static string Snapshot(Channel channel, UserId actor) =>
        JsonSerializer.Serialize(new
        {
            method = CommitmentPolicies.ConvertMethod,
            channelType = channel.Type.ToString(),
            actorId = actor.Value,
        });

    private static void AddChange(VibeChatDbContext db, Commitment item, UserId actor, string action, DateTimeOffset at)
    {
        db.CommitmentChanges.Add(new CommitmentChange
        {
            Id = Guid.NewGuid(),
            TenantId = item.TenantId,
            CommitmentId = item.Id,
            ActorUserId = actor,
            Action = action,
            At = at,
        });
    }

    private static void AddAudit(IAuditWriter audit, Commitment item, UserId actor, string action, DateTimeOffset at)
    {
        audit.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = item.TenantId,
            ActorUserId = actor,
            Action = action,
            EntityType = "Commitment",
            EntityId = item.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new
            {
                kind = CommitmentPolicies.KindToken(item.Kind),
                channelId = item.ChannelId.Value,
                sourceMessageId = item.SourceMessageId.Value,
                state = CommitmentPolicies.StateToken(item.State),
            }),
            OccurredAt = at,
        });
    }

    private static void AddOutbox(IOutboxWriter outbox, Commitment item, string action, DateTimeOffset at)
    {
        outbox.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = item.TenantId,
            Type = CommitmentPolicies.EventType(item.Kind, action),
            OccurredAt = at,
            Payload = JsonSerializer.Serialize(new
            {
                tenantId = item.TenantId.Value,
                workspaceId = item.WorkspaceId.Value,
                commitmentId = item.Id,
                channelId = item.ChannelId.Value,
                kind = CommitmentPolicies.KindToken(item.Kind),
                ownerUserId = item.OwnerUserId?.Value,
                createdBy = item.CreatedBy.Value,
                visibility = CommitmentPolicies.VisibilityToken(item.Visibility),
                state = CommitmentPolicies.StateToken(item.State),
                sourceMessageId = item.SourceMessageId.Value,
            }),
        });
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = CommitmentPolicies.Normalize(value);
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static IResult Bad(string code) => Results.BadRequest(new { error = code });

    private readonly record struct ItemGate(Commitment? Item, UserId UserId, IResult? Error);
}
