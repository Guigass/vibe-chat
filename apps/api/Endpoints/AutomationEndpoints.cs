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
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Api.Endpoints;

internal static class AutomationEndpoints
{
    internal static void MapAutomations(this RouteGroupBuilder v1)
    {
        v1.MapPost("/workspaces/{workspaceId:guid}/automations", CreateAsync)
            .RequirePermission(Permissions.Automation.Manage);
        v1.MapGet("/workspaces/{workspaceId:guid}/automations", ListAsync)
            .RequirePermission(Permissions.Automation.Manage);
        v1.MapGet("/workspaces/{workspaceId:guid}/automations/{automationId:guid}", GetAsync)
            .RequirePermission(Permissions.Automation.Manage);
        v1.MapPatch("/workspaces/{workspaceId:guid}/automations/{automationId:guid}", UpdateAsync)
            .RequirePermission(Permissions.Automation.Manage);
        v1.MapPost("/workspaces/{workspaceId:guid}/automations/{automationId:guid}/publish", PublishAsync)
            .RequirePermission(Permissions.Automation.Manage);
        v1.MapPost("/workspaces/{workspaceId:guid}/automations/{automationId:guid}/enable", EnableAsync)
            .RequirePermission(Permissions.Automation.Manage);
        v1.MapPost("/workspaces/{workspaceId:guid}/automations/{automationId:guid}/disable", DisableAsync)
            .RequirePermission(Permissions.Automation.Manage);
        v1.MapPost("/workspaces/{workspaceId:guid}/automations/{automationId:guid}/dry-run", DryRunAsync)
            .RequirePermission(Permissions.Automation.Manage);
        v1.MapPost("/workspaces/{workspaceId:guid}/automations/{automationId:guid}/trigger", TriggerAsync)
            .RequirePermission(Permissions.Automation.Manage);
        v1.MapGet("/workspaces/{workspaceId:guid}/automations/{automationId:guid}/runs", ListRunsAsync)
            .RequirePermission(Permissions.Automation.Manage);
        v1.MapGet("/workspaces/{workspaceId:guid}/automations/{automationId:guid}/runs/{runId:guid}", GetRunAsync)
            .RequirePermission(Permissions.Automation.Manage);
        v1.MapPost("/workspaces/{workspaceId:guid}/automations/{automationId:guid}/runs/{runId:guid}/replay", ReplayAsync)
            .RequirePermission(Permissions.Automation.Manage);
        v1.MapPost("/workspaces/{workspaceId:guid}/automations/{automationId:guid}/hooks", HookAsync)
            .AllowAnonymous()
            .AllowPermissionGateExempt("automation webhook token (B-125)");
    }

