using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Api.Contracts;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Api.Endpoints;

internal static class IncidentEndpoints
{
    internal static void MapIncidents(this RouteGroupBuilder v1)
    {
        v1.MapPost("/workspaces/{workspaceId:guid}/incidents", CreateAsync)
            .RequirePermission(Permissions.Incident.Manage);
        v1.MapGet("/workspaces/{workspaceId:guid}/incidents/current", CurrentAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapGet("/workspaces/{workspaceId:guid}/incidents/{incidentId:guid}", GetAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/workspaces/{workspaceId:guid}/incidents/{incidentId:guid}/transitions", TransitionAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/workspaces/{workspaceId:guid}/incidents/{incidentId:guid}/evidence", EvidenceAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/workspaces/{workspaceId:guid}/incidents/{incidentId:guid}/updates", UpdateAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/workspaces/{workspaceId:guid}/incidents/{incidentId:guid}/steps/{stepId:guid}/run", RunStepAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapGet("/workspaces/{workspaceId:guid}/incidents/{incidentId:guid}/postmortem", ExportAsync)
            .RequirePermission(Permissions.Incident.Export);
    }

    private static async Task<IResult> CreateAsync(
        Guid workspaceId,
        CreateIncidentRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IAuditWriter audit,
        IOutboxWriter outbox,
        IClock clock,
        IOptionsMonitor<IncidentOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var key = IncidentPolicies.Normalize(request.IdempotencyKey);
        var title = IncidentPolicies.Normalize(request.Title);
        var severity = IncidentPolicies.Normalize(request.Severity).ToLowerInvariant();
        var template = IncidentPolicies.Normalize(request.TemplateKey);
        var correlation = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? null
            : IncidentPolicies.Normalize(request.CorrelationId);
        if (!IncidentPolicies.IsValidIdempotencyKey(key))
        {
            return Bad("InvalidIdempotencyKey");
        }

        if (!IncidentPolicies.IsValidTitle(title))
        {
            return Bad("InvalidTitle");
        }

        if (!IncidentPolicies.IsValidSeverity(severity))
        {
            return Bad("InvalidSeverity");
        }

        if (!IncidentPolicies.IsValidTemplate(template))
        {
            return Bad("UnknownIncidentTemplate");
        }

        if (!IncidentPolicies.IsValidCorrelation(correlation))
        {
            return Bad("InvalidCorrelation");
        }

        var gate = await OpenAsync(workspaceId, http, db, tenantContext, clock, ct);
        if (gate.Error is not null || gate.Profile is null || gate.Workspace is null)
        {
            return gate.Error!;
        }

        var existing = await db.IncidentRooms.FirstOrDefaultAsync(
            row => row.CreatedBy == gate.Profile.Id && row.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            return Results.Ok(await MapAsync(db, existing, true, ct));
        }

        var open = await db.IncidentRooms.CountAsync(
            row => row.WorkspaceId == gate.Workspace.Id && row.Status != IncidentPolicies.Resolved, ct);
        if (open >= IncidentPolicies.MaxOpenPerWorkspace)
        {
            return Bad("IncidentLimitReached");
        }

        var responders = (request.ResponderIds ?? [])
            .Where(id => id != Guid.Empty && id != gate.Profile.Id.Value)
            .Distinct()
            .ToArray();
        if (responders.Length + 1 > IncidentPolicies.MaxResponders)
        {
            return Bad("TooManyResponders");
        }

        foreach (var responder in responders)
        {
            var userId = new UserId(responder);
            var member = await db.WorkspaceMembers.AnyAsync(
                row => row.WorkspaceId == gate.Workspace.Id && row.UserId == userId, ct);
            if (!member)
            {
                return Bad("ResponderNotMember");
            }
        }

        var now = clock.UtcNow;
        var incidentId = Guid.NewGuid();
        var channel = new Channel
        {
            Id = ChannelId.New(),
            TenantId = gate.Workspace.TenantId,
            WorkspaceId = gate.Workspace.Id,
            Name = IncidentPolicies.ChannelName(title, incidentId),
            Type = ChannelType.Private,
            Layout = ChannelLayout.Stream,
            CreatedAt = now,
            CreatedBy = gate.Profile.Id,
        };
        var room = new IncidentRoom
        {
            Id = incidentId,
            TenantId = gate.Workspace.TenantId,
            WorkspaceId = gate.Workspace.Id,
            ChannelId = channel.Id,
            TemplateKey = template,
            Title = title,
            Severity = severity,
            Status = IncidentPolicies.Declared,
            CommanderUserId = gate.Profile.Id,
            CorrelationId = correlation,
            IdempotencyKey = key,
            CreatedBy = gate.Profile.Id,
            DeclaredAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Channels.Add(channel);
        db.ChannelMembers.Add(new ChannelMember
        {
            Id = Guid.NewGuid(),
            TenantId = room.TenantId,
            ChannelId = channel.Id,
            UserId = gate.Profile.Id,
            JoinedAt = now,
        });
        db.IncidentRooms.Add(room);
        db.IncidentResponders.Add(new IncidentResponder
        {
            Id = Guid.NewGuid(),
            TenantId = room.TenantId,
            IncidentId = room.Id,
            UserId = gate.Profile.Id,
            Role = IncidentPolicies.CommanderRole,
            CreatedAt = now,
        });
        foreach (var responder in responders)
        {
            var userId = new UserId(responder);
            db.ChannelMembers.Add(new ChannelMember
            {
                Id = Guid.NewGuid(),
                TenantId = room.TenantId,
                ChannelId = channel.Id,
                UserId = userId,
                JoinedAt = now,
            });
            db.IncidentResponders.Add(new IncidentResponder
            {
                Id = Guid.NewGuid(),
                TenantId = room.TenantId,
                IncidentId = room.Id,
                UserId = userId,
                Role = IncidentPolicies.ResponderRole,
                CreatedAt = now,
            });
        }

        var index = 0;
        foreach (var code in IncidentPolicies.TemplateSteps(template))
        {
            db.IncidentSteps.Add(new IncidentPlaybookStep
            {
                Id = Guid.NewGuid(),
                TenantId = room.TenantId,
                IncidentId = room.Id,
                StepIndex = index,
                Code = code,
                OwnerUserId = gate.Profile.Id,
                Status = IncidentPolicies.StepPending,
                AutomationId = code == IncidentPolicies.StepMitigate ? request.AutomationId : null,
            });
            index++;
        }

        var declaredAudit = NewAudit(room, gate.Profile.Id, AuditActions.IncidentDeclared, now, new { severity, status = room.Status });
        audit.Add(declaredAudit);
        audit.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = room.TenantId,
            ActorUserId = gate.Profile.Id,
            Action = AuditActions.ChannelCreate,
            EntityType = "Channel",
            EntityId = channel.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new { workspaceId, type = ChannelType.Private.ToString() }),
            OccurredAt = now,
        });
        db.IncidentTimeline.Add(new IncidentTimelineEntry
        {
            Id = Guid.NewGuid(),
            TenantId = room.TenantId,
            IncidentId = room.Id,
            Kind = IncidentPolicies.KindAudit,
            SourceId = declaredAudit.Id,
            IdempotencyKey = $"declare-{room.Id:N}",
            CreatedAt = now,
        });
        AddOutbox(outbox, room, IncidentPolicies.EventDeclared, now);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var raced = await db.IncidentRooms.FirstOrDefaultAsync(
                row => row.CreatedBy == gate.Profile.Id && row.IdempotencyKey == key, ct);
            if (raced is null)
            {
                throw;
            }

            return Results.Ok(await MapAsync(db, raced, true, ct));
        }

