using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.Api.Contracts;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Files;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.Infrastructure.Compliance;
using VibeChat.Infrastructure.Messaging;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Api.Endpoints;

internal static class FormEndpoints
{
    internal static void MapForms(this RouteGroupBuilder v1)
    {
        v1.MapPost("/workspaces/{workspaceId:guid}/forms", CreateAsync)
            .RequirePermission(Permissions.Forms.Create);
        v1.MapGet("/workspaces/{workspaceId:guid}/forms", ListAsync)
            .RequirePermission(Permissions.Forms.Submit);
        v1.MapGet("/workspaces/{workspaceId:guid}/forms/{formId:guid}", GetAsync)
            .RequirePermission(Permissions.Forms.Submit);
        v1.MapPatch("/workspaces/{workspaceId:guid}/forms/{formId:guid}", UpdateAsync)
            .RequirePermission(Permissions.Forms.Create);
        v1.MapPost("/workspaces/{workspaceId:guid}/forms/{formId:guid}/publish", PublishAsync)
            .RequirePermission(Permissions.Forms.Publish);
        v1.MapPost("/workspaces/{workspaceId:guid}/forms/{formId:guid}/archive", ArchiveAsync)
            .RequirePermission(Permissions.Forms.Publish);
        v1.MapPost("/workspaces/{workspaceId:guid}/forms/{formId:guid}/submissions", SubmitAsync)
            .RequirePermission(Permissions.Forms.Submit);
        v1.MapGet("/workspaces/{workspaceId:guid}/forms/{formId:guid}/submissions", ListSubmissionsAsync)
            .RequirePermission(Permissions.Forms.Submit);
        v1.MapGet("/workspaces/{workspaceId:guid}/forms/{formId:guid}/submissions/{submissionId:guid}", GetSubmissionAsync)
            .RequirePermission(Permissions.Forms.Submit);
        v1.MapPost("/workspaces/{workspaceId:guid}/forms/{formId:guid}/submissions/{submissionId:guid}/decisions", DecideAsync)
            .RequirePermission(Permissions.Forms.Review);
        v1.MapGet("/workspaces/{workspaceId:guid}/forms/{formId:guid}/export", ExportAsync)
            .RequirePermission(Permissions.Forms.Review);
    }

    private static async Task<IResult> CreateAsync(
        Guid workspaceId,
        CreateFormRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IClock clock,
        IOptionsMonitor<FormOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var key = FormPolicies.Normalize(request.IdempotencyKey);
        var title = FormPolicies.Normalize(request.Title);
        if (!FormPolicies.IsValidIdempotencyKey(key))
        {
            return Bad("InvalidIdempotencyKey");
        }

        if (!FormPolicies.IsValidTitle(title))
        {
            return Bad("InvalidTitle");
        }

        if (!FormPolicies.TryParseSchema(request.Schema, out var schema, out var error))
        {
            return Bad(error);
        }

        var gate = await OpenWorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate.Error is not null || gate.Profile is null || gate.Workspace is null)
        {
            return gate.Error ?? Results.Forbid();
        }

        var profile = gate.Profile;
        var workspace = gate.Workspace;
        var existing = await db.FormDefinitions.FirstOrDefaultAsync(
            row => row.CreatedBy == profile.Id && row.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            return await DetailOrMissing(db, existing, profile.Id, ct);
        }

        var channel = await db.Channels.FirstOrDefaultAsync(
            row => row.Id == new ChannelId(request.ChannelId) && row.WorkspaceId == workspace.Id, ct);
        if (channel is null || !await CanReadChannelAsync(db, channel, profile.Id, ct))
        {
            return Results.Forbid();
        }

        if (!await ReviewersCanReadAsync(db, channel, schema, ct))
        {
            return Bad("ReviewerNotMember");
        }

        var live = await db.FormDefinitions.CountAsync(
            row => row.ChannelId == channel.Id && row.Status != FormStatus.Archived, ct);
        if (live >= FormPolicies.MaxFormsPerChannel)
        {
            return Bad("FormLimitReached");
        }

        var now = clock.UtcNow;
        var form = new FormDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            ChannelId = channel.Id,
            Title = title,
            Status = FormStatus.Draft,
            PublishedVersion = 0,
            DraftSchemaJson = schema.ToJson(),
            CreatedBy = profile.Id,
            IdempotencyKey = key,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.FormDefinitions.Add(form);
        AddAudit(audit, form, profile.Id, "form.drafted", now);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            var raced = await db.FormDefinitions.FirstOrDefaultAsync(
                row => row.CreatedBy == profile.Id && row.IdempotencyKey == key, ct);
            if (raced is null)
            {
                throw;
            }

