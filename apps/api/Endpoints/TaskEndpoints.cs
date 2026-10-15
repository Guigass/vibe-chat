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

internal static class TaskEndpoints
{
    internal static void MapTasks(this RouteGroupBuilder v1)
    {
        v1.MapPost("/workspaces/{workspaceId:guid}/tasks", CreateAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapGet("/workspaces/{workspaceId:guid}/tasks", ListAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapGet("/workspaces/{workspaceId:guid}/tasks/{taskId:guid}", GetAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPatch("/workspaces/{workspaceId:guid}/tasks/{taskId:guid}", PatchAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/workspaces/{workspaceId:guid}/tasks/{taskId:guid}/complete", CompleteAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/workspaces/{workspaceId:guid}/tasks/{taskId:guid}/reopen", ReopenAsync)
            .RequirePermission(Permissions.Message.Read);
    }

    private static async Task<IResult> CreateAsync(
        Guid workspaceId,
        CreateTaskRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOutboxWriter outbox,
        IAuditWriter audit,
        IClock clock,
        CancellationToken ct)
    {
        var key = TaskPolicies.Normalize(request.IdempotencyKey);
        if (!TaskPolicies.IsValidIdempotencyKey(key))
        {
            return Bad("InvalidIdempotencyKey");
        }

        var title = TaskPolicies.Normalize(request.Title);
        if (!TaskPolicies.IsValidTitle(title))
        {
            return Bad("InvalidTitle");
        }

        var description = NormalizeOptional(request.Description);
        if (!TaskPolicies.IsValidDescription(description))
        {
            return Bad("InvalidDescription");
        }

        if (!TaskPolicies.TryParsePriority(request.Priority, out var priority))
        {
            return Bad("InvalidPriority");
        }

        if (!CommitmentPolicies.TryParseVisibility(request.Visibility, out var visibility))
        {
            return Bad("InvalidVisibility");
        }

        if (!TaskPolicies.TryNormalizeAssignees(request.AssigneeUserIds, out var assignees))
        {
            return Bad("TooManyAssignees");
        }

        if (!TaskPolicies.TryNormalizeChecklist(
                request.Checklist?.Select(item => (item.Text, item.Done)),
                out var checklist))
        {
            return Bad("InvalidChecklistItem");
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        if (!TaskPolicies.IsValidDue(request.DueAt, clock.UtcNow))
        {
            return Bad("InvalidDueAt");
        }

        var existing = await db.MessageTasks.FirstOrDefaultAsync(
            row => row.CreatedBy == profile.Id && row.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            return await VisibleOrForbid(db, existing, profile.Id, clock.UtcNow, ct);
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

        if (!TaskPolicies.IsConvertible(message.Body))
        {
            return Bad("NotConvertible");
        }

        foreach (var assignee in assignees)
        {
            if (!await CanReadChannelAsync(db, channel, new UserId(assignee), ct))
            {
                return Bad("AssigneeNotMember");
            }
        }

        var threadId = message.ThreadId ?? await db.MessageThreads.AsNoTracking()
            .Where(row => row.ParentMessageId == message.Id)
            .Select(row => (Guid?)row.Id)
            .FirstOrDefaultAsync(ct);

        var now = clock.UtcNow;
        var item = new MessageTask
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            ChannelId = channel.Id,
            Title = title,
            Description = description,
            Priority = priority,
            State = TaskState.Open,
            Version = 1,
            DueAt = request.DueAt,
            SourceMessageId = message.Id,
            SourceThreadId = threadId,
            SourceVersion = message.Sequence,
            ExcerptHash = TaskPolicies.ExcerptHash(message.Body),
            Method = TaskPolicies.ConvertMethod,
            CreatedBy = profile.Id,
            AuthzSnapshot = Snapshot(channel, profile.Id),
            Visibility = visibility,
            IdempotencyKey = key,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.MessageTasks.Add(item);
        AddAssignees(db, item, assignees, now);
        AddChecklist(db, item, checklist);
        AddAudit(audit, item, profile.Id, AuditActions.TaskCreate, now);
        AddOutbox(outbox, item, assignees, TaskPolicies.Created, now);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            var raced = await db.MessageTasks.FirstOrDefaultAsync(
                row => row.CreatedBy == profile.Id && row.IdempotencyKey == key, ct);
            if (raced is null)
            {
                throw;
            }

            return await VisibleOrForbid(db, raced, profile.Id, clock.UtcNow, ct);
        }

        return Results.Ok(await ToResponseAsync(db, item, profile.Id, sourceRemoved: false, clock.UtcNow, ct));
    }

    private static async Task<IResult> ListAsync(
        Guid workspaceId,
        Guid? channelId,
        string? view,
        string? state,
        string? cursor,
        int? limit,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        if (!TaskPolicies.TryParseView(view, out var viewFilter)
            || !TaskPolicies.TryParseListState(state, out var stateFilter))
        {
            return Bad("InvalidTaskFilter");
        }

        if (viewFilter == TaskView.Channel && channelId is null)
        {
            return Bad("InvalidTaskFilter");
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

        var pageSize = Math.Clamp(limit ?? TaskPolicies.DefaultPageSize, 1, TaskPolicies.MaxPageSize);
        var query = db.MessageTasks.AsNoTracking().Where(row => row.WorkspaceId == workspace.Id);
        if (viewFilter == TaskView.Channel && channelId is Guid channel)
        {
            query = query.Where(row => row.ChannelId == new ChannelId(channel));
        }

        if (viewFilter == TaskView.Mine)
        {
            var userId = profile.Id;
            query = query.Where(row => db.MessageTaskAssignees.Any(assignee =>
                assignee.TaskId == row.Id && assignee.UserId == userId));
        }

        query = stateFilter switch
        {
            TaskListState.Open => query.Where(row => row.State == TaskState.Open),
            TaskListState.InProgress => query.Where(row => row.State == TaskState.InProgress),
            TaskListState.Completed => query.Where(row => row.State == TaskState.Completed),
            TaskListState.Active => query.Where(row => row.State != TaskState.Completed),
            _ => query,
        };

        if (cursorAt is DateTimeOffset at && cursorId is Guid id)
        {
            query = query.Where(row => row.CreatedAt < at || (row.CreatedAt == at && row.Id.CompareTo(id) > 0));
        }

        var scanned = await query
            .OrderByDescending(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .Take(TaskPolicies.MaxScan)
            .ToListAsync(ct);

        var visible = new List<MessageTask>(pageSize + 1);
        var channels = new Dictionary<ChannelId, Channel>();
        var assigneeIds = await AssigneeIdsAsync(db, scanned.Select(row => row.Id).ToArray(), ct);
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

            var canRead = channelRow is not null && await CanReadChannelAsync(db, channelRow, profile.Id, ct);
            var isAssignee = assigneeIds.TryGetValue(row.Id, out var ids) && ids.Contains(profile.Id.Value);
            if (!TaskPolicies.CanSee(row, profile.Id, canRead, isAssignee))
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
        var now = clock.UtcNow;
        var responses = new TaskResponse[page.Length];
        for (var i = 0; i < page.Length; i++)
        {
            var removed = await IsSourceRemovedAsync(db, page[i], ct);
            responses[i] = await ToResponseAsync(db, page[i], profile.Id, removed, now, ct);
        }

        string? next = visible.Count > pageSize
            ? MessagingEndpointHelpers.EncodeSavedCursor(page[^1].CreatedAt, page[^1].Id)
            : null;
        return Results.Ok(new TaskPageResponse(responses, next));
    }

    private static async Task<IResult> GetAsync(
        Guid workspaceId,
        Guid taskId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        var gate = await OpenVisibleAsync(workspaceId, taskId, http, db, tenant, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var removed = await IsSourceRemovedAsync(db, gate.Item!, ct);
        return Results.Ok(await ToResponseAsync(db, gate.Item!, gate.UserId, removed, clock.UtcNow, ct));
    }

    private static async Task<IResult> PatchAsync(
        Guid workspaceId,
        Guid taskId,
        PatchTaskRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IOutboxWriter outbox,
        IAuditWriter audit,
        IClock clock,
        CancellationToken ct)
    {
        var gate = await OpenMutableAsync(workspaceId, taskId, http, db, tenant, permissions, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var item = gate.Item!;
        if (!TaskPolicies.TryBump(item.Version, request.Version, out var nextVersion))
        {
            return Conflict(item.Version);
        }

        if (request.Title is not null)
        {
            var title = TaskPolicies.Normalize(request.Title);
            if (!TaskPolicies.IsValidTitle(title))
            {
                return Bad("InvalidTitle");
            }

            item.Title = title;
        }

        if (request.Description is not null)
        {
            var description = NormalizeOptional(request.Description);
            if (!TaskPolicies.IsValidDescription(description))
            {
                return Bad("InvalidDescription");
            }

            item.Description = description;
        }

        if (request.Priority is not null)
        {
            if (!TaskPolicies.TryParsePriority(request.Priority, out var priority))
            {
                return Bad("InvalidPriority");
            }

            item.Priority = priority;
        }

        TaskState? nextState = null;
        if (request.State is not null)
        {
            if (!TaskPolicies.TryParseState(request.State, out var state))
            {
                return Bad("InvalidTaskState");
            }

            nextState = state;
        }

        Guid[]? assignees = null;
        if (request.ReplaceAssignees)
        {
            if (!TaskPolicies.TryNormalizeAssignees(request.AssigneeUserIds, out var normalized))
            {
                return Bad("TooManyAssignees");
            }

            var channel = await db.Channels.AsNoTracking().FirstAsync(x => x.Id == item.ChannelId, ct);
            foreach (var assignee in normalized)
            {
                if (!await CanReadChannelAsync(db, channel, new UserId(assignee), ct))
                {
                    return Bad("AssigneeNotMember");
                }
            }

            assignees = normalized;
        }

        if (request.ClearDue)
        {
            item.DueAt = null;
            item.OverdueNotifiedAt = null;
        }
        else if (request.DueAt is DateTimeOffset due)
        {
            if (!TaskPolicies.IsValidDue(due, clock.UtcNow))
            {
                return Bad("InvalidDueAt");
            }

            if (item.DueAt != due)
            {
                item.OverdueNotifiedAt = null;
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

        (string Text, bool Done)[]? checklist = null;
        if (request.ReplaceChecklist)
        {
            if (!TaskPolicies.TryNormalizeChecklist(
                    request.Checklist?.Select(row => (row.Text, row.Done)),
                    out var normalizedChecklist))
            {
                return Bad("InvalidChecklistItem");
            }

            checklist = normalizedChecklist;
        }

        var now = clock.UtcNow;
        var completing = nextState == TaskState.Completed && item.State != TaskState.Completed;
        var reopening = nextState is not null && nextState != TaskState.Completed && item.State == TaskState.Completed;
        if (nextState is TaskState stateValue)
        {
            item.State = stateValue;
            if (completing)
            {
                item.CompletedAt = now;
                item.CompletedBy = gate.UserId;
            }
            else if (reopening)
            {
                item.CompletedAt = null;
                item.CompletedBy = null;
            }
        }

        item.Version = nextVersion;
        item.UpdatedAt = now;
        if (assignees is not null)
        {
            await ReplaceAssigneesAsync(db, item, assignees, now, ct);
        }

        if (checklist is not null)
        {
            await ReplaceChecklistAsync(db, item, checklist, ct);
        }

        var assigneeIds = assignees ?? await CurrentAssigneeIdsAsync(db, item.Id, ct);
        var eventType = completing ? TaskPolicies.Completed : TaskPolicies.Updated;
        var auditAction = completing
            ? AuditActions.TaskComplete
            : reopening ? AuditActions.TaskReopen : AuditActions.TaskUpdate;
        AddAudit(audit, item, gate.UserId, auditAction, now);
        AddOutbox(outbox, item, assigneeIds, eventType, now);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(request.Version);
        }

        var removed = await IsSourceRemovedAsync(db, item, ct);
        return Results.Ok(await ToResponseAsync(db, item, gate.UserId, removed, now, ct));
    }

    private static Task<IResult> CompleteAsync(
        Guid workspaceId,
        Guid taskId,
        TaskVersionRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IOutboxWriter outbox,
        IAuditWriter audit,
        IClock clock,
        CancellationToken ct) =>
        SetCompletedAsync(workspaceId, taskId, request.Version, completed: true, http, db, tenant, permissions, outbox, audit, clock, ct);

    private static Task<IResult> ReopenAsync(
        Guid workspaceId,
        Guid taskId,
        TaskVersionRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IOutboxWriter outbox,
        IAuditWriter audit,
        IClock clock,
        CancellationToken ct) =>
        SetCompletedAsync(workspaceId, taskId, request.Version, completed: false, http, db, tenant, permissions, outbox, audit, clock, ct);

    private static async Task<IResult> SetCompletedAsync(
        Guid workspaceId,
        Guid taskId,
        int version,
        bool completed,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IOutboxWriter outbox,
        IAuditWriter audit,
        IClock clock,
        CancellationToken ct)
    {
        var gate = await OpenMutableAsync(workspaceId, taskId, http, db, tenant, permissions, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var item = gate.Item!;
        var now = clock.UtcNow;
        var already = completed ? item.State == TaskState.Completed : item.State != TaskState.Completed;
        if (already)
        {
            var removedEarly = await IsSourceRemovedAsync(db, item, ct);
            return Results.Ok(await ToResponseAsync(db, item, gate.UserId, removedEarly, now, ct));
        }

        if (!TaskPolicies.TryBump(item.Version, version, out var nextVersion))
        {
            return Conflict(item.Version);
        }

        item.Version = nextVersion;
        item.UpdatedAt = now;
        if (completed)
        {
            item.State = TaskState.Completed;
            item.CompletedAt = now;
            item.CompletedBy = gate.UserId;
        }
        else
        {
            item.State = TaskState.Open;
            item.CompletedAt = null;
            item.CompletedBy = null;
        }

        var assigneeIds = await CurrentAssigneeIdsAsync(db, item.Id, ct);
        AddAudit(audit, item, gate.UserId, completed ? AuditActions.TaskComplete : AuditActions.TaskReopen, now);
        AddOutbox(outbox, item, assigneeIds, completed ? TaskPolicies.Completed : TaskPolicies.Updated, now);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(version);
        }

        var removed = await IsSourceRemovedAsync(db, item, ct);
        return Results.Ok(await ToResponseAsync(db, item, gate.UserId, removed, now, ct));
    }

    private static async Task<ItemGate> OpenVisibleAsync(
        Guid workspaceId,
        Guid taskId,
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

        var item = await db.MessageTasks.FirstOrDefaultAsync(
            row => row.Id == taskId && row.WorkspaceId == workspace.Id, ct);
        if (item is null)
        {
            return new ItemGate(null, profile.Id, Results.NotFound());
        }

        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(x => x.Id == item.ChannelId, ct);
        var canRead = channel is not null && await CanReadChannelAsync(db, channel, profile.Id, ct);
        var isAssignee = await db.MessageTaskAssignees.AsNoTracking()
            .AnyAsync(row => row.TaskId == item.Id && row.UserId == profile.Id, ct);
        if (!TaskPolicies.CanSee(item, profile.Id, canRead, isAssignee))
        {
            return new ItemGate(null, profile.Id, Results.NotFound());
        }

        return new ItemGate(item, profile.Id, null);
    }

    private static async Task<ItemGate> OpenMutableAsync(
        Guid workspaceId,
        Guid taskId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        CancellationToken ct)
    {
        var gate = await OpenVisibleAsync(workspaceId, taskId, http, db, tenant, clock, ct);
        if (gate.Error is not null || gate.Item is null)
        {
            return gate;
        }

        var isAssignee = await db.MessageTaskAssignees.AsNoTracking()
            .AnyAsync(row => row.TaskId == gate.Item.Id && row.UserId == gate.UserId, ct);
        var isAdmin = await permissions.HasPermissionAsync(
            gate.Item.TenantId, gate.UserId, Permissions.Workspace.Admin, ct);
        if (!TaskPolicies.CanMutate(gate.Item, gate.UserId, isAssignee, isAdmin))
        {
            return new ItemGate(null, gate.UserId, Results.Forbid());
        }

        return gate;
    }

    private static async Task<IResult> VisibleOrForbid(
        VibeChatDbContext db,
        MessageTask item,
        UserId viewerId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(x => x.Id == item.ChannelId, ct);
        var canRead = channel is not null && await CanReadChannelAsync(db, channel, viewerId, ct);
        var isAssignee = await db.MessageTaskAssignees.AsNoTracking()
            .AnyAsync(row => row.TaskId == item.Id && row.UserId == viewerId, ct);
        if (!TaskPolicies.CanSee(item, viewerId, canRead, isAssignee))
        {
            return Results.Forbid();
        }

        var removed = await IsSourceRemovedAsync(db, item, ct);
        return Results.Ok(await ToResponseAsync(db, item, viewerId, removed, now, ct));
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

    private static async Task<bool> IsSourceRemovedAsync(VibeChatDbContext db, MessageTask item, CancellationToken ct)
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

    private static async Task<TaskResponse> ToResponseAsync(
        VibeChatDbContext db,
        MessageTask item,
        UserId viewerId,
        bool sourceRemoved,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var channel = await db.Channels.AsNoTracking().FirstAsync(x => x.Id == item.ChannelId, ct);
        var channelName = await MessagingEndpointHelpers.ResolveSavedChannelDisplayNameAsync(db, channel, viewerId, ct);
        var assignees = await db.MessageTaskAssignees.AsNoTracking()
            .Where(row => row.TaskId == item.Id)
            .ToListAsync(ct);
        var names = await ActorNamesAsync(db, assignees.Select(row => row.UserId).Distinct().ToArray(), ct);
        var checklist = await db.MessageTaskChecklistItems.AsNoTracking()
            .Where(row => row.TaskId == item.Id)
            .OrderBy(row => row.Position)
            .ToListAsync(ct);
        return new TaskResponse(
            item.Id,
            item.Title,
            item.Description,
            TaskPolicies.PriorityToken(item.Priority),
            TaskPolicies.StateToken(item.State),
            item.Version,
            item.DueAt,
            TaskPolicies.IsOverdue(item.DueAt, item.State, now),
            assignees.Select(row => new TaskAssigneeResponse(row.UserId.Value, names.GetValueOrDefault(row.UserId, string.Empty))).ToArray(),
            checklist.Select(row => new TaskChecklistResponse(row.Id, row.Position, row.Text, row.Done)).ToArray(),
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
            item.CompletedAt);
    }

    private static async Task<Dictionary<Guid, Guid[]>> AssigneeIdsAsync(
        VibeChatDbContext db,
        Guid[] taskIds,
        CancellationToken ct)
    {
        if (taskIds.Length == 0)
        {
            return [];
        }

        var rows = await db.MessageTaskAssignees.AsNoTracking()
            .Where(row => taskIds.Contains(row.TaskId))
            .Select(row => new { row.TaskId, UserId = row.UserId.Value })
            .ToListAsync(ct);
        return rows.GroupBy(row => row.TaskId)
            .ToDictionary(group => group.Key, group => group.Select(row => row.UserId).ToArray());
    }

    private static async Task<Guid[]> CurrentAssigneeIdsAsync(VibeChatDbContext db, Guid taskId, CancellationToken ct) =>
        await db.MessageTaskAssignees.AsNoTracking()
            .Where(row => row.TaskId == taskId)
            .Select(row => row.UserId.Value)
            .ToArrayAsync(ct);

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

    private static void AddAssignees(VibeChatDbContext db, MessageTask item, Guid[] assignees, DateTimeOffset now)
    {
        foreach (var assignee in assignees)
        {
            db.MessageTaskAssignees.Add(new MessageTaskAssignee
            {
                Id = Guid.NewGuid(),
                TenantId = item.TenantId,
                TaskId = item.Id,
                UserId = new UserId(assignee),
                AssignedAt = now,
            });
        }
    }

    private static async Task ReplaceAssigneesAsync(
        VibeChatDbContext db,
        MessageTask item,
        Guid[] assignees,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var existing = await db.MessageTaskAssignees.Where(row => row.TaskId == item.Id).ToListAsync(ct);
        db.MessageTaskAssignees.RemoveRange(existing);
        AddAssignees(db, item, assignees, now);
    }

    private static void AddChecklist(VibeChatDbContext db, MessageTask item, (string Text, bool Done)[] checklist)
    {
        for (var i = 0; i < checklist.Length; i++)
        {
            db.MessageTaskChecklistItems.Add(new MessageTaskChecklistItem
            {
                Id = Guid.NewGuid(),
                TenantId = item.TenantId,
                TaskId = item.Id,
                Position = i,
                Text = checklist[i].Text,
                Done = checklist[i].Done,
            });
        }
    }

    private static async Task ReplaceChecklistAsync(
        VibeChatDbContext db,
        MessageTask item,
        (string Text, bool Done)[] checklist,
        CancellationToken ct)
    {
        var existing = await db.MessageTaskChecklistItems.Where(row => row.TaskId == item.Id).ToListAsync(ct);
        db.MessageTaskChecklistItems.RemoveRange(existing);
        AddChecklist(db, item, checklist);
    }

    private static string Snapshot(Channel channel, UserId actor) =>
        JsonSerializer.Serialize(new
        {
            method = TaskPolicies.ConvertMethod,
            channelType = channel.Type.ToString(),
            actorId = actor.Value,
        });

    private static void AddAudit(IAuditWriter audit, MessageTask item, UserId actor, string action, DateTimeOffset at)
    {
        audit.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = item.TenantId,
            ActorUserId = actor,
            Action = action,
            EntityType = "Task",
            EntityId = item.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new
            {
                channelId = item.ChannelId.Value,
                sourceMessageId = item.SourceMessageId.Value,
                state = TaskPolicies.StateToken(item.State),
                version = item.Version,
            }),
            OccurredAt = at,
        });
    }

    private static void AddOutbox(IOutboxWriter outbox, MessageTask item, Guid[] assignees, string type, DateTimeOffset at)
    {
        outbox.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = item.TenantId,
            Type = type,
            OccurredAt = at,
            Payload = JsonSerializer.Serialize(new
            {
                tenantId = item.TenantId.Value,
                workspaceId = item.WorkspaceId.Value,
                taskId = item.Id,
                channelId = item.ChannelId.Value,
                createdBy = item.CreatedBy.Value,
                assigneeUserIds = assignees,
                visibility = CommitmentPolicies.VisibilityToken(item.Visibility),
                state = TaskPolicies.StateToken(item.State),
                sourceMessageId = item.SourceMessageId.Value,
            }),
        });
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = TaskPolicies.Normalize(value);
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static IResult Bad(string code) => Results.BadRequest(new { error = code });

    private static IResult Conflict(int version) =>
        Results.Json(new { error = "TaskVersionConflict", currentVersion = version }, statusCode: StatusCodes.Status409Conflict);

    private readonly record struct ItemGate(MessageTask? Item, UserId UserId, IResult? Error);
}