    private static async Task<IResult> CreateAsync(
        Guid workspaceId,
        CreateAutomationRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IAuditWriter audit,
        IClock clock,
        IOptionsMonitor<AutomationOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var key = AutomationPolicies.Normalize(request.IdempotencyKey);
        var name = AutomationPolicies.Normalize(request.Name);
        if (!AutomationPolicies.IsValidIdempotencyKey(key))
        {
            return Bad("InvalidIdempotencyKey");
        }

        if (!AutomationPolicies.IsValidName(name))
        {
            return Bad("InvalidName");
        }

        if (!AutomationPolicies.TryParse(request.Definition.GetRawText(), out var model, out var error) || model is null)
        {
            return Bad(error);
        }

        var gate = await OpenAsync(workspaceId, http, db, tenantContext, clock, ct);
        if (gate.Error is not null || gate.Profile is null || gate.Workspace is null)
        {
            return gate.Error!;
        }

        var existing = await db.AutomationDefinitions.FirstOrDefaultAsync(
            row => row.CreatedBy == gate.Profile.Id && row.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            return Results.Ok(await MapAsync(db, existing, null, ct));
        }

        var live = await db.AutomationDefinitions.CountAsync(row => row.WorkspaceId == gate.Workspace.Id, ct);
        if (live >= AutomationPolicies.MaxAutomationsPerWorkspace)
        {
            return Bad("AutomationLimitReached");
        }

        if (!await ChannelsInWorkspaceAsync(db, gate.Workspace.Id, model.ChannelIds, ct))
        {
            return Results.Forbid();
        }

        var now = clock.UtcNow;
        var definition = new AutomationDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = gate.Workspace.TenantId,
            WorkspaceId = gate.Workspace.Id,
            Name = name,
            Status = AutomationStatus.Draft,
            Enabled = false,
            DraftJson = model.ToJson(),
            TriggerType = model.Trigger.Type,
            CreatedBy = gate.Profile.Id,
            IdempotencyKey = key,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AutomationDefinitions.Add(definition);
        Audit(audit, definition, gate.Profile.Id, AutomationPolicies.Created, now);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await MapAsync(db, definition, null, ct));
    }

    private static async Task<IResult> ListAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        IOptionsMonitor<AutomationOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var gate = await OpenAsync(workspaceId, http, db, tenantContext, clock, ct);
        if (gate.Error is not null || gate.Workspace is null)
        {
            return gate.Error!;
        }

        var rows = await db.AutomationDefinitions
            .Where(row => row.WorkspaceId == gate.Workspace.Id)
            .OrderByDescending(row => row.UpdatedAt)
            .Take(AutomationPolicies.MaxPageSize)
            .ToListAsync(ct);
        var items = new List<AutomationResponse>();
        foreach (var row in rows)
        {
            items.Add(await MapAsync(db, row, null, ct));
        }

        return Results.Ok(new AutomationListResponse(items.ToArray()));
    }

    private static async Task<IResult> GetAsync(
        Guid workspaceId,
        Guid automationId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        IOptionsMonitor<AutomationOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var loaded = await LoadAsync(workspaceId, automationId, http, db, tenantContext, clock, ct);
        return loaded.Error ?? Results.Ok(await MapAsync(db, loaded.Definition!, null, ct));
    }

    private static async Task<IResult> UpdateAsync(
        Guid workspaceId,
        Guid automationId,
        UpdateAutomationRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        IOptionsMonitor<AutomationOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var name = AutomationPolicies.Normalize(request.Name);
        if (!AutomationPolicies.IsValidName(name))
        {
            return Bad("InvalidName");
        }

        if (!AutomationPolicies.TryParse(request.Definition.GetRawText(), out var model, out var error) || model is null)
        {
            return Bad(error);
        }

        var loaded = await LoadAsync(workspaceId, automationId, http, db, tenantContext, clock, ct);
        if (loaded.Error is not null || loaded.Definition is null)
        {
            return loaded.Error!;
        }

        if (!await ChannelsInWorkspaceAsync(db, loaded.Definition.WorkspaceId, model.ChannelIds, ct))
        {
            return Results.Forbid();
        }

        loaded.Definition.Name = name;
        loaded.Definition.DraftJson = model.ToJson();
        loaded.Definition.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(await MapAsync(db, loaded.Definition, null, ct));
    }

    private static async Task<IResult> PublishAsync(
        Guid workspaceId,
        Guid automationId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IAuditWriter audit,
        IClock clock,
        AutomationService automations,
        IOptionsMonitor<AutomationOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var loaded = await LoadAsync(workspaceId, automationId, http, db, tenantContext, clock, ct);
        if (loaded.Error is not null || loaded.Definition is null || loaded.Profile is null)
        {
            return loaded.Error!;
        }

        if (!AutomationPolicies.TryParse(loaded.Definition.DraftJson, out var model, out var error) || model is null)
        {
            return Bad(error);
        }

        var membership = await db.WorkspaceMembers.AsNoTracking().FirstOrDefaultAsync(
            row => row.WorkspaceId == loaded.Definition.WorkspaceId && row.UserId == loaded.Profile.Id, ct);
        if (membership is null)
        {
            return Results.Forbid();
        }

        var granted = AutomationPolicies.Intersect(RolePermissionCatalog.For(membership.Role), model.Capabilities());
        if (granted.Length != model.Capabilities().Length)
        {
            return Results.Json(new { error = "CapabilityDenied" }, statusCode: StatusCodes.Status403Forbidden);
        }

        var now = clock.UtcNow;
        var versionNumber = loaded.Definition.PublishedVersion + 1;
        db.AutomationVersions.Add(new AutomationVersion
        {
            Id = Guid.NewGuid(),
            TenantId = loaded.Definition.TenantId,
            AutomationId = loaded.Definition.Id,
            Version = versionNumber,
            DefinitionJson = model.ToJson(),
            CapabilitiesJson = JsonSerializer.Serialize(granted),
            PublishedBy = loaded.Profile.Id,
            PublishedAt = now,
        });
        loaded.Definition.PublishedVersion = versionNumber;
        loaded.Definition.Status = AutomationStatus.Published;
        loaded.Definition.TriggerType = model.Trigger.Type;
        loaded.Definition.UpdatedAt = now;
        if (model.Trigger.Type == AutomationPolicies.TriggerSchedule)
        {
            loaded.Definition.NextRunAt = now.AddMinutes(model.Trigger.IntervalMinutes);
        }

        string? token = null;
        if (model.Trigger.Type == AutomationPolicies.TriggerWebhook && loaded.Definition.WebhookTokenHash is null)
        {
            token = AutomationPolicies.CreateToken();
            loaded.Definition.WebhookTokenHash = AutomationPolicies.HashToken(token);
        }

        await automations.EnsureIdentityAsync(loaded.Definition, model, ct);
        Audit(audit, loaded.Definition, loaded.Profile.Id, AutomationPolicies.Published, now);
        VibeChatMetrics.AutomationsPublished.Add(1);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await MapAsync(db, loaded.Definition, token, ct));
    }

    private static async Task<IResult> EnableAsync(
        Guid workspaceId,
        Guid automationId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IAuditWriter audit,
        IClock clock,
        IOptionsMonitor<AutomationOptions> options,
        CancellationToken ct) =>
        await SetEnabledAsync(workspaceId, automationId, true, http, db, tenantContext, audit, clock, options, ct);

    private static async Task<IResult> DisableAsync(
        Guid workspaceId,
        Guid automationId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IAuditWriter audit,
        IClock clock,
        IOptionsMonitor<AutomationOptions> options,
        CancellationToken ct) =>
        await SetEnabledAsync(workspaceId, automationId, false, http, db, tenantContext, audit, clock, options, ct);

    private static async Task<IResult> DryRunAsync(
        Guid workspaceId,
        Guid automationId,
        AutomationSignalRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        AutomationService automations,
        IOptionsMonitor<AutomationOptions> options,
        CancellationToken ct) =>
        await RunAsync(workspaceId, automationId, request, AutomationRunMode.DryRun, http, db, tenantContext, clock, automations, options, ct);

    private static async Task<IResult> TriggerAsync(
        Guid workspaceId,
        Guid automationId,
        AutomationSignalRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        AutomationService automations,
        IOptionsMonitor<AutomationOptions> options,
        CancellationToken ct) =>
        await RunAsync(workspaceId, automationId, request, AutomationRunMode.Live, http, db, tenantContext, clock, automations, options, ct);

    private static async Task<IResult> ListRunsAsync(
        Guid workspaceId,
        Guid automationId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        IOptionsMonitor<AutomationOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var loaded = await LoadAsync(workspaceId, automationId, http, db, tenantContext, clock, ct);
        if (loaded.Error is not null || loaded.Definition is null)
        {
            return loaded.Error!;
        }

        var runs = await db.AutomationRuns
            .Where(row => row.AutomationId == loaded.Definition.Id)
            .OrderByDescending(row => row.CreatedAt)
            .Take(AutomationPolicies.MaxPageSize)
            .ToListAsync(ct);
        var items = new List<AutomationRunResponse>();
        foreach (var run in runs)
        {
            items.Add(await MapRunAsync(db, run, ct));
        }

        return Results.Ok(new AutomationRunListResponse(items.ToArray()));
    }

    private static async Task<IResult> GetRunAsync(
        Guid workspaceId,
        Guid automationId,
        Guid runId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        IOptionsMonitor<AutomationOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var loaded = await LoadAsync(workspaceId, automationId, http, db, tenantContext, clock, ct);
        if (loaded.Error is not null || loaded.Definition is null)
        {
            return loaded.Error!;
        }

        var run = await db.AutomationRuns.FirstOrDefaultAsync(row => row.Id == runId && row.AutomationId == loaded.Definition.Id, ct);
        return run is null ? Results.NotFound(new { error = "RunNotFound" }) : Results.Ok(await MapRunAsync(db, run, ct));
    }

    private static async Task<IResult> ReplayAsync(
        Guid workspaceId,
        Guid automationId,
        Guid runId,
        ReplayAutomationRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        AutomationService automations,
        IOptionsMonitor<AutomationOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var key = AutomationPolicies.Normalize(request.IdempotencyKey);
        if (!AutomationPolicies.IsValidIdempotencyKey(key))
        {
            return Bad("InvalidIdempotencyKey");
        }

        var loaded = await LoadAsync(workspaceId, automationId, http, db, tenantContext, clock, ct);
        if (loaded.Error is not null || loaded.Definition is null)
        {
            return loaded.Error!;
        }

        if (!loaded.Definition.Enabled)
        {
            return Bad("AutomationDisabled");
        }

        var source = await db.AutomationRuns.FirstOrDefaultAsync(row => row.Id == runId && row.AutomationId == loaded.Definition.Id, ct);
        if (source is null)
        {
            return Results.NotFound(new { error = "RunNotFound" });
        }

        var signal = new AutomationSignal(
            ReadJson(source.EventJson, "type") ?? loaded.Definition.TriggerType,
            ReadJson(source.EventJson, "eventName") ?? "fired",
            key,
            ReadJsonGuid(source.EventJson, "channelId"),
            ReadJsonGuid(source.EventJson, "sourceId"),
            ReadJsonGuid(source.EventJson, "formId"),
            ReadJsonGuid(source.EventJson, "taskId"),
            ReadJson(source.EventJson, "state"),
            source.Depth,
            source.Id);
        var run = await automations.TryEnqueueAsync(loaded.Definition, signal, AutomationRunMode.Replay, execute: true, ct);
        if (run is null)
        {
            return Bad("AutomationDisabled");
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(await MapRunAsync(db, run, ct));
    }

    private static async Task<IResult> HookAsync(
        Guid workspaceId,
        Guid automationId,
        AutomationSignalRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        AutomationService automations,
        IOptionsMonitor<AutomationOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        if (!http.Request.Headers.TryGetValue("X-Automation-Token", out var header))
        {
            return Results.NotFound(new { error = "AutomationDisabled" });
        }

        var token = header.ToString().Trim();
        if (!token.StartsWith(AutomationPolicies.TokenPrefix, StringComparison.Ordinal))
        {
            return Results.NotFound(new { error = "AutomationDisabled" });
        }

        tenantContext.SetJobRole(AutomationPolicies.JobRole);
        await RlsSession.EnsureAppliedAsync(db, tenantContext, ct);
        var definition = await db.AutomationDefinitions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(row => row.Id == automationId && row.WorkspaceId == new WorkspaceId(workspaceId), ct);
        tenantContext.SetJobRole(null);
        if (definition is null
            || definition.WebhookTokenHash is null
            || !string.Equals(definition.WebhookTokenHash, AutomationPolicies.HashToken(token), StringComparison.Ordinal)
            || !definition.Enabled)
        {
            return Results.NotFound(new { error = "AutomationDisabled" });
        }

        tenantContext.SetTenant(definition.TenantId);
        await RlsSession.EnsureAppliedAsync(db, tenantContext, ct);
        var eventId = AutomationPolicies.Normalize(request.EventId);
        if (!AutomationPolicies.IsValidIdempotencyKey(eventId))
        {
            return Bad("InvalidIdempotencyKey");
        }

        var signal = new AutomationSignal(
            AutomationPolicies.TriggerWebhook,
            "fired",
            eventId,
            request.ChannelId,
            request.SourceId,
            request.FormId,
            request.TaskId,
            AutomationPolicies.Normalize(request.State),
            0,
            null);
        var run = await automations.TryEnqueueAsync(definition, signal, AutomationRunMode.Live, execute: true, ct);
        return run is null
            ? Bad("AutomationDisabled")
            : Results.Ok(await MapRunAsync(db, run, ct));
    }

    private static async Task<IResult> RunAsync(
        Guid workspaceId,
        Guid automationId,
        AutomationSignalRequest request,
        AutomationRunMode mode,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        AutomationService automations,
        IOptionsMonitor<AutomationOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var loaded = await LoadAsync(workspaceId, automationId, http, db, tenantContext, clock, ct);
        if (loaded.Error is not null || loaded.Definition is null)
        {
            return loaded.Error!;
        }

        if (loaded.Definition.PublishedVersion == 0)
        {
            return Bad("NotPublished");
        }

        if (mode != AutomationRunMode.DryRun && !loaded.Definition.Enabled)
        {
            return Bad("AutomationDisabled");
        }

        var eventId = AutomationPolicies.Normalize(request.EventId);
        if (!AutomationPolicies.IsValidIdempotencyKey(eventId))
        {
            return Bad("InvalidIdempotencyKey");
        }

        if (ContainsSecret(request))
        {
            return Bad("SecretValueNotAllowed");
        }

        var signal = new AutomationSignal(
            AutomationPolicies.Normalize(request.Type).ToLowerInvariant(),
            AutomationPolicies.Normalize(request.EventName).ToLowerInvariant(),
            eventId,
            request.ChannelId,
            request.SourceId,
            request.FormId,
            request.TaskId,
            AutomationPolicies.Normalize(request.State).ToLowerInvariant(),
            0,
            null);
        var run = await automations.TryEnqueueAsync(loaded.Definition, signal, mode, execute: true, ct);
        if (run is null)
        {
            return Bad("AutomationDisabled");
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(await MapRunAsync(db, run, ct));
    }

    private static async Task<IResult> SetEnabledAsync(
        Guid workspaceId,
        Guid automationId,
        bool enabled,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IAuditWriter audit,
        IClock clock,
        IOptionsMonitor<AutomationOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var loaded = await LoadAsync(workspaceId, automationId, http, db, tenantContext, clock, ct);
        if (loaded.Error is not null || loaded.Definition is null || loaded.Profile is null)
        {
            return loaded.Error!;
        }

        if (loaded.Definition.PublishedVersion == 0)
        {
            return Bad("NotPublished");
        }

        var now = clock.UtcNow;
        loaded.Definition.Enabled = enabled;
        loaded.Definition.UpdatedAt = now;
        if (enabled)
        {
            loaded.Definition.ConsecutiveFailures = 0;
            loaded.Definition.CircuitOpenUntil = null;
        }

        Audit(audit, loaded.Definition, loaded.Profile.Id, enabled ? AutomationPolicies.EnabledEvent : AutomationPolicies.DisabledEvent, now);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await MapAsync(db, loaded.Definition, null, ct));
    }

    private static async Task<bool> ChannelsInWorkspaceAsync(VibeChatDbContext db, WorkspaceId workspaceId, Guid[] channelIds, CancellationToken ct)
    {
        if (channelIds.Length == 0)
        {
            return true;
        }

        var ids = channelIds.Select(id => new ChannelId(id)).ToArray();
        var count = await db.Channels.CountAsync(row => row.WorkspaceId == workspaceId && ids.Contains(row.Id), ct);
        return count == ids.Length;
    }

    private static async Task<AutomationResponse> MapAsync(VibeChatDbContext db, AutomationDefinition definition, string? token, CancellationToken ct)
    {
        string? identityName = null;
        if (definition.IdentityUserId is not null)
        {
            identityName = await db.UserProfiles.AsNoTracking()
                .Where(row => row.Id == definition.IdentityUserId)
                .Select(row => row.DisplayName)
                .FirstOrDefaultAsync(ct);
        }

        var capabilities = Array.Empty<string>();
        var channels = Array.Empty<Guid>();
        if (definition.PublishedVersion > 0)
        {
            var version = await db.AutomationVersions.AsNoTracking().FirstOrDefaultAsync(
                row => row.AutomationId == definition.Id && row.Version == definition.PublishedVersion, ct);
            if (version is not null)
            {
                capabilities = JsonSerializer.Deserialize<string[]>(version.CapabilitiesJson) ?? [];
                if (AutomationPolicies.TryParse(version.DefinitionJson, out var model, out _) && model is not null)
                {
                    channels = model.ChannelIds;
                }
            }
        }
        else if (AutomationPolicies.TryParse(definition.DraftJson, out var draft, out _) && draft is not null)
        {
            capabilities = draft.Capabilities();
            channels = draft.ChannelIds;
        }

        return new AutomationResponse(
            definition.Id,
            definition.Name,
            AutomationPolicies.StatusToken(definition.Status),
            definition.Enabled,
            definition.PublishedVersion,
            definition.TriggerType,
            definition.IdentityUserId?.Value,
            identityName,
            capabilities,
            channels,
            definition.CircuitOpenUntil,
            token,
            definition.UpdatedAt);
    }

    private static async Task<AutomationRunResponse> MapRunAsync(VibeChatDbContext db, AutomationRun run, CancellationToken ct)
    {
        var steps = await db.AutomationSteps.AsNoTracking()
            .Where(row => row.RunId == run.Id)
            .OrderBy(row => row.ActionIndex)
            .ToListAsync(ct);
        return new AutomationRunResponse(
            run.Id,
            run.Version,
            AutomationPolicies.ModeToken(run.Mode),
            AutomationPolicies.RunToken(run.Status),
            run.Depth,
            run.ErrorCode,
            run.EventJson,
            steps.Select(step => new AutomationStepResponse(
                step.ActionIndex,
                step.ActionType,
                AutomationPolicies.StepToken(step.Status),
                step.ErrorCode,
                step.EffectId)).ToArray());
    }

    private static async Task<Loaded> LoadAsync(
        Guid workspaceId,
        Guid automationId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        CancellationToken ct)
    {
        var gate = await OpenAsync(workspaceId, http, db, tenantContext, clock, ct);
        if (gate.Error is not null || gate.Workspace is null)
        {
            return new Loaded(null, gate.Profile, gate.Error);
        }

        var definition = await db.AutomationDefinitions.FirstOrDefaultAsync(
            row => row.Id == automationId && row.WorkspaceId == gate.Workspace.Id, ct);
        return definition is null
            ? new Loaded(null, gate.Profile, Results.NotFound(new { error = "AutomationNotFound" }))
            : new Loaded(definition, gate.Profile, null);
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

    private static bool ContainsSecret(AutomationSignalRequest request)
    {
        var raw = JsonSerializer.Serialize(request);
        return raw.Contains(AutomationPolicies.TokenPrefix, StringComparison.Ordinal)
            || raw.Contains("vc_int_", StringComparison.Ordinal);
    }

    private static string? ReadJson(string json, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Guid? ReadJsonGuid(string json, string name)
    {
        var text = ReadJson(json, name);
        return Guid.TryParse(text, out var id) ? id : null;
    }

    private static void Audit(IAuditWriter audit, AutomationDefinition definition, UserId actor, string action, DateTimeOffset at)
    {
        audit.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = definition.TenantId,
            ActorUserId = actor,
            Action = action,
            EntityType = "Automation",
            EntityId = definition.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new
            {
                version = definition.PublishedVersion,
                enabled = definition.Enabled,
                trigger = definition.TriggerType,
            }),
            OccurredAt = at,
        });
    }

    private static IResult Bad(string code) => Results.BadRequest(new { error = code });

    private static IResult Disabled() => Results.NotFound(new { error = "AutomationDisabled" });

    private sealed record WorkspaceGate(Workspace? Workspace, UserProfile? Profile, IResult? Error);

    private sealed record Loaded(AutomationDefinition? Definition, UserProfile? Profile, IResult? Error);
}