            return await DetailOrMissing(db, raced, profile.Id, ct);
        }

        return Results.Ok(await ToDetailAsync(db, form, profile.Id, ct));
    }

    private static async Task<IResult> ListAsync(
        Guid workspaceId,
        Guid? channelId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        IOptionsMonitor<FormOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        if (channelId is not Guid rawChannel)
        {
            return Bad("InvalidFormFilter");
        }

        var gate = await OpenWorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate.Error is not null || gate.Profile is null || gate.Workspace is null)
        {
            return gate.Error ?? Results.Forbid();
        }

        var profile = gate.Profile;
        var workspace = gate.Workspace;
        var channel = await db.Channels.FirstOrDefaultAsync(
            row => row.Id == new ChannelId(rawChannel) && row.WorkspaceId == workspace.Id, ct);
        if (channel is null || !await CanReadChannelAsync(db, channel, profile.Id, ct))
        {
            return Results.Forbid();
        }

        var rows = await db.FormDefinitions.AsNoTracking()
            .Where(row => row.ChannelId == channel.Id
                && (row.Status != FormStatus.Draft || row.CreatedBy == profile.Id))
            .OrderByDescending(row => row.UpdatedAt)
            .Take(FormPolicies.MaxPageSize)
            .ToListAsync(ct);
        var visible = rows
            .Select(row => new FormListItemResponse(
                row.Id,
                row.ChannelId.Value,
                row.Title,
                FormPolicies.StatusToken(row.Status),
                row.PublishedVersion,
                row.UpdatedAt))
            .ToArray();
        return Results.Ok(new FormListResponse(visible));
    }

    private static async Task<IResult> GetAsync(
        Guid workspaceId,
        Guid formId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        IOptionsMonitor<FormOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var opened = await OpenFormAsync(workspaceId, formId, http, db, tenant, clock, ct);
        return opened.Error ?? Results.Ok(await ToDetailAsync(db, opened.Form!, opened.ProfileId, ct));
    }

    private static async Task<IResult> UpdateAsync(
        Guid workspaceId,
        Guid formId,
        UpdateFormRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        IOptionsMonitor<FormOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var title = FormPolicies.Normalize(request.Title);
        if (!FormPolicies.IsValidTitle(title))
        {
            return Bad("InvalidTitle");
        }

        if (!FormPolicies.TryParseSchema(request.Schema, out var schema, out var error))
        {
            return Bad(error);
        }

        var opened = await OpenFormAsync(workspaceId, formId, http, db, tenant, clock, ct, tracked: true);
        if (opened.Error is not null)
        {
            return opened.Error;
        }

        var form = opened.Form!;
        if (form.CreatedBy != opened.ProfileId || form.Status == FormStatus.Archived)
        {
            return Results.Forbid();
        }

        var channel = await db.Channels.FirstAsync(row => row.Id == form.ChannelId, ct);
        if (!await ReviewersCanReadAsync(db, channel, schema, ct))
        {
            return Bad("ReviewerNotMember");
        }

        form.Title = title;
        form.DraftSchemaJson = schema.ToJson();
        form.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ToDetailAsync(db, form, opened.ProfileId, ct));
    }

    private static async Task<IResult> PublishAsync(
        Guid workspaceId,
        Guid formId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOutboxWriter outbox,
        IAuditWriter audit,
        IClock clock,
        IOptionsMonitor<FormOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var opened = await OpenFormAsync(workspaceId, formId, http, db, tenant, clock, ct, tracked: true);
        if (opened.Error is not null)
        {
            return opened.Error;
        }

        var form = opened.Form!;
        if (form.Status == FormStatus.Archived)
        {
            return Bad("FormArchived");
        }

        if (form.CreatedBy != opened.ProfileId)
        {
            return Results.Forbid();
        }

        if (!FormPolicies.TryReadSchema(form.DraftSchemaJson, out var schema))
        {
            return Bad("InvalidFormSchema");
        }

        var channel = await db.Channels.FirstAsync(row => row.Id == form.ChannelId, ct);
        if (!await ReviewersCanReadAsync(db, channel, schema, ct))
        {
            return Bad("ReviewerNotMember");
        }

        var current = form.PublishedVersion == 0
            ? null
            : await db.FormSchemaVersions.AsNoTracking()
                .Where(row => row.FormId == form.Id && row.Version == form.PublishedVersion)
                .Select(row => row.SchemaJson)
                .FirstOrDefaultAsync(ct);
        var canonical = schema.ToJson();
        if (!string.Equals(current, canonical, StringComparison.Ordinal))
        {
            var version = form.PublishedVersion + 1;
            db.FormSchemaVersions.Add(new FormSchemaVersion
            {
                Id = Guid.NewGuid(),
                TenantId = form.TenantId,
                FormId = form.Id,
                Version = version,
                SchemaJson = canonical,
                PublishedBy = opened.ProfileId,
                PublishedAt = clock.UtcNow,
            });
            form.PublishedVersion = version;
        }

        var now = clock.UtcNow;
        form.Status = FormStatus.Published;
        form.UpdatedAt = now;
        AddAudit(audit, form, opened.ProfileId, FormPolicies.Published, now);
        AddOutbox(outbox, form, FormPolicies.Published, now, null);
        VibeChatMetrics.FormsPublished.Add(1);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ToDetailAsync(db, form, opened.ProfileId, ct));
    }

    private static async Task<IResult> ArchiveAsync(
        Guid workspaceId,
        Guid formId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IClock clock,
        IOptionsMonitor<FormOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var opened = await OpenFormAsync(workspaceId, formId, http, db, tenant, clock, ct, tracked: true);
        if (opened.Error is not null)
        {
            return opened.Error;
        }

        var form = opened.Form!;
        if (form.CreatedBy != opened.ProfileId)
        {
            return Results.Forbid();
        }

        if (form.Status != FormStatus.Archived)
        {
            form.Status = FormStatus.Archived;
            form.UpdatedAt = clock.UtcNow;
            AddAudit(audit, form, opened.ProfileId, "form.archived", form.UpdatedAt);
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(await ToDetailAsync(db, form, opened.ProfileId, ct));
    }

    private static async Task<IResult> SubmitAsync(
        Guid workspaceId,
        Guid formId,
        SubmitFormRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOutboxWriter outbox,
        IAuditWriter audit,
        IClock clock,
        IOptionsMonitor<FormOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var key = FormPolicies.Normalize(request.IdempotencyKey);
        if (!FormPolicies.IsValidIdempotencyKey(key))
        {
            return Bad("InvalidIdempotencyKey");
        }

        var opened = await OpenFormAsync(workspaceId, formId, http, db, tenant, clock, ct);
        if (opened.Error is not null)
        {
            return opened.Error;
        }

        var form = opened.Form!;
        if (form.Status == FormStatus.Archived)
        {
            return Bad("FormArchived");
        }

        if (form.Status != FormStatus.Published || form.PublishedVersion < 1)
        {
            return Bad("FormNotPublished");
        }

        var replay = await db.FormSubmissions.FirstOrDefaultAsync(
            row => row.SubmittedBy == opened.ProfileId && row.IdempotencyKey == key, ct);
        if (replay is not null)
        {
            return await SubmissionOrMissing(db, form, replay, opened.ProfileId, ct);
        }

        var published = await db.FormSchemaVersions.AsNoTracking()
            .FirstOrDefaultAsync(row => row.FormId == form.Id && row.Version == form.PublishedVersion, ct);
        if (published is null || !FormPolicies.TryReadSchema(published.SchemaJson, out var schema))
        {
            return Bad("InvalidFormSchema");
        }

        if (!FormPolicies.TryBindAnswers(schema, request.Answers, out var answers, out var error))
        {
            return Bad(error);
        }

        if (!await AnswersAllowedAsync(db, form, opened.ProfileId, answers, ct))
        {
            return Bad("InvalidAnswer");
        }

        var now = clock.UtcNow;
        var retention = await db.MessageRetentionSettings.AsNoTracking()
            .Where(row => row.TenantId == form.TenantId && row.Enabled)
            .Select(row => (int?)row.RetentionDays)
            .FirstOrDefaultAsync(ct);
        var submission = new FormSubmission
        {
            Id = Guid.NewGuid(),
            TenantId = form.TenantId,
            FormId = form.Id,
            SchemaVersion = form.PublishedVersion,
            AnswersJson = answers,
            State = FormSubmissionState.Pending,
            SubmittedBy = opened.ProfileId,
            IdempotencyKey = key,
            TitleSnapshot = form.Title,
            SubmittedAt = now,
            RetainUntil = FormPolicies.RetainUntil(now, retention),
        };
        db.FormSubmissions.Add(submission);
        AddOutbox(outbox, form, FormPolicies.Submitted, now, submission.Id);
        AddSubmissionAudit(audit, submission, opened.ProfileId, FormPolicies.Submitted, now);
        VibeChatMetrics.FormsSubmitted.Add(1);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            var raced = await db.FormSubmissions.FirstOrDefaultAsync(
                row => row.SubmittedBy == opened.ProfileId && row.IdempotencyKey == key, ct);
            if (raced is null)
            {
                throw;
            }

            return await SubmissionOrMissing(db, form, raced, opened.ProfileId, ct);
        }

        return Results.Ok(await ToSubmissionAsync(db, form, submission, opened.ProfileId, ct));
    }

    private static async Task<IResult> ListSubmissionsAsync(
        Guid workspaceId,
        Guid formId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        IOptionsMonitor<FormOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var opened = await OpenFormAsync(workspaceId, formId, http, db, tenant, clock, ct);
        if (opened.Error is not null)
        {
            return opened.Error;
        }

        var rows = await db.FormSubmissions.AsNoTracking()
            .Where(row => row.FormId == formId)
            .OrderByDescending(row => row.SubmittedAt)
            .Take(FormPolicies.MaxPageSize)
            .ToListAsync(ct);
        var items = new List<FormSubmissionResponse>();
        foreach (var row in rows)
        {
            if (await CanSeeSubmissionAsync(db, opened.Form!, row, opened.ProfileId, ct))
            {
                items.Add(await ToSubmissionAsync(db, opened.Form!, row, opened.ProfileId, ct));
            }
        }

        return Results.Ok(new FormSubmissionListResponse(items.ToArray()));
    }

    private static async Task<IResult> GetSubmissionAsync(
        Guid workspaceId,
        Guid formId,
        Guid submissionId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        IOptionsMonitor<FormOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var opened = await OpenSubmissionAsync(workspaceId, formId, submissionId, http, db, tenant, clock, ct);
        if (opened.Error is not null)
        {
            return opened.Error;
        }

        return Results.Ok(await ToSubmissionAsync(db, opened.Form!, opened.Submission!, opened.ProfileId, ct));
    }

    private static async Task<IResult> DecideAsync(
        Guid workspaceId,
        Guid formId,
        Guid submissionId,
        DecideFormRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOutboxWriter outbox,
        IAuditWriter audit,
        IClock clock,
        IMessageWriter messages,
        IOptionsMonitor<FormOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var key = FormPolicies.Normalize(request.IdempotencyKey);
        if (!FormPolicies.IsValidIdempotencyKey(key) || !FormPolicies.TryParseDecision(request.Decision, out var decision))
        {
            return Bad(FormPolicies.IsValidIdempotencyKey(key) ? "InvalidDecision" : "InvalidIdempotencyKey");
        }

        var opened = await OpenSubmissionAsync(workspaceId, formId, submissionId, http, db, tenant, clock, ct, tracked: true);
        if (opened.Error is not null)
        {
            return opened.Error;
        }

        if (request.ActorUserId is Guid claimed && claimed != opened.ProfileId.Value)
        {
            return Bad("ActorNotAllowed");
        }

        var submission = opened.Submission!;
        var form = opened.Form!;
        var replay = await db.FormDecisions.FirstOrDefaultAsync(
            row => row.ActorUserId == opened.ProfileId && row.IdempotencyKey == key, ct);
        if (replay is not null)
        {
            if (replay.SubmissionId != submission.Id)
            {
                return Bad("InvalidIdempotencyKey");
            }

            await EnsureResultAsync(db, messages, form, submission, clock, ct);
            return Results.Ok(await ToSubmissionAsync(db, form, submission, opened.ProfileId, ct));
        }

        var prior = await db.FormDecisions.Where(row => row.SubmissionId == submission.Id).ToListAsync(ct);
        if (prior.Any(row => row.ActorUserId == opened.ProfileId))
        {
            return Conflict("AlreadyDecided");
        }

        if (submission.State != FormSubmissionState.Pending)
        {
            return Conflict("FormClosed");
        }

        var published = await db.FormSchemaVersions.AsNoTracking()
            .FirstOrDefaultAsync(row => row.FormId == form.Id && row.Version == submission.SchemaVersion, ct);
        if (published is null || !FormPolicies.TryReadSchema(published.SchemaJson, out var schema) || !schema.IsReviewer(opened.ProfileId.Value))
        {
            return Results.Forbid();
        }

        var channel = await db.Channels.FirstOrDefaultAsync(row => row.Id == form.ChannelId, ct);
        if (channel is null || !await CanReadChannelAsync(db, channel, opened.ProfileId, ct))
        {
            return Results.Forbid();
        }

        var now = clock.UtcNow;
        var rowDecision = new FormDecision
        {
            Id = Guid.NewGuid(),
            TenantId = form.TenantId,
            SubmissionId = submission.Id,
            Version = prior.Count + 1,
            ActorUserId = opened.ProfileId,
            Decision = decision,
            IdempotencyKey = key,
            CreatedAt = now,
        };
        db.FormDecisions.Add(rowDecision);
        var votes = prior.Select(item => (item.ActorUserId.Value, item.Decision)).Append((opened.ProfileId.Value, decision)).ToArray();
        submission.State = FormPolicies.NextState(schema, votes);
        var action = submission.State == FormSubmissionState.Rejected ? FormPolicies.Rejected : FormPolicies.Approved;
        if (submission.State == FormSubmissionState.Pending)
        {
            action = "form.decision";
        }

        AddSubmissionAudit(audit, submission, opened.ProfileId, action, now);
        if (submission.State is FormSubmissionState.Approved or FormSubmissionState.Rejected)
        {
            AddOutbox(outbox, form, submission.State == FormSubmissionState.Approved ? FormPolicies.Approved : FormPolicies.Rejected, now, submission.Id);
            VibeChatMetrics.FormsDecided.Add(1);
        }

        if (submission.State == FormSubmissionState.Approved && schema.OnApproved != FormPolicies.ResultNone)
        {
            await EnsureResultAsync(db, messages, form, submission, clock, ct);
        }
        else
        {
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(await ToSubmissionAsync(db, form, submission, opened.ProfileId, ct));
    }

    private static async Task<IResult> ExportAsync(
        Guid workspaceId,
        Guid formId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        DlpService dlp,
        IClock clock,
        IOptionsMonitor<FormOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var opened = await OpenFormAsync(workspaceId, formId, http, db, tenant, clock, ct);
        if (opened.Error is not null)
        {
            return opened.Error;
        }

        var denied = await DlpGuard.BlockAsync(
            dlp,
            opened.Form!.TenantId,
            opened.Form.WorkspaceId,
            null,
            opened.Form.ChannelId.Value,
            null,
            DlpActions.Export,
            null,
            opened.Form.Id,
            ct);
        if (denied is not null)
        {
            return denied;
        }

        var rows = await db.FormSubmissions.AsNoTracking()
            .Where(row => row.FormId == formId)
            .OrderBy(row => row.SubmittedAt)
            .Take(FormPolicies.MaxPageSize)
            .ToListAsync(ct);
        var items = new List<FormSubmissionResponse>();
        foreach (var row in rows)
        {
            if (await CanSeeSubmissionAsync(db, opened.Form!, row, opened.ProfileId, ct))
            {
                items.Add(await ToSubmissionAsync(db, opened.Form!, row, opened.ProfileId, ct));
            }
        }

        audit.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = opened.Form!.TenantId,
            ActorUserId = opened.ProfileId,
            Action = FormPolicies.Exported,
            EntityType = "Form",
            EntityId = formId.ToString(),
            MetadataJson = $"{{\"count\":{items.Count}}}",
            OccurredAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new FormExportResponse(formId, items.ToArray()));
    }

    private static async Task EnsureResultAsync(
        VibeChatDbContext db,
        IMessageWriter messages,
        FormDefinition form,
        FormSubmission submission,
        IClock clock,
        CancellationToken ct)
    {
        if (!FormPolicies.TryReadSchema(await SchemaJsonAsync(db, submission, ct), out var schema)
            || schema.OnApproved == FormPolicies.ResultNone
            || submission.State != FormSubmissionState.Approved)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        if (submission.ResultMessageId is null)
        {
            var sent = await messages.SendAsync(new SendMessageCommand(
                form.TenantId,
                form.CreatedBy,
                form.ChannelId,
                new MessageId(Guid.NewGuid()),
                FormPolicies.MessageIdempotencyKey(submission.Id),
                submission.TitleSnapshot,
                null,
                null), ct);
            submission.ResultMessageId = sent.MessageId.Value;
        }

        if (schema.OnApproved == FormPolicies.ResultTask && submission.ResultTaskId is null)
        {
            var taskKey = FormPolicies.TaskIdempotencyKey(submission.Id);
            var existing = await db.MessageTasks.FirstOrDefaultAsync(
                row => row.CreatedBy == form.CreatedBy && row.IdempotencyKey == taskKey, ct);
            if (existing is null)
            {
                var now = clock.UtcNow;
                var task = new MessageTask
                {
                    Id = Guid.NewGuid(),
                    TenantId = form.TenantId,
                    WorkspaceId = form.WorkspaceId,
                    ChannelId = form.ChannelId,
                    Title = submission.TitleSnapshot,
                    Priority = TaskPriority.Normal,
                    State = TaskState.Open,
                    Version = 1,
                    SourceMessageId = new MessageId(submission.ResultMessageId.Value),
                    SourceVersion = 0,
                    ExcerptHash = TaskPolicies.ExcerptHash(submission.TitleSnapshot),
                    Method = FormPolicies.ResultMethod,
                    CreatedBy = form.CreatedBy,
                    AuthzSnapshot = "{}",
                    Visibility = CommitmentVisibility.Channel,
                    IdempotencyKey = taskKey,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.MessageTasks.Add(task);
                db.MessageTaskAssignees.Add(new MessageTaskAssignee
                {
                    Id = Guid.NewGuid(),
                    TenantId = form.TenantId,
                    TaskId = task.Id,
                    UserId = form.CreatedBy,
                    AssignedAt = now,
                });
                submission.ResultTaskId = task.Id;
            }
            else
            {
                submission.ResultTaskId = existing.Id;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<string> SchemaJsonAsync(VibeChatDbContext db, FormSubmission submission, CancellationToken ct) =>
        await db.FormSchemaVersions.AsNoTracking()
            .Where(row => row.FormId == submission.FormId && row.Version == submission.SchemaVersion)
            .Select(row => row.SchemaJson)
            .FirstOrDefaultAsync(ct) ?? "{}";

    private static async Task<bool> AnswersAllowedAsync(
        VibeChatDbContext db,
        FormDefinition form,
        UserId submitter,
        string answersJson,
        CancellationToken ct)
    {
        using var document = JsonDocument.Parse(answersJson);
        if (!FormPolicies.TryReadSchema(await CurrentSchemaJson(db, form, ct), out var schema))
        {
            return false;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            var field = schema.Fields.FirstOrDefault(item => item.Key == property.Name);
            if (field is null)
            {
                return false;
            }

            if (field.Type == "user")
            {
                var userId = new UserId(property.Value.GetGuid());
                var member = await db.WorkspaceMembers.AnyAsync(
                    row => row.WorkspaceId == form.WorkspaceId && row.UserId == userId, ct);
                if (!member)
                {
                    return false;
                }
            }

            if (field.Type == "attachment")
            {
                var attachmentId = property.Value.GetGuid();
                var allowed = await db.Attachments.AnyAsync(
                    row => row.Id == attachmentId
                        && row.TenantId == form.TenantId
                        && row.ChannelId == form.ChannelId
                        && row.Status == AttachmentStatus.Ready, ct);
                if (!allowed)
                {
                    return false;
                }
            }
        }

        _ = submitter;
        return true;
    }

    private static async Task<string> CurrentSchemaJson(VibeChatDbContext db, FormDefinition form, CancellationToken ct) =>
        await db.FormSchemaVersions.AsNoTracking()
            .Where(row => row.FormId == form.Id && row.Version == form.PublishedVersion)
            .Select(row => row.SchemaJson)
            .FirstOrDefaultAsync(ct) ?? form.DraftSchemaJson;

    private static async Task<FormResponse> ToDetailAsync(VibeChatDbContext db, FormDefinition form, UserId viewer, CancellationToken ct)
    {
        FormSchema? published = null;
        if (form.PublishedVersion > 0)
        {
            var json = await db.FormSchemaVersions.AsNoTracking()
                .Where(row => row.FormId == form.Id && row.Version == form.PublishedVersion)
                .Select(row => row.SchemaJson)
                .FirstOrDefaultAsync(ct);
            if (json is not null && FormPolicies.TryReadSchema(json, out var parsed))
            {
                published = parsed;
            }
        }

        var creator = form.CreatedBy == viewer;
        var publicSchema = published is null
            ? null
            : JsonNode.Parse((creator ? published : published.WithoutHiddenFields()).ToJson());
        return new FormResponse(
            form.Id,
            form.ChannelId.Value,
            form.Title,
            FormPolicies.StatusToken(form.Status),
            form.PublishedVersion,
            creator && form.Status != FormStatus.Archived,
            form.Status == FormStatus.Published,
            published?.IsReviewer(viewer.Value) == true,
            creator ? JsonNode.Parse(form.DraftSchemaJson) : null,
            publicSchema,
            form.UpdatedAt);
    }

    private static async Task<FormSubmissionResponse> ToSubmissionAsync(
        VibeChatDbContext db,
        FormDefinition form,
        FormSubmission submission,
        UserId viewer,
        CancellationToken ct)
    {
        var decisions = await db.FormDecisions.AsNoTracking()
            .Where(row => row.SubmissionId == submission.Id)
            .OrderBy(row => row.Version)
            .ToListAsync(ct);
        var schemaJson = await SchemaJsonAsync(db, submission, ct);
        var canDecide = submission.State == FormSubmissionState.Pending
            && FormPolicies.TryReadSchema(schemaJson, out var schema)
            && schema.IsReviewer(viewer.Value)
            && decisions.All(row => row.ActorUserId != viewer);
        return new FormSubmissionResponse(
            submission.Id,
            submission.FormId,
            submission.SchemaVersion,
            FormPolicies.StateToken(submission.State),
            submission.AnswersPurged,
            submission.AnswersPurged ? null : JsonNode.Parse(submission.AnswersJson),
            submission.SubmittedBy.Value,
            submission.SubmittedAt,
            canDecide,
            submission.ResultMessageId,
            submission.ResultTaskId,
            decisions.Select(row => new FormDecisionResponse(row.Version, row.ActorUserId.Value, row.Decision, row.CreatedAt)).ToArray());
    }

    private static async Task<bool> CanSeeSubmissionAsync(
        VibeChatDbContext db,
        FormDefinition form,
        FormSubmission submission,
        UserId viewer,
        CancellationToken ct)
    {
        if (submission.SubmittedBy == viewer || form.CreatedBy == viewer)
        {
            return true;
        }

        var json = await SchemaJsonAsync(db, submission, ct);
        return FormPolicies.TryReadSchema(json, out var schema) && schema.IsReviewer(viewer.Value);
    }

    private static async Task<IResult> DetailOrMissing(VibeChatDbContext db, FormDefinition form, UserId viewer, CancellationToken ct)
    {
        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(row => row.Id == form.ChannelId, ct);
        if (channel is null || !await CanReadChannelAsync(db, channel, viewer, ct))
        {
            return Results.Forbid();
        }

        return Results.Ok(await ToDetailAsync(db, form, viewer, ct));
    }

    private static async Task<IResult> SubmissionOrMissing(
        VibeChatDbContext db,
        FormDefinition form,
        FormSubmission submission,
        UserId viewer,
        CancellationToken ct)
    {
        if (submission.FormId != form.Id || !await CanSeeSubmissionAsync(db, form, submission, viewer, ct))
        {
            return Results.Forbid();
        }

        return Results.Ok(await ToSubmissionAsync(db, form, submission, viewer, ct));
    }

    private static async Task<FormGate> OpenFormAsync(
        Guid workspaceId,
        Guid formId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct,
        bool tracked = false)
    {
        var gate = await OpenWorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate.Error is not null || gate.Profile is null || gate.Workspace is null)
        {
            return new FormGate(null, null, gate.Profile?.Id ?? default, gate.Error ?? Results.Forbid());
        }

        var profile = gate.Profile;
        var query = tracked ? db.FormDefinitions : db.FormDefinitions.AsNoTracking();
        var form = await query.FirstOrDefaultAsync(row => row.Id == formId && row.WorkspaceId == gate.Workspace.Id, ct);
        if (form is null)
        {
            return new FormGate(null, null, profile.Id, Results.NotFound());
        }

        if (form.Status == FormStatus.Draft && form.CreatedBy != profile.Id)
        {
            return new FormGate(null, null, profile.Id, Results.NotFound());
        }

        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(row => row.Id == form.ChannelId, ct);
        if (channel is null || !await CanReadChannelAsync(db, channel, profile.Id, ct))
        {
            return new FormGate(null, null, profile.Id, Results.Forbid());
        }

        return new FormGate(form, null, profile.Id, null);
    }

    private static async Task<FormGate> OpenSubmissionAsync(
        Guid workspaceId,
        Guid formId,
        Guid submissionId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct,
        bool tracked = false)
    {
        var opened = await OpenFormAsync(workspaceId, formId, http, db, tenant, clock, ct, tracked);
        if (opened.Error is not null)
        {
            return opened;
        }

        var query = tracked ? db.FormSubmissions : db.FormSubmissions.AsNoTracking();
        var submission = await query.FirstOrDefaultAsync(row => row.Id == submissionId && row.FormId == formId, ct);
        if (submission is null || !await CanSeeSubmissionAsync(db, opened.Form!, submission, opened.ProfileId, ct))
        {
            return new FormGate(opened.Form, null, opened.ProfileId, Results.NotFound());
        }

        return opened with { Submission = submission };
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
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        return workspace is null
            ? new WorkspaceGate(null, profile, Results.Forbid())
            : new WorkspaceGate(workspace, profile, null);
    }

    private static async Task<bool> ReviewersCanReadAsync(VibeChatDbContext db, Channel channel, FormSchema schema, CancellationToken ct)
    {
        foreach (var reviewer in schema.ReviewerUserIds)
        {
            if (!await CanReadChannelAsync(db, channel, new UserId(reviewer), ct))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<bool> CanReadChannelAsync(VibeChatDbContext db, Channel channel, UserId userId, CancellationToken ct)
    {
        var isWorkspaceMember = await db.WorkspaceMembers.IgnoreQueryFilters().AnyAsync(
            row => row.TenantId == channel.TenantId && row.WorkspaceId == channel.WorkspaceId && row.UserId == userId, ct);
        var isChannelMember = await db.ChannelMembers.IgnoreQueryFilters().AnyAsync(
            row => row.TenantId == channel.TenantId && row.ChannelId == channel.Id && row.UserId == userId && row.LeftAt == null, ct);
        if (!isWorkspaceMember && !isChannelMember)
        {
            return false;
        }

        return !channel.Type.RequiresChannelMembership() || isChannelMember;
    }

    private static void AddAudit(IAuditWriter audit, FormDefinition form, UserId actor, string action, DateTimeOffset at)
    {
        audit.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = form.TenantId,
            ActorUserId = actor,
            Action = action,
            EntityType = "Form",
            EntityId = form.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new { version = form.PublishedVersion, status = FormPolicies.StatusToken(form.Status) }),
            OccurredAt = at,
        });
    }

    private static void AddSubmissionAudit(IAuditWriter audit, FormSubmission submission, UserId actor, string action, DateTimeOffset at)
    {
        audit.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = submission.TenantId,
            ActorUserId = actor,
            Action = action,
            EntityType = "FormSubmission",
            EntityId = submission.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new { formId = submission.FormId, schemaVersion = submission.SchemaVersion, state = FormPolicies.StateToken(submission.State) }),
            OccurredAt = at,
        });
    }

    private static void AddOutbox(IOutboxWriter outbox, FormDefinition form, string type, DateTimeOffset at, Guid? submissionId)
    {
        outbox.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = form.TenantId,
            Type = type,
            OccurredAt = at,
            Payload = JsonSerializer.Serialize(new { formId = form.Id, submissionId, version = form.PublishedVersion }),
        });
    }

    private static IResult Bad(string code) => Results.BadRequest(new { error = code });

    private static IResult Disabled() => Results.NotFound(new { error = "FormDisabled" });

    private static IResult Conflict(string code) => Results.Json(new { error = code }, statusCode: StatusCodes.Status409Conflict);

    private sealed record WorkspaceGate(Workspace? Workspace, UserProfile? Profile, IResult? Error);

    private sealed record FormGate(FormDefinition? Form, FormSubmission? Submission, UserId ProfileId, IResult? Error);
}