        VibeChatMetrics.IncidentsDeclared.Add(1);
        return Results.Ok(await MapAsync(db, room, true, ct));
    }

    private static async Task<IResult> CurrentAsync(
        Guid workspaceId,
        Guid channelId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IPermissionChecker permissions,
        IClock clock,
        IOptionsMonitor<IncidentOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        if (channelId == Guid.Empty)
        {
            return Bad("InvalidChannel");
        }

        var gate = await OpenAsync(workspaceId, http, db, tenantContext, clock, ct);
        if (gate.Error is not null || gate.Profile is null || gate.Workspace is null)
        {
            return gate.Error!;
        }

        var canManage = await permissions.HasPermissionAsync(
            gate.Workspace.TenantId, gate.Profile.Id, Permissions.Incident.Manage, ct);
        var room = await db.IncidentRooms.FirstOrDefaultAsync(
            row => row.WorkspaceId == gate.Workspace.Id && row.ChannelId == new ChannelId(channelId), ct);
        if (room is null || !await IsMemberAsync(db, room.Id, gate.Profile.Id, ct))
        {
            return Results.Ok(new IncidentCurrentResponse(canManage, null));
        }

        var canExport = await permissions.HasPermissionAsync(
            gate.Workspace.TenantId, gate.Profile.Id, Permissions.Incident.Export, ct);
        return Results.Ok(new IncidentCurrentResponse(canManage, await MapAsync(db, room, canExport, ct)));
    }

    private static async Task<IResult> GetAsync(
        Guid workspaceId,
        Guid incidentId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IPermissionChecker permissions,
        IClock clock,
        IOptionsMonitor<IncidentOptions> options,
        CancellationToken ct)
    {
        var loaded = await LoadMemberAsync(workspaceId, incidentId, http, db, tenantContext, clock, options, ct);
        if (loaded.Error is not null || loaded.Room is null || loaded.Profile is null || loaded.Workspace is null)
        {
            return loaded.Error!;
        }

        var canExport = await permissions.HasPermissionAsync(
            loaded.Workspace.TenantId, loaded.Profile.Id, Permissions.Incident.Export, ct);
        return Results.Ok(await MapAsync(db, loaded.Room, canExport, ct));
    }

    private static async Task<IResult> TransitionAsync(
        Guid workspaceId,
        Guid incidentId,
        IncidentTransitionRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IAuditWriter audit,
        IOutboxWriter outbox,
        IPermissionChecker permissions,
        IClock clock,
        IOptionsMonitor<IncidentOptions> options,
        CancellationToken ct)
    {
        var loaded = await LoadMemberAsync(workspaceId, incidentId, http, db, tenantContext, clock, options, ct);
        if (loaded.Error is not null || loaded.Room is null || loaded.Profile is null || loaded.Workspace is null)
        {
            return loaded.Error!;
        }

        var next = IncidentPolicies.Normalize(request.Status);
        if (!IncidentPolicies.TryTransition(loaded.Room.Status, next))
        {
            return Conflict("InvalidIncidentTransition");
        }

        if (loaded.Room.Status == IncidentPolicies.Resolved)
        {
            return Conflict("IncidentClosed");
        }

        var now = clock.UtcNow;
        loaded.Room.Status = next;
        loaded.Room.UpdatedAt = now;
        if (next == IncidentPolicies.Mitigating)
        {
            loaded.Room.MitigatingAt = now;
        }

        var steps = await db.IncidentSteps.Where(row => row.IncidentId == loaded.Room.Id).ToListAsync(ct);
        var timeline = await db.IncidentTimeline.Where(row => row.IncidentId == loaded.Room.Id).ToListAsync(ct);
        string eventType;
        string action;
        if (next == IncidentPolicies.Resolved)
        {
            loaded.Room.ResolvedAt = now;
            loaded.Room.PostmortemJson = IncidentPolicies.BuildPostmortem(loaded.Room, steps, timeline);
            eventType = IncidentPolicies.EventResolved;
            action = AuditActions.IncidentResolved;
            VibeChatMetrics.IncidentsResolved.Add(1);
        }
        else
        {
            eventType = IncidentPolicies.EventTransitioned;
            action = AuditActions.IncidentTransitioned;
        }

        audit.Add(NewAudit(loaded.Room, loaded.Profile.Id, action, now, new { status = next, severity = loaded.Room.Severity }));
        AddOutbox(outbox, loaded.Room, eventType, now);
        await db.SaveChangesAsync(ct);
        var canExport = next == IncidentPolicies.Resolved
            && await permissions.HasPermissionAsync(loaded.Workspace.TenantId, loaded.Profile.Id, Permissions.Incident.Export, ct);
        return Results.Ok(await MapAsync(db, loaded.Room, canExport, ct));
    }

    private static async Task<IResult> EvidenceAsync(
        Guid workspaceId,
        Guid incidentId,
        IncidentEvidenceRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        IOptionsMonitor<IncidentOptions> options,
        CancellationToken ct)
    {
        var loaded = await LoadMemberAsync(workspaceId, incidentId, http, db, tenantContext, clock, options, ct);
        if (loaded.Error is not null || loaded.Room is null || loaded.Profile is null)
        {
            return loaded.Error!;
        }

        if (loaded.Room.Status == IncidentPolicies.Resolved)
        {
            return Conflict("IncidentClosed");
        }

        var key = IncidentPolicies.Normalize(request.IdempotencyKey);
        var kind = IncidentPolicies.Normalize(request.Kind);
        if (!IncidentPolicies.IsValidIdempotencyKey(key) || !IncidentPolicies.IsValidEvidenceKind(kind) || request.SourceId == Guid.Empty)
        {
            return Bad("InvalidEvidence");
        }

        var replay = await db.IncidentTimeline.FirstOrDefaultAsync(
            row => row.IncidentId == loaded.Room.Id && row.IdempotencyKey == key, ct);
        if (replay is not null)
        {
            return Results.Ok(await MapAsync(db, loaded.Room, false, ct));
        }

        if (!await EvidenceExistsAsync(db, loaded.Room, kind, request.SourceId, ct))
        {
            return Bad("EvidenceNotFound");
        }

        var count = await db.IncidentTimeline.CountAsync(row => row.IncidentId == loaded.Room.Id, ct);
        if (count >= IncidentPolicies.MaxTimeline)
        {
            return Bad("IncidentTimelineLimit");
        }

        db.IncidentTimeline.Add(new IncidentTimelineEntry
        {
            Id = Guid.NewGuid(),
            TenantId = loaded.Room.TenantId,
            IncidentId = loaded.Room.Id,
            Kind = kind,
            SourceId = request.SourceId,
            IdempotencyKey = key,
            CreatedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return Results.Ok(await MapAsync(db, loaded.Room, false, ct));
    }

    private static async Task<IResult> UpdateAsync(
        Guid workspaceId,
        Guid incidentId,
        IncidentUpdateRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IAuditWriter audit,
        IClock clock,
        IOptionsMonitor<IncidentOptions> options,
        CancellationToken ct)
    {
        var loaded = await LoadMemberAsync(workspaceId, incidentId, http, db, tenantContext, clock, options, ct);
        if (loaded.Error is not null || loaded.Room is null || loaded.Profile is null)
        {
            return loaded.Error!;
        }

        if (loaded.Room.Status == IncidentPolicies.Resolved)
        {
            return Conflict("IncidentClosed");
        }

        var key = IncidentPolicies.Normalize(request.IdempotencyKey);
        var note = IncidentPolicies.Normalize(request.Note);
        if (!IncidentPolicies.IsValidIdempotencyKey(key))
        {
            return Bad("InvalidIdempotencyKey");
        }

        if (!IncidentPolicies.IsValidNote(note))
        {
            return Bad("InvalidNote");
        }

        var replay = await db.IncidentTimeline.FirstOrDefaultAsync(
            row => row.IncidentId == loaded.Room.Id && row.IdempotencyKey == key, ct);
        if (replay is not null)
        {
            return Results.Ok(await MapAsync(db, loaded.Room, false, ct));
        }

        var now = clock.UtcNow;
        var entry = NewAudit(loaded.Room, loaded.Profile.Id, AuditActions.IncidentUpdated, now, new { note });
        audit.Add(entry);
        db.IncidentTimeline.Add(new IncidentTimelineEntry
        {
            Id = Guid.NewGuid(),
            TenantId = loaded.Room.TenantId,
            IncidentId = loaded.Room.Id,
            Kind = IncidentPolicies.KindAnnouncement,
            SourceId = entry.Id,
            IdempotencyKey = key,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        return Results.Ok(await MapAsync(db, loaded.Room, false, ct));
    }

    private static async Task<IResult> RunStepAsync(
        Guid workspaceId,
        Guid incidentId,
        Guid stepId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IAuditWriter audit,
        IOutboxWriter outbox,
        IClock clock,
        IOptionsMonitor<IncidentOptions> options,
        CancellationToken ct)
    {
        var loaded = await LoadMemberAsync(workspaceId, incidentId, http, db, tenantContext, clock, options, ct);
        if (loaded.Error is not null || loaded.Room is null || loaded.Profile is null)
        {
            return loaded.Error!;
        }

        if (loaded.Room.Status == IncidentPolicies.Resolved)
        {
            return Conflict("IncidentClosed");
        }

        var step = await db.IncidentSteps.FirstOrDefaultAsync(
            row => row.Id == stepId && row.IncidentId == loaded.Room.Id, ct);
        if (step is null)
        {
            return Results.NotFound(new { error = "IncidentStepNotFound" });
        }

        if (step.AutomationId is null)
        {
            step.Status = IncidentPolicies.StepDone;
            step.ErrorCode = null;
            await db.SaveChangesAsync(ct);
            return Results.Ok(await MapAsync(db, loaded.Room, false, ct));
        }

        var automation = await db.AutomationDefinitions.FirstOrDefaultAsync(
            row => row.Id == step.AutomationId && row.WorkspaceId == loaded.Room.WorkspaceId, ct);
        var admitted = automation is { Enabled: true, Status: AutomationStatus.Published };
        var now = clock.UtcNow;
        if (!admitted)
        {
            step.Status = IncidentPolicies.StepFailed;
            step.ErrorCode = "AutomationFailed";
            audit.Add(NewAudit(loaded.Room, loaded.Profile.Id, AuditActions.IncidentAutomationFailed, now, new { step = step.Code }));
            AddOutbox(outbox, loaded.Room, IncidentPolicies.EventAutomationFailed, now);
            VibeChatMetrics.IncidentAutomationFailed.Add(1);
        }
        else
        {
            step.Status = IncidentPolicies.StepDone;
            step.ErrorCode = null;
            step.EvidenceKind = "automation";
            step.EvidenceId = automation!.Id;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(await MapAsync(db, loaded.Room, false, ct));
    }

    private static async Task<IResult> ExportAsync(
        Guid workspaceId,
        Guid incidentId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IAuditWriter audit,
        IClock clock,
        IOptionsMonitor<IncidentOptions> options,
        CancellationToken ct)
    {
        var loaded = await LoadMemberAsync(workspaceId, incidentId, http, db, tenantContext, clock, options, ct);
        if (loaded.Error is not null || loaded.Room is null || loaded.Profile is null)
        {
            return loaded.Error!;
        }

        if (loaded.Room.Status != IncidentPolicies.Resolved || string.IsNullOrWhiteSpace(loaded.Room.PostmortemJson))
        {
            return Conflict("IncidentNotResolved");
        }

        audit.Add(NewAudit(loaded.Room, loaded.Profile.Id, AuditActions.IncidentExported, clock.UtcNow, new { status = loaded.Room.Status }));
        await db.SaveChangesAsync(ct);
        return Results.Ok(JsonSerializer.Deserialize<JsonElement>(loaded.Room.PostmortemJson));
    }

    private static async Task<bool> EvidenceExistsAsync(
        VibeChatDbContext db,
        IncidentRoom room,
        string kind,
        Guid sourceId,
        CancellationToken ct)
    {
        if (kind == IncidentPolicies.KindMessage)
        {
            return await db.Messages.AnyAsync(
                row => row.Id == new MessageId(sourceId) && row.ConversationId == room.ChannelId, ct);
        }

        if (kind == IncidentPolicies.KindDecision)
        {
            return await db.Commitments.AnyAsync(
                row => row.Id == sourceId && row.ChannelId == room.ChannelId, ct);
        }

        if (kind == IncidentPolicies.KindTask)
        {
            return await db.MessageTasks.AnyAsync(
                row => row.Id == sourceId && row.ChannelId == room.ChannelId, ct);
        }

        return await db.AuditEvents.AnyAsync(
            row => row.Id == sourceId && row.EntityId == room.Id.ToString(), ct);
    }

    private static async Task<IncidentResponse> MapAsync(
        VibeChatDbContext db,
        IncidentRoom room,
        bool canExport,
        CancellationToken ct)
    {
        var steps = await db.IncidentSteps.AsNoTracking()
            .Where(row => row.IncidentId == room.Id)
            .OrderBy(row => row.StepIndex)
            .ToListAsync(ct);
        var timeline = await db.IncidentTimeline.AsNoTracking()
            .Where(row => row.IncidentId == room.Id)
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .ToListAsync(ct);
        var announcementIds = timeline
            .Where(row => row.Kind == IncidentPolicies.KindAnnouncement)
            .Select(row => row.SourceId)
            .ToArray();
        var notes = announcementIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await NotesAsync(db, announcementIds, ct);
        return new IncidentResponse(
            room.Id,
            room.ChannelId.Value,
            room.Title,
            room.Severity,
            room.Status,
            room.CommanderUserId.Value,
            room.CorrelationId,
            room.DeclaredAt,
            room.MitigatingAt,
            room.ResolvedAt,
            canExport && room.Status == IncidentPolicies.Resolved,
            steps.Select(step => new IncidentStepResponse(
                step.Id, step.StepIndex, step.Code, step.Status, step.ErrorCode, step.AutomationId)).ToArray(),
            timeline.Select(entry => new IncidentTimelineResponse(
                entry.Id,
                entry.Kind,
                entry.SourceId,
                entry.CreatedAt,
                notes.TryGetValue(entry.SourceId, out var note) ? note : null)).ToArray());
    }

    private static async Task<Dictionary<Guid, string>> NotesAsync(
        VibeChatDbContext db,
        Guid[] ids,
        CancellationToken ct)
    {
        var rows = await db.AuditEvents.AsNoTracking().Where(row => ids.Contains(row.Id)).ToListAsync(ct);
        var notes = new Dictionary<Guid, string>();
        foreach (var row in rows)
        {
            try
            {
                using var document = JsonDocument.Parse(row.MetadataJson);
                if (document.RootElement.TryGetProperty("note", out var note) && note.ValueKind == JsonValueKind.String)
                {
                    var text = note.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        notes[row.Id] = text;
                    }
                }
            }
            catch (JsonException)
            {
                // A malformed audit row does not surface stored metadata.
            }
        }

        return notes;
    }

    private static async Task<bool> IsMemberAsync(VibeChatDbContext db, Guid incidentId, UserId userId, CancellationToken ct) =>
        await db.IncidentResponders.AnyAsync(row => row.IncidentId == incidentId && row.UserId == userId, ct);

    private static async Task<Loaded> LoadMemberAsync(
        Guid workspaceId,
        Guid incidentId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        IOptionsMonitor<IncidentOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return new Loaded(null, null, null, Disabled());
        }

        var gate = await OpenAsync(workspaceId, http, db, tenantContext, clock, ct);
        if (gate.Error is not null || gate.Workspace is null || gate.Profile is null)
        {
            return new Loaded(null, gate.Workspace, gate.Profile, gate.Error);
        }

        var room = await db.IncidentRooms.FirstOrDefaultAsync(
            row => row.Id == incidentId && row.WorkspaceId == gate.Workspace.Id, ct);
        if (room is null || !await IsMemberAsync(db, room.Id, gate.Profile.Id, ct))
        {
            return new Loaded(null, gate.Workspace, gate.Profile, Hidden());
        }

        return new Loaded(room, gate.Workspace, gate.Profile, null);
    }

    private static async Task<WorkspaceGate> OpenAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenantContext, ct);
        return workspace is null
            ? new WorkspaceGate(null, profile, Results.Forbid())
            : new WorkspaceGate(workspace, profile, null);
    }

    private static AuditEvent NewAudit(IncidentRoom room, UserId actor, string action, DateTimeOffset at, object metadata) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = room.TenantId,
            ActorUserId = actor,
            Action = action,
            EntityType = "Incident",
            EntityId = room.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(metadata),
            OccurredAt = at,
        };

    private static void AddOutbox(IOutboxWriter outbox, IncidentRoom room, string type, DateTimeOffset at)
    {
        outbox.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = room.TenantId,
            Type = type,
            OccurredAt = at,
            Payload = JsonSerializer.Serialize(new
            {
                incidentId = room.Id,
                workspaceId = room.WorkspaceId.Value,
                status = room.Status,
                correlationId = room.CorrelationId,
            }),
        });
    }

    private static IResult Bad(string code) => Results.BadRequest(new { error = code });

    private static IResult Conflict(string code) => Results.Json(new { error = code }, statusCode: StatusCodes.Status409Conflict);

    private static IResult Disabled() => Results.NotFound(new { error = "IncidentDisabled" });

    private static IResult Hidden() => Results.NotFound(new { error = "IncidentNotFound" });

    private sealed record WorkspaceGate(Workspace? Workspace, UserProfile? Profile, IResult? Error);

    private sealed record Loaded(IncidentRoom? Room, Workspace? Workspace, UserProfile? Profile, IResult? Error);
}
